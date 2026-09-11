using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Spends work ticks on the bill <see cref="WorkGiver_DoBill"/> found runnable, then consumes the exact
    /// ingredient stacks that bill was offered on and spawns the product (RimWorld:
    /// <c>RimWorld.JobDriver_DoBill</c>, minus the walk out to fetch them — see <see cref="WorkGiver_DoBill"/>'s
    /// own remarks for why ingredient delivery is not this driver's job). Those stacks arrive on the job in
    /// <see cref="Job.targetQueueB"/>/<see cref="Job.countQueue"/> and are reserved by
    /// <see cref="Toils_Reserve.ReserveQueue"/> before a single tick of work is spent, which is what keeps a
    /// second bench's bill — or a hauler — from spending the same pile mid-recipe.
    /// One iteration per job, the same shape every other work-and-produce
    /// driver this port ships uses (<see cref="Building.JobDriver_ConstructFinishFrame"/>,
    /// <see cref="Building.JobDriver_Harvest"/>): <see cref="AI.JobGiver_Work"/> simply re-issues this
    /// WorkGiver on its next scan if the bill still wants doing.
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

        /// <summary>Unsourced shape only, matching <see cref="Building.JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel"/>'s own disclosure.</summary>
        public static readonly SimpleCurve WorkSpeedFactorFromSkillLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.4f),
            new CurvePoint(20, 1.8f),
        });

        /// <summary>
        /// The bench and every ingredient stack the giver chose must be claimable before this job starts
        /// (RimWorld: <c>JobDriver_DoBill.TryMakePreToilReservations</c> reserves target A and then calls
        /// <c>ReserveAsManyAsPossible</c> over target queue B).
        /// <b>Deviation:</b> RimWorld tolerates losing some of the queue there, because its pawn walks out and
        /// picks the ingredients up — whatever it is holding when it reaches the bench is what the recipe gets.
        /// This port leaves them on the ground until the recipe finishes (see <see cref="WorkGiver_DoBill"/>'s
        /// remarks), so an ingredient it cannot claim is one the recipe will not have at the finish line, and
        /// the honest answer is to refuse the job now rather than burn the work.
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
                Bill_Production? bill = comp != null ? WorkGiver_DoBill.FindBill(work.Pawn, comp) : null;
                if (bill == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int skillLevel = bill.recipe.workSkill != null
                    ? work.Pawn.skills?.GetSkill(bill.recipe.workSkill)?.Level ?? 0
                    : 0;
                workDone += BaseWorkPerTick * WorkSpeedFactorFromSkillLevel.Evaluate(skillLevel);
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
        /// reserved and how much of each it may spend — the port's stand-in for RimWorld reading the
        /// ingredients out of the pawn's carry tracker, where they would be by now. Answers false and hands
        /// back nothing if any entry has gone, so a recipe is never paid for half its ingredients.
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
