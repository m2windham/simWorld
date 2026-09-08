using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Spends work ticks on a fully-materialed <see cref="Frame"/> until it completes or fails (RimWorld:
    /// <c>RimWorld.JobDriver_ConstructFinishFrame</c>). Construction skill scales both how fast work
    /// accumulates and the odds of success — RimWorld's own <c>ConstructionSpeed</c>/<c>ConstructSuccessChance</c>
    /// stats do this through <c>StatDef.skillNeedFactors</c>, which this codebase's Stats module has not
    /// built yet (<c>docs/status.json</c>'s <c>work.stats</c> item says so explicitly). Reading the Construction
    /// skill level directly here and applying a curve in code is this module's workaround — see its report.
    /// </summary>
    public sealed class JobDriver_ConstructFinishFrame : JobDriver
    {
        /// <summary>Work-units applied per tick at skill level 0 before the skill factor; unsourced, pinned
        /// by a test on the trend (higher skill finishes sooner) rather than the literal.</summary>
        public const float BaseWorkPerTick = 1f;

        /// <summary>RimWorld's real ConstructionSpeed skillNeedFactors curve is not sourced here (see this
        /// module's report); shape only (skill increases speed) is what this port claims.</summary>
        public static readonly SimpleCurve WorkSpeedFactorFromConstructionLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.4f),
            new CurvePoint(20, 2.0f),
        });

        /// <summary>RimWorld's real ConstructSuccessChance skillNeedFactors curve is likewise not sourced
        /// here; shape only (higher skill fails less often) is what this port claims.</summary>
        public static readonly SimpleCurve SuccessChanceFromConstructionLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.5f),
            new CurvePoint(20, 1.0f),
        });

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            work.FailOnDespawnedOrNull(TargetIndex.A);
            work.tickAction = () =>
            {
                var frame = (Frame)work.Job.GetTarget(TargetIndex.A).Thing!;
                int skillLevel = work.Pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
                float speedFactor = WorkSpeedFactorFromConstructionLevel.Evaluate(skillLevel);
                frame.workDone += BaseWorkPerTick * speedFactor;
                if (frame.workDone < frame.WorkToBuild) return;

                float successChance = SuccessChanceFromConstructionLevel.Evaluate(skillLevel);
                if (Rand.Chance(successChance))
                {
                    frame.CompleteConstruction(work.Pawn);
                }
                else
                {
                    frame.FailConstruction(work.Pawn);
                }
                work.actor.ReadyForNextToil();
            };
            yield return work;
        }
    }
}
