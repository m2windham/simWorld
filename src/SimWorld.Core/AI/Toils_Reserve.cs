using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>Toils that claim or release a <see cref="ReservationManager"/> target (RimWorld: <c>Verse.AI.Toils_Reserve</c>).</summary>
    public static class Toils_Reserve
    {
        /// <summary>Reserves the named target for this pawn; fails the job immediately if it cannot be claimed.
        /// <paramref name="stackCount"/> claims part of a stack rather than the whole Thing (RimWorld:
        /// <c>Toils_Reserve.Reserve</c>).</summary>
        public static Toil Reserve(TargetIndex ind, int maxClaimants = 1, int stackCount = ReservationManager.StackCount_All)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            toil.initAction = () =>
            {
                Pawn pawn = toil.Pawn;
                Map.Map? map = pawn.Map;
                LocalTargetInfo target = toil.Job.GetTarget(ind);
                if (map == null || !map.reservationManager.Reserve(pawn, target, maxClaimants, stackCount))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
            };
            return toil;
        }

        /// <summary>
        /// Reserves every target in the named slot's queue (RimWorld: <c>Toils_Reserve.ReserveQueue</c>), so a
        /// job that will consume several Things holds all of them before it starts spending time on the first.
        /// Any one of them being unavailable fails the whole job — a half-claimed ingredient list can never
        /// finish its recipe.
        /// </summary>
        public static Toil ReserveQueue(TargetIndex ind, int maxClaimants = 1, int stackCount = ReservationManager.StackCount_All)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            toil.initAction = () =>
            {
                Pawn pawn = toil.Pawn;
                Map.Map? map = pawn.Map;
                List<LocalTargetInfo> queue = toil.Job.GetTargetQueue(ind);
                for (int i = 0; i < queue.Count; i++)
                {
                    if (map != null && map.reservationManager.Reserve(pawn, queue[i], maxClaimants, stackCount)) continue;
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }
            };
            return toil;
        }

        /// <summary>Releases this pawn's claim on the named target, if any.</summary>
        public static Toil Release(TargetIndex ind)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            toil.initAction = () =>
            {
                Pawn pawn = toil.Pawn;
                pawn.Map?.reservationManager.Release(pawn, toil.Job.GetTarget(ind));
            };
            return toil;
        }
    }
}
