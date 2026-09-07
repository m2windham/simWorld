using System;
using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// One state of a <see cref="JobDriver"/>'s toil state machine (RimWorld: <c>Verse.AI.Toil</c>).
    /// <see cref="initAction"/> runs once on entry; <see cref="tickAction"/> runs every tick the toil is
    /// current; <see cref="defaultCompleteMode"/> says how the driver knows the toil is done.
    /// </summary>
    public sealed class Toil
    {
        /// <summary>Set by <see cref="JobDriver.Notify_Starting"/> once this toil joins a driver's list.</summary>
        public JobDriver actor = null!;

        public Action? initAction;
        public Action? tickAction;

        public ToilCompleteMode defaultCompleteMode = ToilCompleteMode.Instant;

        /// <summary>Ticks to wait when <see cref="defaultCompleteMode"/> is <see cref="ToilCompleteMode.Delay"/>.</summary>
        public int defaultDuration;

        private List<Func<bool>>? failConditions;

        public Pawn Pawn => actor.pawn;

        public Job Job => actor.job;

        /// <summary>Adds a condition that ends the job as <see cref="JobCondition.Incompletable"/> the moment it's true.</summary>
        public Toil FailOn(Func<bool> predicate)
        {
            failConditions ??= new List<Func<bool>>();
            failConditions.Add(predicate ?? throw new ArgumentNullException(nameof(predicate)));
            return this;
        }

        /// <summary>Fails once the named target's Thing is destroyed or de-spawned (never fails for a bare-cell target).</summary>
        public Toil FailOnDespawnedOrNull(TargetIndex ind) => FailOn(() =>
        {
            LocalTargetInfo t = Job.GetTarget(ind);
            return t.HasThing && (t.Thing!.Destroyed || !t.Thing.Spawned);
        });

        /// <summary>Fails once the named target can no longer be reached at all.</summary>
        public Toil FailOnCannotReach(TargetIndex ind, PathEndMode peMode) => FailOn(() =>
            !Reachability.CanReach(Pawn, Job.GetTarget(ind), peMode));

        internal bool ShouldFail()
        {
            if (failConditions == null) return false;
            for (int i = 0; i < failConditions.Count; i++)
            {
                if (failConditions[i]()) return true;
            }
            return false;
        }
    }
}
