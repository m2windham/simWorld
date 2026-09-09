using System;
using System.Collections.Generic;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Weapons a pawn is carrying (RimWorld: <c>Verse.Pawn_EquipmentTracker</c>), trimmed to what gear
    /// generation needs: a small held list, with the first entry addressable as <see cref="Primary"/> the way
    /// RimWorld code everywhere reads <c>pawn.equipment.Primary</c>. No inventory here (RimWorld's carried,
    /// non-worn/wielded items) — apparel is <see cref="Pawn_ApparelTracker"/> instead, RimWorld's own split.
    /// </summary>
    public sealed class Pawn_EquipmentTracker : IExposable
    {
        private readonly Pawn pawn;

        private List<ThingWithComps> equipment = new List<ThingWithComps>();

        /// <summary>Scribe reconstructs this by passing the owning pawn as a ctor arg (see <see cref="Pawn.ExposeData"/>), the same way <see cref="Pawn_AgeTracker"/> does — there is no parameterless constructor to support.</summary>
        public Pawn_EquipmentTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        /// <summary>The pawn this tracker belongs to (kept for parity with RimWorld's own trackers and for whatever later consumes it — nothing in this module reads it yet).</summary>
        public Pawn Pawn => pawn;

        public IReadOnlyList<ThingWithComps> AllEquipmentListForReading => equipment;

        /// <summary>The first item carried — RimWorld's convention for "the weapon this pawn currently wields", since nothing here yet distinguishes a holstered sidearm from a readied primary.</summary>
        public ThingWithComps? Primary => equipment.Count > 0 ? equipment[0] : null;

        public void AddEquipment(ThingWithComps newEq)
        {
            if (newEq == null) throw new ArgumentNullException(nameof(newEq));
            equipment.Add(newEq);
        }

        public bool RemoveEquipment(ThingWithComps eq) => eq != null && equipment.Remove(eq);

        public void ExposeData()
        {
            List<ThingWithComps>? list = equipment;
            Scribe_Collections.Look(ref list, "equipment", LookMode.Deep);
            equipment = list ?? new List<ThingWithComps>();
        }
    }
}
