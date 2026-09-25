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

        /// <summary>
        /// RimWorld's <c>ConstructSuccessChance</c> by Construction level, at full Manipulation and Sight: 75%
        /// at level 0, 80%, 85%, then 2.5 points a level to 100% from level 8 (the wiki's
        /// <c>Construct_Success_Chance</c> table). Manipulation's and Sight's share of the stat is not ported,
        /// the same limitation as the speed curve above.
        /// <para/>
        /// This used to be an unsourced straight line from 50% at level 0 to 100% at level 20, so a level-8
        /// builder botched three attempts in ten where RimWorld's never does. Each botch refunds half the
        /// materials (<see cref="Frame.FailConstruction"/>): on seed 12345 of the storyteller bench, three of
        /// the first four bed frames failed and took twelve of the map's 52 logs with them, and once the map had
        /// trees about four storage hut attempts in ten were botched.
        /// </summary>
        public static readonly SimpleCurve SuccessChanceFromConstructionLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.75f),
            new CurvePoint(1, 0.80f),
            new CurvePoint(2, 0.85f),
            new CurvePoint(3, 0.875f),
            new CurvePoint(4, 0.90f),
            new CurvePoint(5, 0.925f),
            new CurvePoint(6, 0.95f),
            new CurvePoint(7, 0.975f),
            new CurvePoint(8, 1.0f),
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
