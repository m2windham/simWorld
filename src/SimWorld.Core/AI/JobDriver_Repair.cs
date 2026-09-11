using System;
using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a damaged building, walks adjacent to it and restores its
    /// <see cref="Thing.HitPoints"/> one point at a time until it is whole, then ends
    /// (RimWorld: <c>RimWorld.JobDriver_Repair</c>).
    /// <para/>
    /// <b>The two tick counts are RimWorld's own shape, recalled rather than sourced.</b> RimWorld waits a
    /// longer lead-in before the first point lands and a shorter interval between every point after it,
    /// counting down by the worker's <c>ConstructionSpeed</c> stat each tick. The lead-in/interval split is
    /// the part that matters — it is what makes starting a repair cost more than continuing one, so a pawn
    /// bouncing between two damaged buildings gets less done than one seeing a single repair through — and
    /// that trend is what this module's tests pin, not these literals (CLAUDE.md: pin the behaviour, not the
    /// number you cannot source).
    /// <para/>
    /// <b><c>ConstructionSpeed</c> stands in as a skill curve, as elsewhere in this codebase.</b> RimWorld
    /// reads <c>actor.GetStatValue(StatDefOf.ConstructionSpeed)</c>, which resolves through
    /// <c>StatDef.skillNeedFactors</c> — machinery the Stats module here has not built yet (see
    /// <see cref="JobDriver_ConstructFinishFrame"/>'s own remarks, which hit exactly this and worked around it
    /// the same way). This driver reuses that class's
    /// <see cref="JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel"/> curve directly rather
    /// than declaring a second one with different unsourced literals: in RimWorld both of these really are the
    /// one <c>ConstructionSpeed</c> stat, so one curve is the more faithful port as well as the smaller one.
    /// </summary>
    public sealed class JobDriver_Repair : JobDriver
    {
        /// <summary>Ticks of work (at <c>ConstructionSpeed</c> 1) before the first hit point is restored —
        /// see the class doc on why this is unsourced and what pins it instead.</summary>
        public const float TicksBeforeFirstRepair = 80f;

        /// <summary>Ticks of work (at <c>ConstructionSpeed</c> 1) between every hit point after the first.</summary>
        public const float TicksBetweenRepairs = 20f;

        /// <summary>Construction xp per tick spent repairing (RimWorld: <c>JobDriver_Repair</c>'s own
        /// per-tick <c>SkillDefOf.Construction</c> learn call; the rate itself is unsourced, and the test
        /// only asserts that repairing teaches the repairer something).</summary>
        public const float RepairXpPerTick = 0.05f;

        /// <summary>Counts down by the worker's construction speed each tick; a field rather than a captured
        /// local (the shape <see cref="JobDriver_Harvest"/> uses) precisely because the toil's
        /// <c>initAction</c> has to reset it — re-entering the repair toil must pay the lead-in again.</summary>
        private float ticksToNextRepair;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var repair = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            repair.FailOnDespawnedOrNull(TargetIndex.A);
            repair.initAction = () => ticksToNextRepair = TicksBeforeFirstRepair;
            repair.tickAction = () =>
            {
                Thing? target = repair.Job.GetTarget(TargetIndex.A).Thing;
                if (target == null) return;

                // Someone else may have finished it while this pawn was walking over. Nothing left to do is
                // a success, not a failure — RimWorld ends the job the same way the moment HitPoints tops out.
                if (target.HitPoints >= target.MaxHitPoints)
                {
                    repair.actor.ReadyForNextToil();
                    return;
                }

                SkillRecord? skill = repair.Pawn.skills?.GetSkill(SkillDefOf.Construction);
                skill?.Learn(RepairXpPerTick);
                ticksToNextRepair -= JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel
                    .Evaluate(skill?.Level ?? 0);
                if (ticksToNextRepair > 0f) return;

                ticksToNextRepair = TicksBetweenRepairs;
                target.HitPoints = Math.Min(target.HitPoints + 1, target.MaxHitPoints);
                if (target.HitPoints >= target.MaxHitPoints) repair.actor.ReadyForNextToil();
            };
            yield return repair;
        }
    }
}
