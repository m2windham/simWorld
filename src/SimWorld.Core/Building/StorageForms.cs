using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Research;

namespace SimWorld.Building
{
    /// <summary>The storage ladder's forms after the first (<c>Buildings_StorageForms.xml</c>), bound in their own
    /// <c>[DefOf]</c> class rather than appended to <see cref="ConstructionThingDefOf"/> (CLAUDE.md).</summary>
    [DefOf]
    public static class StorageFormDefOf
    {
        public static ThingDef Granary = null!;
        public static ThingDef Storehouse = null!;
        public static ThingDef Horreum = null!;
        public static ThingDef TitheBarn = null!;
        public static ThingDef Warehouse = null!;
        public static ThingDef DistributionCentre = null!;
        public static ThingDef AutomatedDepot = null!;
    }

    /// <summary>
    /// The settlement's store through the eras (renewal lane 2, <c>docs/design/era-progression.md</c> §7): the
    /// storage hut, then one form per era from the granary to the automated depot. Every form fills the one storage
    /// need (<see cref="StorageHutNeed"/>, counted across the forms by <see cref="StoneWallMaterials.EquivalentsOf"/>),
    /// and a standing store is renewed in place as the era's own form (<see cref="StructureRenewal"/>).
    /// <para/>
    /// RimWorld has no store but the shelf and the stockpile (recalled); the ladder is this port's, shaped like the
    /// dwelling ladder: the form is the era's, its material is what the town holds.
    /// </summary>
    public static class StorageForms
    {
        /// <summary>Every form, oldest era first (the era of its research gate; the hut has none), defName on a tie.</summary>
        public static IReadOnlyList<ThingDef> All
        {
            get
            {
                var forms = new List<ThingDef>
                {
                    ConstructionThingDefOf.StorageHut,
                    StorageFormDefOf.Granary,
                    StorageFormDefOf.Storehouse,
                    StorageFormDefOf.Horreum,
                    StorageFormDefOf.TitheBarn,
                    StorageFormDefOf.Warehouse,
                    StorageFormDefOf.DistributionCentre,
                    StorageFormDefOf.AutomatedDepot,
                };
                forms.Sort((a, b) =>
                {
                    int ea = EraAvailability.EraOf(a)?.order ?? 0, eb = EraAvailability.EraOf(b)?.order ?? 0;
                    return ea != eb ? ea.CompareTo(eb) : string.CompareOrdinal(a.defName, b.defName);
                });
                return forms;
            }
        }

        /// <summary>Whether <paramref name="def"/> is one of the forms.</summary>
        public static bool IsStorage(ThingDef? def)
        {
            if (def == null) return false;
            IReadOnlyList<ThingDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], def)) return true;
            }
            return false;
        }

        /// <summary>The newest form the civilization has learned; the hut before any other.</summary>
        public static ThingDef Newest()
        {
            IReadOnlyList<ThingDef> all = All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].IsResearchFinished) return all[i];
            }
            return ConstructionThingDefOf.StorageHut;
        }
    }
}
