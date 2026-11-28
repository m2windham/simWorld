using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Stats;

namespace SimWorld.Filth
{
    /// <summary>
    /// Walks to each pile of filth the job carries in turn and scrubs it away a layer at a time (RimWorld:
    /// <c>RimWorld.JobDriver_CleanFilth</c>).
    /// <para/>
    /// <b>The queue.</b> <see cref="WorkGiver_CleanFilth.JobOnThing"/> hands over up to fifteen piles from one
    /// room: the first in <see cref="Job.targetA"/>, the rest in <see cref="Job.targetQueueA"/>. RimWorld's
    /// driver loops <c>ClearDespawnedNullOrForbiddenQueuedTargets</c>, <c>ExtractNextTargetFromQueue</c>, a walk
    /// and the scrub, and jumps back to the start after each pile; this does the same with one toil,
    /// <c>next</c>, that drops what is gone or no longer inside <see cref="CleaningBounds"/> and takes the next
    /// pile. Unlike RimWorld's, it keeps the pile it is on in <see cref="Job.targetA"/> until that pile is done,
    /// so a driver rebuilt after a load (drivers are not saved here) carries on with it instead of losing it.
    /// <para/>
    /// Work is counted the way RimWorld counts it: a running total that ticks up by the cleaner's
    /// <c>CleaningSpeed</c>, and every time it passes the def's
    /// <see cref="Defs.FilthProperties.cleaningWorkToReduceThickness"/> one point of
    /// <see cref="Filth.thickness"/> comes off. A three-thick pile therefore costs three times what a fresh
    /// one does, which is the whole point of thickness existing — filth that has been walked over all day is
    /// a real job, not the same flat cost as a single footprint.
    /// <para/>
    /// <b>Cleaning has no skill.</b> This driver teaches the cleaner nothing and consults no
    /// <see cref="Work.SkillDef"/>, which is RimWorld's own design (there is no Cleaning skill in the game)
    /// and the reason <c>CleaningSpeed</c> ships with <c>capacityFactors</c> but no <c>skillNeedFactors</c> —
    /// a cleaner is only ever slowed by injury, never sped up by practice. It is the one wired work giver in
    /// this codebase that does not call <c>skills.Learn</c>, so the absence is deliberate rather than missed.
    /// </summary>
    public sealed class JobDriver_CleanFilth : JobDriver
    {
        /// <summary>Work accumulated toward the next layer; reset each time one comes off. A field rather
        /// than a captured local because the toil's <c>initAction</c> has to clear it when the toil is
        /// re-entered, the same shape <see cref="JobDriver_Repair"/> uses.</summary>
        private float cleaningWorkDone;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            // RimWorld: TryMakePreToilReservations calls ReserveAsManyAsPossible over the queue. A pile somebody
            // else already holds is left to them and dropped from this job.
            var reserveQueue = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            reserveQueue.initAction = () =>
            {
                Map.Map? map = pawn.Map;
                List<LocalTargetInfo>? queue = job.targetQueueA;
                if (map == null || queue == null) return;
                queue.RemoveAll(t => !map.reservationManager.Reserve(pawn, t));
            };
            yield return reserveQueue;

            var next = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            next.initAction = () =>
            {
                List<LocalTargetInfo>? queue = job.targetQueueA;
                while (!StillWork(job.targetA))
                {
                    if (queue == null || queue.Count == 0)
                    {
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                    job.targetA = queue[0];
                    queue.RemoveAt(0);
                }
            };
            yield return next;

            // Toils_Goto.GotoThing, except that a pile cleaned away by somebody else on the way is skipped rather
            // than failing the job (RimWorld: JumpIfDespawnedOrNullOrForbidden and JumpIfOutsideHomeArea).
            var walk = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            walk.initAction = () => pawn.pather.StartPath(job.targetA, PathEndMode.Touch);
            walk.FailOn(() => pawn.pather.Failed);
            walk.tickAction = () =>
            {
                if (!StillWork(job.targetA)) JumpToToil(next);
            };
            yield return walk;

            var clean = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            clean.initAction = () => cleaningWorkDone = 0f;
            clean.tickAction = () =>
            {
                if (!StillWork(job.targetA))
                {
                    // Somebody else finished it, or it rotted away underfoot while we walked over. Nothing
                    // left to clean is not a failure — the cell is clean either way — so on to the next.
                    JumpToToil(next);
                    return;
                }
                var filth = (Filth)job.targetA.Thing!;

                cleaningWorkDone += pawn.GetStatValue(CleaningStatDefOf.CleaningSpeed);
                if (cleaningWorkDone < filth.Props.cleaningWorkToReduceThickness) return;

                cleaningWorkDone = 0f;
                filth.ThinFilth();
                if (filth.Destroyed) JumpToToil(next);
            };
            yield return clean;
        }

        /// <summary>A pile still standing inside the region colonists clean.</summary>
        private static bool StillWork(LocalTargetInfo target) =>
            target.Thing is Filth filth && !filth.Destroyed && CleaningBounds.IsCleanable(filth);
    }
}
