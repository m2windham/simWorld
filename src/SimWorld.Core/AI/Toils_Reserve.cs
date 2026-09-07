using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>Toils that claim or release a <see cref="ReservationManager"/> target (RimWorld: <c>Verse.AI.Toils_Reserve</c>).</summary>
    public static class Toils_Reserve
    {
        /// <summary>Reserves the named target for this pawn; fails the job immediately if it cannot be claimed.</summary>
        public static Toil Reserve(TargetIndex ind, int maxClaimants = 1)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            toil.initAction = () =>
            {
                Pawn pawn = toil.Pawn;
                Map.Map? map = pawn.Map;
                LocalTargetInfo target = toil.Job.GetTarget(ind);
                if (map == null || !map.reservationManager.Reserve(pawn, target, maxClaimants))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
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
