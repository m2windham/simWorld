using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// One pawn's claim on one target (RimWorld: <c>Verse.AI.ReservationManager.Reservation</c>).
    /// <b>Deviation:</b> real RimWorld also stores the claiming <see cref="Job"/> and a reservation "layer",
    /// so several independent reservations can coexist per pawn and be released one at a time when their
    /// owning job ends. This port's job set never asks a pawn to hold more than one reservation at once, so
    /// <see cref="ReservationManager.ReleaseAllClaimedBy"/> (all of a pawn's claims, on any job's end) covers
    /// every case this pass needs; a job field can be added here later without changing the save shape.
    /// </summary>
    public sealed class Reservation : IExposable
    {
        public Pawn claimant = null!;
        public LocalTargetInfo target;
        public int maxClaimants = 1;

        public Reservation()
        {
        }

        public Reservation(Pawn claimant, LocalTargetInfo target, int maxClaimants)
        {
            this.claimant = claimant ?? throw new ArgumentNullException(nameof(claimant));
            this.target = target;
            this.maxClaimants = maxClaimants;
        }

        public void ExposeData()
        {
            Pawn? p = claimant;
            Scribe_References.Look(ref p, "claimant");
            Scribe_Targets.Look(ref target, "target");
            Scribe_Values.Look(ref maxClaimants, "maxClaimants", 1);
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                claimant = p!;
            }
        }
    }

    /// <summary>
    /// Per-map claim tracking so two pawns cannot both act on the same target (RimWorld: <c>Verse.AI.ReservationManager</c>).
    /// A target with <see cref="Reservation.maxClaimants"/> already met refuses further claims until one is
    /// released — which happens automatically whenever a pawn's job ends (<see cref="Pawn_JobTracker.EndCurrentJob"/>),
    /// not per-toil, so a job never needs to remember to clean up after itself.
    /// </summary>
    public sealed class ReservationManager : IExposable
    {
        private List<Reservation> reservations = new List<Reservation>();

        /// <summary>True if <paramref name="claimant"/> could reserve <paramref name="target"/> right now
        /// (already holding it counts as able to).</summary>
        public bool CanReserve(Pawn claimant, LocalTargetInfo target, int maxClaimants = 1)
        {
            if (claimant == null) throw new ArgumentNullException(nameof(claimant));
            if (!target.IsValid) return false;

            int heldByOthers = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                Reservation r = reservations[i];
                if (!r.target.Equals(target)) continue;
                if (ReferenceEquals(r.claimant, claimant)) return true;
                heldByOthers++;
            }
            return heldByOthers < maxClaimants;
        }

        public bool Reserve(Pawn claimant, LocalTargetInfo target, int maxClaimants = 1)
        {
            if (!CanReserve(claimant, target, maxClaimants)) return false;
            for (int i = 0; i < reservations.Count; i++)
            {
                if (ReferenceEquals(reservations[i].claimant, claimant) && reservations[i].target.Equals(target))
                {
                    return true;
                }
            }
            reservations.Add(new Reservation(claimant, target, maxClaimants));
            return true;
        }

        public bool IsReservedBy(Pawn claimant, LocalTargetInfo target)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                if (ReferenceEquals(reservations[i].claimant, claimant) && reservations[i].target.Equals(target)) return true;
            }
            return false;
        }

        public bool IsReserved(LocalTargetInfo target)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                if (reservations[i].target.Equals(target)) return true;
            }
            return false;
        }

        public void Release(Pawn claimant, LocalTargetInfo target)
        {
            reservations.RemoveAll(r => ReferenceEquals(r.claimant, claimant) && r.target.Equals(target));
        }

        /// <summary>Drops every claim <paramref name="claimant"/> holds on this map (RimWorld: called from job cleanup).</summary>
        public void ReleaseAllClaimedBy(Pawn claimant)
        {
            reservations.RemoveAll(r => ReferenceEquals(r.claimant, claimant));
        }

        public void ExposeData()
        {
            List<Reservation>? list = reservations;
            Scribe_Collections.Look(ref list, "reservations", LookMode.Deep);
            reservations = list ?? new List<Reservation>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                reservations.RemoveAll(r => r == null || r.claimant == null);
            }
        }
    }
}
