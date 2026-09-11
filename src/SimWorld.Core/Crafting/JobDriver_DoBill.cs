using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Spends work ticks on the bill <see cref="WorkGiver_DoBill"/> found runnable, then consumes ingredients
    /// and spawns the product (RimWorld: <c>RimWorld.JobDriver_DoBill</c>, trimmed to this port's
    /// single-target <see cref="Job"/> — see <see cref="WorkGiver_DoBill"/>'s own remarks for why ingredient
    /// delivery is not this driver's job). One iteration per job, the same shape every other work-and-produce
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

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            work.FailOnDespawnedOrNull(TargetIndex.A);
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

                if (!WorkGiver_DoBill.TryFindIngredients(billGiverThing, bill, out List<(Thing thing, int count)> takes))
                {
                    // Someone else emptied the pile while this pawn worked — a genuine, if rare, race between
                    // the offer and the finish (see WorkGiver_DoBill.TryFindIngredients's own remarks).
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
    }
}
