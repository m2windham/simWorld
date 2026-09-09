using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Walks to a harvestable <see cref="Plant"/> and harvests it (RimWorld: <c>RimWorld.JobDriver_Harvest</c>).
    /// Same skill-scaled work shape as <see cref="JobDriver_Sow"/>.
    /// </summary>
    public sealed class JobDriver_Harvest : JobDriver
    {
        /// <summary>Unsourced, same shape as <see cref="JobDriver_Sow.SowWorkAmount"/>.</summary>
        public const float HarvestWorkAmount = 300f;

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
                if (workDone < HarvestWorkAmount) return;

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
