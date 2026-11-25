using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Fetches the ingredient stacks <see cref="WorkGiver_DoBill"/> chose to the bench, spends work ticks on the
    /// bill, then consumes them and spawns the product (RimWorld: <c>RimWorld.JobDriver_DoBill</c>). The stacks
    /// arrive on the job in <see cref="Job.targetQueueB"/>/<see cref="Job.countQueue"/> and are reserved by
    /// <see cref="Toils_Reserve.ReserveQueue"/> before the first step, which is what keeps a second bench's
    /// bill, or a hauler, from spending the same pile mid-recipe.
    /// <para/>
    /// <b>Collecting the ingredients</b> (RimWorld: <c>JobDriver_DoBill.CollectIngredientsToils</c>). For each
    /// queued stack the crafter walks to it, picks up the counted part through its
    /// <see cref="Pawn_CarryTracker"/> (<c>Toils_Haul.StartCarryThing</c>), walks to the bench and puts it down
    /// on a free cell beside it (<c>Toils_JobTransforms.SetTargetToIngredientPlaceCell</c> and
    /// <c>Toils_Haul.PlaceHauledThingInCell</c>). Anything interrupted on the way is put down at the crafter's
    /// feet by the job tracker, never lost. RimWorld loops one block of toils with
    /// <c>Toils_JobTransforms.ExtractNextTargetFromQueue</c> and <c>Toils_Jump.JumpIfHaveTargetInQueue</c>; this
    /// port has no jump toil, so the block is written out once per queued stack when the job starts, which visits
    /// the same stacks in the same order. A stack already lying beside the bench is not carried at all, and a
    /// stack in hand goes straight to the bench, so a driver rebuilt after a load picks up where it was.
    /// <para/>
    /// <b>Not ported:</b> RimWorld's <c>JumpToCollectNextIntoHandsForBill</c>, which picks up a second nearby
    /// stack of the same def into the same hands; the carry tracker here holds one Thing, so each stack is its
    /// own trip. Recipes in this port take one or two ingredients, so the cost is a few extra walks.
    /// <para/>
    /// One iteration per job, the same shape every other work-and-produce driver this port ships uses
    /// (<see cref="Building.JobDriver_ConstructFinishFrame"/>, <see cref="Building.JobDriver_Harvest"/>):
    /// <see cref="AI.JobGiver_Work"/> simply re-issues this WorkGiver on its next scan if the bill still wants
    /// doing.
    /// </summary>
    public sealed class JobDriver_DoBill : JobDriver
    {
        /// <summary>
        /// Work-units applied per tick at skill level 0, before <see cref="WorkSpeedFactorFromSkillLevel"/>.
        /// Same shape and the same caveat as
        /// <see cref="Building.JobDriver_ConstructFinishFrame.BaseWorkPerTick"/>: RimWorld scales a bill's
        /// pace through <c>StatDefOf.WorkSpeedGlobal</c>'s skillNeedFactors curve (system: work.stats), which
        /// this port's Stats module has not wired any job driver to yet. Reading the recipe's own
        /// <see cref="RecipeDef.workSkill"/> level directly and applying an unsourced curve is this driver's
        /// workaround; pinned by trend (a better crafter finishes sooner), not by literal.
        /// </summary>
        public const float BaseWorkPerTick = 1f;

        /// <summary>How far out from the bench, in cells, a delivered ingredient may be put down and still count as
        /// at the bench. RimWorld tries the bench's own cells and then the radial pattern around its interaction
        /// cell; this port has no interaction cell and does not stack items on a bench, so it uses the ring
        /// around the bench and one more. Not sourced.</summary>
        public const int PlaceReach = 2;

        /// <summary>Unsourced shape only, matching <see cref="Building.JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel"/>'s own disclosure.</summary>
        public static readonly SimpleCurve WorkSpeedFactorFromSkillLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.4f),
            new CurvePoint(20, 1.8f),
        });

        /// <summary>
        /// How fast <paramref name="pawn"/> works <paramref name="recipe"/>, per tick. A recipe naming a
        /// <see cref="RecipeDef.workSpeedStat"/> is worked at that stat's value, as RimWorld's
        /// <c>Toils_Recipe.DoRecipeWork</c> does (<c>actor.GetStatValue(recipe.workSpeedStat)</c>), so the
        /// stat's own skill and capacity factors decide the pace; one that names none keeps this driver's
        /// unsourced skill curve above.
        /// </summary>
        public static float WorkSpeedFor(Pawn pawn, RecipeDef recipe)
        {
            if (recipe.workSpeedStat != null) return pawn.GetStatValue(recipe.workSpeedStat);
            int skillLevel = recipe.workSkill != null ? pawn.skills?.GetSkill(recipe.workSkill)?.Level ?? 0 : 0;
            return WorkSpeedFactorFromSkillLevel.Evaluate(skillLevel);
        }

        /// <summary>
        /// The bench and every ingredient stack the giver chose must be claimable before this job starts
        /// (RimWorld: <c>JobDriver_DoBill.TryMakePreToilReservations</c> reserves target A and then calls
        /// <c>ReserveAsManyAsPossible</c> over target queue B).
        /// <b>Deviation:</b> RimWorld tolerates losing some of the queue there and works with whatever reached
        /// the bench. Here an ingredient that cannot be claimed is one the recipe will not have at the finish
        /// line, so the job is refused now rather than after the walk.
        /// </summary>
        public override bool TryMakePreToilReservations()
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (!map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))) return false;

            List<LocalTargetInfo> ingredients = job.GetTargetQueue(TargetIndex.B);
            for (int i = 0; i < ingredients.Count; i++)
            {
                if (!map.reservationManager.CanReserve(pawn, ingredients[i])) return false;
            }
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.ReserveQueue(TargetIndex.B);

            // Which bill this job is for. The job does not carry the bill (RimWorld's Job.bill has no field
            // here), so it is the first startable bill on the bench that the queued stacks pay for: the one the
            // giver picked, since it walks the same stack in the same order. Fixed for the job, so a bill that
            // stops wanting work during the long walk ends the job instead of handing its ingredients to the
            // next bill's recipe.
            RecipeDef? recipe = RecipeForQueue();

            int queued = job.GetTargetQueue(TargetIndex.B).Count;
            for (int i = 0; i < queued; i++)
            {
                foreach (Toil toil in CollectIngredientToils(i)) yield return toil;
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            work.FailOnDespawnedOrNull(TargetIndex.A);
            // An ingredient vanishing under a live claim should not happen, but a recipe cannot finish without
            // it either; stop the moment it does rather than working to the end and finding out (RimWorld:
            // JobDriver_DoBill.FailOnDespawnedNullOrForbiddenPlacedThings).
            work.FailOn(() => !IngredientsStillPresent(work.Job));
            float workDone = 0f;
            work.tickAction = () =>
            {
                Thing billGiverThing = work.Job.GetTarget(TargetIndex.A).Thing!;
                CompBillGiver? comp = WorkGiver_DoBill.BillGiverFor(billGiverThing);
                Bill_Production? bill = comp != null && recipe != null ? StartableBillFor(work.Pawn, comp, recipe) : null;
                if (bill == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                workDone += BaseWorkPerTick * WorkSpeedFor(work.Pawn, bill.recipe);
                if (workDone < bill.recipe.WorkAmountTotal()) return;

                if (!TryTakeReservedIngredients(work.Job, out List<(Thing thing, int count)> takes))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var consumed = new List<ItemStack>(takes.Count);
                foreach ((Thing ingredient, int count) in takes)
                {
                    consumed.Add(new ItemStack(ingredient.def, count, ingredient.Stuff));
                    ingredient.stackCount -= count;
                    if (ingredient.stackCount <= 0) ingredient.Destroy(DestroyMode.Vanish);
                }

                ThingDef? dominant = GenRecipe.DominantIngredient(consumed);
                List<ItemStack> products = GenRecipe.MakeRecipeProducts(bill.recipe, work.Pawn, consumed, dominant);

                Map.Map map = work.Pawn.Map!;
                IntVec3 dropAt = billGiverThing.Position;
                for (int i = 0; i < products.Count; i++)
                {
                    ItemStack product = products[i];
                    Thing spawned = ThingMaker.MakeThing(product.Def, product.Stuff, product.Quality);
                    spawned.stackCount = product.Count;
                    GenSpawn.Spawn(spawned, dropAt, map);
                }

                bill.Notify_IterationCompleted(work.Pawn, consumed);
                work.actor.ReadyForNextToil();
            };
            yield return work;
        }

        /// <summary>
        /// Walk to queued stack <paramref name="index"/>, pick up its count, walk to the bench, put it down beside
        /// it (RimWorld: one pass of <c>CollectIngredientsToils</c>). Each step passes over a stack that is
        /// already beside the bench, and the first two over one already in hand.
        /// </summary>
        private IEnumerable<Toil> CollectIngredientToils(int index)
        {
            // Toils_Goto.GotoThing's shape, less its fail-on-despawned: a stack in hand is off the map.
            var gotoIngredient = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            gotoIngredient.initAction = () =>
            {
                if (AtBench(index) || Carrying(index)) return;
                pawn.pather.StartPath(Queue[index], PathEndMode.ClosestTouch);
            };
            gotoIngredient.FailOn(() =>
            {
                if (AtBench(index) || Carrying(index)) return false;
                Thing? ingredient = Queue[index].Thing;
                return ingredient == null || ingredient.Destroyed || !ingredient.Spawned || pawn.pather.Failed;
            });
            gotoIngredient.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoIngredient;

            yield return Toils_General.Do(() =>
            {
                if (AtBench(index) || Carrying(index)) return;
                Thing source = Queue[index].Thing!;
                int count = Count(index);
                // RimWorld: Toils_Haul.StartCarryThing with failIfStackCountLessThanJobCount.
                if (count <= 0 || source.stackCount < count || pawn.carryTracker.TryStartCarry(source, count) < count)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // As StartCarryThing re-points the haulable target at what is now in hand. A part of a stack is a
                // new Thing: the claim moves onto it, and the rest of the pile is free for others again.
                Thing carried = pawn.carryTracker.CarriedThing!;
                if (!ReferenceEquals(carried, source))
                {
                    Map.Map map = pawn.Map!;
                    map.reservationManager.Release(pawn, source);
                    map.reservationManager.Reserve(pawn, carried);
                }
                Queue[index] = carried;
            });

            var gotoBench = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            gotoBench.initAction = () =>
            {
                if (AtBench(index)) return;
                pawn.pather.StartPath(job.GetTarget(TargetIndex.A), PathEndMode.Touch);
            };
            gotoBench.FailOn(() => !AtBench(index) && (!Carrying(index) || pawn.pather.Failed));
            gotoBench.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoBench;

            yield return Toils_General.Do(() =>
            {
                if (AtBench(index)) return;
                if (!Carrying(index) || !TryFindPlaceCell(out IntVec3 cell)
                    || !pawn.carryTracker.TryDropCarriedThing(cell, out Thing? placed) || placed == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Queue[index] = placed;
            });
        }

        private List<LocalTargetInfo> Queue => job.GetTargetQueue(TargetIndex.B);

        private int Count(int index) => job.countQueue != null && index < job.countQueue.Count ? job.countQueue[index] : 0;

        private bool Carrying(int index)
        {
            Thing? carried = pawn.carryTracker.CarriedThing;
            return carried != null && ReferenceEquals(carried, Queue[index].Thing);
        }

        /// <summary>Queued stack <paramref name="index"/> lies on the map within <see cref="PlaceReach"/> of the
        /// bench's footprint: nothing to fetch.</summary>
        private bool AtBench(int index)
        {
            Thing? ingredient = Queue[index].Thing;
            Thing? bench = job.GetTarget(TargetIndex.A).Thing;
            if (ingredient == null || bench == null || ingredient.Destroyed || !ingredient.Spawned || !bench.Spawned) return false;
            return bench.OccupiedRect().ExpandedBy(PlaceReach).Contains(ingredient.Position);
        }

        /// <summary>
        /// The nearest cell to the bench, within <see cref="PlaceReach"/> of it, that is standable, holds no item
        /// and the crafter can walk to (RimWorld: <c>Toils_JobTransforms.SetTargetToIngredientPlaceCell</c>, which
        /// walks <c>IngredientPlaceCellsInOrder</c> for a cell with no item it cannot stack with). Recalled, not
        /// read: this port puts one stack per cell and does not merge into a stack already there.
        /// </summary>
        private bool TryFindPlaceCell(out IntVec3 cell)
        {
            Thing bench = job.GetTarget(TargetIndex.A).Thing!;
            Map.Map map = pawn.Map!;
            CellRect near = bench.OccupiedRect().ExpandedBy(PlaceReach);
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            // Out far enough to cover the whole expanded footprint, corners included; the rect test trims it.
            int cells = GenRadial.NumCellsInRadius(1.5f * (PlaceReach + System.Math.Max(bench.Size.x, bench.Size.z)));
            for (int i = 0; i < cells; i++)
            {
                IntVec3 c = bench.Position + pattern[i];
                if (!near.Contains(c)) continue;
                if (!GenGrid.InBounds(c, map) || !GenGrid.Standable(c, map) || HoldsItem(map, c)) continue;
                if (!Reachability.CanReach(pawn, c, PathEndMode.OnCell)) continue;
                cell = c;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        private static bool HoldsItem(Map.Map map, IntVec3 c)
        {
            foreach (Thing t in map.thingGrid.ThingsAt(c))
            {
                if (t.def.category == ThingCategory.Item) return true;
            }
            return false;
        }

        /// <summary>The recipe of the first bill on the bench this pawn may start that the queued stacks pay
        /// for, or null.</summary>
        private RecipeDef? RecipeForQueue()
        {
            CompBillGiver? comp = WorkGiver_DoBill.BillGiverFor(job.GetTarget(TargetIndex.A).Thing!);
            if (comp == null) return null;
            List<LocalTargetInfo> queue = Queue;
            var stacks = new List<ItemStack>(queue.Count);
            for (int i = 0; i < queue.Count; i++)
            {
                Thing? t = queue[i].Thing;
                if (t != null) stacks.Add(new ItemStack(t.def, Count(i)));
            }

            var chosen = new List<ItemStack>();
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is not Bill_Production bill || !WorkGiver_DoBill.CanStart(pawn, bill)) continue;
                bool needsNothing = bill.recipe.ingredients == null || bill.recipe.ingredients.Count == 0;
                if (needsNothing ? stacks.Count == 0 : BillIngredientsFinder.TryFindBestIngredients(bill, stacks, chosen))
                {
                    return bill.recipe;
                }
            }
            return null;
        }

        private static Bill_Production? StartableBillFor(Pawn pawn, CompBillGiver comp, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i] is Bill_Production bill && ReferenceEquals(bill.recipe, recipe) && WorkGiver_DoBill.CanStart(pawn, bill))
                {
                    return bill;
                }
            }
            return null;
        }

        /// <summary>
        /// Every entry of <see cref="Job.targetQueueB"/> is still on the map holding at least the count this
        /// job claimed for it. Runs once a tick as a fail condition, so it answers without allocating; the
        /// list itself is only built at the finish, by <see cref="TryTakeReservedIngredients"/>.
        /// </summary>
        private static bool IngredientsStillPresent(Job job)
        {
            List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
            List<int>? counts = job.countQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                Thing? ingredient = queue[i].Thing;
                int count = counts != null && i < counts.Count ? counts[i] : 0;
                if (ingredient == null || ingredient.Destroyed || !ingredient.Spawned || count <= 0 || ingredient.stackCount < count)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Pairs up <see cref="Job.targetQueueB"/> with <see cref="Job.countQueue"/> into the stacks this job
        /// reserved and how much of each it may spend: by now the stacks put down beside the bench. Answers
        /// false and hands back nothing if any entry has gone, so a recipe is never paid for half its
        /// ingredients.
        /// </summary>
        private static bool TryTakeReservedIngredients(Job job, out List<(Thing thing, int count)> takes)
        {
            List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
            takes = new List<(Thing, int)>(queue.Count);
            if (!IngredientsStillPresent(job)) return false;

            List<int>? counts = job.countQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                takes.Add((queue[i].Thing!, counts![i]));
            }
            return true;
        }
    }
}
