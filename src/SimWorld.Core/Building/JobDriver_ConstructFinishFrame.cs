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

        /// <summary>
        /// How coarsely a Frame's progress is bucketed before <see cref="Map.View.MapViewTracker.Notify_ChunkChangedAt"/>
        /// is called for it. Not a RimWorld number: <c>Frame.workDone</c> has no view-layer equivalent there,
        /// because RimWorld's renderer reads the live Thing directly. It exists here because <c>Map/View</c>'s
        /// chunk versions are, by design, <i>not</i> bumped for an in-place field write (see
        /// <c>MapViewTracker</c>'s own remarks and <c>docs/perf/map-view.md</c>'s "What this model does not
        /// track" — a stack count, hit points and plant growth all take this same silent path today).
        /// <see cref="Frame.workDone"/> advancing every tick a builder works it is exactly that kind of write,
        /// so without an explicit notify here a frame's progress would never reach a host at all; calling it
        /// every tick, though, would dirty the chunk once per tick for the whole build — for a level-20
        /// builder on a Bed that is ~90 calls where a handful would do — which is the "adding a notification
        /// means paying for a redraw nobody asked for" cost that doc explicitly warns against, and it is what
        /// <c>MapViewIntegrationTests.A_ticking_settlement_re_sends_a_small_fraction_of_its_chunks</c> would
        /// catch.
        /// <para/>
        /// <b>20, not fewer.</b> This is deliberately <i>not</i> keyed to any host's art thresholds — <c>Map/View</c>
        /// does not know what stages a host draws a frame in, and should not have to be changed when a host
        /// changes its art. 20 buckets is 5 percentage points apiece: fine enough that whatever threshold a
        /// host picks to switch construction art, the notify carrying a frame across it lands within 5 points
        /// of the true crossing — coarse enough that one frame's build dirties its chunk at most ~20 times
        /// over its whole life rather than once a tick, whatever <see cref="Frame.WorkToBuild"/> is.
        /// </summary>
        public const int BuildProgressNotifyBuckets = 20;

        private static int BuildProgressBucket(float percentComplete)
        {
            int bucket = (int)(Clamp01(percentComplete) * BuildProgressNotifyBuckets);
            return bucket >= BuildProgressNotifyBuckets ? BuildProgressNotifyBuckets - 1 : bucket;
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

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
                int bucketBefore = BuildProgressBucket(frame.PercentComplete);
                frame.workDone += BaseWorkPerTick * speedFactor;
                if (BuildProgressBucket(frame.PercentComplete) != bucketBefore)
                {
                    // Frame.workDone is a plain field write, not a ThingGrid call, so nothing else marks its
                    // chunk changed for it — see BuildProgressNotifyBuckets above.
                    frame.Map?.mapView.Notify_ChunkChangedAt(frame.Position);
                }
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
