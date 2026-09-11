using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a plant, works on it for a while and then clears it, yielding whatever it had grown
    /// (RimWorld: <c>RimWorld.JobDriver_PlantWork</c>). RimWorld keeps that class abstract and hangs a
    /// one-line <c>JobDriver_PlantCut</c> off it; cutting is the only plant work this driver serves here
    /// (sowing and harvesting already have <see cref="JobDriver_Sow"/> and <see cref="JobDriver_Harvest"/>),
    /// so the pair is folded into this one class rather than ported as a base plus an empty leaf.
    /// <para/>
    /// <b>The yield goes through <see cref="Plant.Harvest"/> — the same single path
    /// <see cref="WorkGiver_GrowerHarvest"/>'s own driver uses</b>, rather than a second cut-specific one.
    /// That has one visible consequence worth stating: RimWorld distinguishes collecting from cutting and
    /// gives a plant below its <c>harvestMinGrowth</c> nothing at all when cut, whereas
    /// <see cref="Plant.Harvest"/> deliberately folds the two together (its own doc says so) and always
    /// scales the yield by actual growth. So a half-grown crop cut out of a growing zone here drops about
    /// half a crop instead of nothing. Re-splitting that distinction would mean reversing a decision the
    /// plant-growth module already made and documented, for a case where this port's behaviour is the more
    /// forgiving of the two; the report notes it rather than the code quietly forking.
    /// </summary>
    public sealed class JobDriver_PlantWork : JobDriver
    {
        /// <summary>Work-units a cut takes. RimWorld reads <c>plant.def.plant.harvestWork</c> for both
        /// cutting and harvesting — one number, one meaning — and this port's
        /// <see cref="SimWorld.Defs.PlantProperties"/> carries no such field, so this deliberately *is*
        /// <see cref="JobDriver_Harvest.HarvestWorkAmount"/> rather than a second unsourced literal that
        /// would imply the two differ.</summary>
        public const float CutWorkAmount = JobDriver_Harvest.HarvestWorkAmount;

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
                int skillLevel = work.Pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                workDone += PlantUtility.WorkSpeedFactorFromPlantsLevel.Evaluate(skillLevel);
                if (workDone < CutWorkAmount) return;

                if (work.Job.GetTarget(TargetIndex.A).Thing is Plant plant && plant.Spawned)
                {
                    plant.Harvest(work.Pawn);
                }
                work.actor.ReadyForNextToil();
            };
            yield return work;
        }
    }
}
