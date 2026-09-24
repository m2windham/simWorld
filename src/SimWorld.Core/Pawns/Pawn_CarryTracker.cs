using System;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Pawns
{
    /// <summary>
    /// What a pawn is holding in its hands (RimWorld: <c>Verse.Pawn_CarryTracker</c>). A hauler that picks
    /// something up takes the real Thing off the map and holds it here until it puts it down — at the place it
    /// was taking it, or, if the job ends first, at its own feet (<see cref="AI.Pawn_JobTracker.EndCurrentJob"/>,
    /// RimWorld's <c>Pawn_JobTracker.CleanupCurrentJob</c>). Nothing is destroyed at pickup, so nothing can
    /// vanish between pickup and delivery: whatever was picked up either reaches where it was going or is lying
    /// on the map afterwards.
    ///
    /// <para/><b>Why this exists.</b> Before it, a construction haul took its materials off the stack the
    /// moment the pawn reached it and held them nowhere, so a hauler pulled away before delivery — a player's
    /// order, being downed, a raid — deleted them. A wall whose cost was exactly what the settlement owned could
    /// then never be finished, and nothing said why.
    ///
    /// <para/><b>What is ported, and what is not.</b> The minimum of RimWorld's shape that makes an interrupted
    /// carry put its load down: one Thing in hand (<see cref="CarriedThing"/>), picked up by splitting it off
    /// its stack (<see cref="TryStartCarry"/>, RimWorld's <c>Thing.SplitOff</c>), dropped on the pawn's own
    /// cell (<see cref="TryDropCarriedThing"/>), used up in place (<see cref="DestroyCarriedThing"/>), and
    /// deep-saved with the pawn so a save taken mid-carry keeps it. Left out: RimWorld's <c>ThingOwner</c>
    /// container (so a second pickup is refused rather than merged into what is already held, and
    /// <c>AvailableStackSpace</c>/mass limits do not exist), <c>GenPlace</c>'s search for a free or mergeable
    /// cell near the drop point (the Thing lands on the pawn's cell, which is where every abstract carry in
    /// this port already drops), forbidding what a hostile pawn drops, ticking the carried Thing
    /// (<c>CarryHandsTick</c> — nothing carried here yet has a comp that ticks), and copying comp state such
    /// as rot progress onto a split-off piece (<c>ThingComp.PostSplit</c>). Only
    /// <see cref="Building.JobDriver_HaulToBuildingSite"/> carries through this tracker today; the other
    /// abstract carries (<see cref="AI.JobDriver_HaulToCell"/>, <see cref="AI.JobDriver_FoodDeliver"/>,
    /// <see cref="AI.JobDriver_ButcherCorpse"/>, <see cref="AI.JobDriver_Warden_Feed"/>) still keep their own.
    /// </summary>
    public sealed class Pawn_CarryTracker : IExposable
    {
        private readonly Pawn pawn;

        /// <summary>RimWorld keeps this in <c>innerContainer</c>, a one-stack <c>ThingOwner</c>; see the
        /// class doc for why a single field stands in for it.</summary>
        private Thing? carriedThing;

        /// <summary>Scribe reconstructs this by passing the owning pawn as a ctor arg (see <see cref="Pawn.ExposeData"/>),
        /// the same way <see cref="Pawn_EquipmentTracker"/> does.</summary>
        public Pawn_CarryTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public Pawn Pawn => pawn;

        /// <summary>The Thing in this pawn's hands, off the map; null when it holds nothing (RimWorld:
        /// <c>Pawn_CarryTracker.CarriedThing</c>).</summary>
        public Thing? CarriedThing => carriedThing;

        /// <summary>
        /// Picks up <paramref name="count"/> of <paramref name="item"/> and returns how many were actually taken
        /// (RimWorld: <c>Pawn_CarryTracker.TryStartCarry</c>). The whole stack travels as itself — the same
        /// object, taken off the map — so a Thing with state of its own arrives as itself; part of a stack is
        /// split off into a new Thing of the same def and stuff, which is only ever asked of a stackable good.
        /// Refused (0) for a pawn that is dead or downed, one already holding something, and a destroyed item.
        /// </summary>
        public int TryStartCarry(Thing item, int count)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (pawn.Dead || pawn.Downed) return 0;
            if (carriedThing != null || item.Destroyed) return 0;

            count = Math.Min(count, item.stackCount);
            if (count <= 0) return 0;

            carriedThing = SplitOff(item, count);
            return count;
        }

        /// <summary>
        /// Puts the carried Thing down on <paramref name="dropLoc"/> (RimWorld:
        /// <c>Pawn_CarryTracker.TryDropCarriedThing</c> with <c>ThingPlaceMode.Near</c>, trimmed to the cell
        /// itself — see the class doc). False, with the Thing still in hand, when there is nothing to drop or
        /// nowhere on a map to drop it: a pawn that is not spawned keeps what it holds rather than losing it.
        /// </summary>
        public bool TryDropCarriedThing(IntVec3 dropLoc, out Thing? resultingThing)
        {
            resultingThing = null;
            Thing? thing = carriedThing;
            if (thing == null) return false;
            if (thing.Destroyed)
            {
                carriedThing = null;
                return false;
            }

            Map.Map? map = pawn.Map;
            if (map == null || !dropLoc.IsValid || !GenGrid.InBounds(dropLoc, map)) return false;

            carriedThing = null;
            GenSpawn.Spawn(thing, dropLoc, map);
            resultingThing = thing;
            return true;
        }

        /// <summary>Destroys what is in hand — it has been used up where it was taken, as a delivered
        /// construction material is (RimWorld: <c>Pawn_CarryTracker.DestroyCarriedThing</c>).</summary>
        public void DestroyCarriedThing()
        {
            Thing? thing = carriedThing;
            carriedThing = null;
            thing?.Destroy(DestroyMode.Vanish);
        }

        /// <summary>
        /// <paramref name="count"/> of <paramref name="thing"/> as a Thing of its own, off the map (RimWorld:
        /// <c>Thing.SplitOff</c>): the Thing itself when that is the whole stack, otherwise a new Thing of the
        /// same def and stuff with the count moved onto it. Hit points are copied, as RimWorld's does.
        /// </summary>
        private static Thing SplitOff(Thing thing, int count)
        {
            if (count >= thing.stackCount)
            {
                thing.DeSpawn();
                return thing;
            }

            Thing piece = ThingMaker.MakeThing(thing.def, thing.Stuff);
            piece.stackCount = count;
            piece.HitPoints = thing.HitPoints;
            thing.stackCount -= count;
            return piece;
        }

        public void ExposeData()
        {
            // Deep, not a reference: the Thing in hand is off the map, so this pawn is the only place a save
            // can find it. Deep-loading also registers its load id, which is what lets the hauler's saved job
            // (whose target A is this very Thing) re-link to it.
            Thing? thing = carriedThing;
            Scribe_Deep.Look(ref thing, "carriedThing");
            carriedThing = thing;
        }
    }
}
