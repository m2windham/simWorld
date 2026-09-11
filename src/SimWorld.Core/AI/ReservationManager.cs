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

        /// <summary>
        /// How many pieces out of the target's stack this claim covers, or
        /// <see cref="ReservationManager.StackCount_All"/> for the whole Thing (RimWorld:
        /// <c>Reservation.StackCount</c>). A cell target is one piece by definition.
        /// </summary>
        public int stackCount = ReservationManager.StackCount_All;

        public Reservation()
        {
        }

        public Reservation(Pawn claimant, LocalTargetInfo target, int maxClaimants, int stackCount = ReservationManager.StackCount_All)
        {
            this.claimant = claimant ?? throw new ArgumentNullException(nameof(claimant));
            this.target = target;
            this.maxClaimants = maxClaimants;
            this.stackCount = stackCount;
        }

        public void ExposeData()
        {
            Pawn? p = claimant;
            Scribe_References.Look(ref p, "claimant");
            Scribe_Targets.Look(ref target, "target");
            Scribe_Values.Look(ref maxClaimants, "maxClaimants", 1);
            Scribe_Values.Look(ref stackCount, "stackCount", ReservationManager.StackCount_All);
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
    /// <para/>
    /// A claim can cover part of a stack rather than the whole Thing (<see cref="Reservation.stackCount"/>), so
    /// two jobs that each want some of one pile can both have what they asked for while a third that would
    /// overdraw it is refused. Every caller that does not pass a count gets <see cref="StackCount_All"/> and
    /// therefore exclusive use, which is what the bench, bed and stockpile-cell claims all want.
    /// </summary>
    public sealed class ReservationManager : IExposable
    {
        /// <summary>The <c>stackCount</c> meaning "the whole Thing, however big the stack is" (RimWorld:
        /// <c>ReservationManager.StackCount_All</c>). The default for every caller that does not care.</summary>
        public const int StackCount_All = -1;

        private List<Reservation> reservations = new List<Reservation>();

        /// <summary>
        /// True if <paramref name="claimant"/> could reserve <paramref name="stackCount"/> pieces of
        /// <paramref name="target"/> right now (RimWorld: <c>ReservationManager.CanReserve</c>). Already
        /// holding a big enough claim counts as able to. Two claimants may share one stack only when
        /// <paramref name="maxClaimants"/> leaves room for both <i>and</i> their counts together fit inside it.
        /// <para/>
        /// <b>Not ported:</b> RimWorld also rejects a claimant that is unspawned or on another map, consults a
        /// separate <c>physicalInteractionReservationManager</c>, and lets a player-forced job bump another
        /// pawn's claim. This manager has no map of its own and this port has no physical-interaction layer,
        /// so those three screens have nothing to read yet.
        /// </summary>
        public bool CanReserve(Pawn claimant, LocalTargetInfo target, int maxClaimants = 1, int stackCount = StackCount_All)
        {
            if (claimant == null) throw new ArgumentNullException(nameof(claimant));
            if (!target.IsValid) return false;

            int available = AvailablePieces(target);
            int wanted = stackCount == StackCount_All ? available : stackCount;
            if (wanted > available) return false;
            if (IsAlreadyReserved(claimant, target, wanted)) return true;

            int otherClaimants = 0;
            int claimedByOthers = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                Reservation r = reservations[i];
                if (!r.target.Equals(target) || ReferenceEquals(r.claimant, claimant)) continue;
                if (r.maxClaimants != maxClaimants) return false;
                otherClaimants++;
                claimedByOthers += r.stackCount == StackCount_All ? available : r.stackCount;
                if (otherClaimants >= maxClaimants) return false;
                if (wanted + claimedByOthers > available) return false;
            }
            return true;
        }

        public bool Reserve(Pawn claimant, LocalTargetInfo target, int maxClaimants = 1, int stackCount = StackCount_All)
        {
            if (claimant == null) throw new ArgumentNullException(nameof(claimant));
            if (!target.IsValid) return false;

            int wanted = stackCount == StackCount_All ? AvailablePieces(target) : stackCount;
            // A claim this pawn already holds that is at least this big is the reservation — don't stack a
            // second one on top of it (RimWorld keys that check on the claiming Job as well; see this class's
            // own remarks for why there is no Job here to key on).
            if (IsAlreadyReserved(claimant, target, wanted)) return true;
            if (!CanReserve(claimant, target, maxClaimants, stackCount)) return false;

            reservations.Add(new Reservation(claimant, target, maxClaimants, stackCount));
            return true;
        }

        /// <summary>How many pieces of <paramref name="target"/> there are to go round: a stack's count, or
        /// one for a bare cell (RimWorld does exactly this inline in both CanReserve and Reserve).</summary>
        private static int AvailablePieces(LocalTargetInfo target) => target.HasThing ? target.Thing!.stackCount : 1;

        /// <summary>True when <paramref name="claimant"/> already holds a claim on <paramref name="target"/>
        /// covering at least <paramref name="wanted"/> pieces (RimWorld: <c>ReservationManager.IsAlreadyReserved</c>).</summary>
        private bool IsAlreadyReserved(Pawn claimant, LocalTargetInfo target, int wanted)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                Reservation r = reservations[i];
                if (!ReferenceEquals(r.claimant, claimant) || !r.target.Equals(target)) continue;
                if (r.stackCount == StackCount_All || r.stackCount >= wanted) return true;
            }
            return false;
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
