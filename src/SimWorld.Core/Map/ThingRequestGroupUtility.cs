using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>Which defs a <see cref="ThingRequestGroup"/> holds (RimWorld: <c>Verse.ThingListGroupHelper</c>).</summary>
    public static class ThingRequestGroupUtility
    {
        /// <summary>Every group <see cref="ListerThings"/> keeps a list for: all of them but
        /// <see cref="ThingRequestGroup.Undefined"/>, which is nothing, and <see cref="ThingRequestGroup.Everything"/>,
        /// which is the lister's own master list (RimWorld: <c>ThingListGroupHelper.AllGroups</c>).</summary>
        public static readonly ThingRequestGroup[] StoredGroups = BuildStoredGroups();

        private static ThingRequestGroup[] BuildStoredGroups()
        {
            var groups = new List<ThingRequestGroup>();
            foreach (ThingRequestGroup group in (ThingRequestGroup[])System.Enum.GetValues(typeof(ThingRequestGroup)))
            {
                if (group == ThingRequestGroup.Undefined || group == ThingRequestGroup.Everything) continue;
                groups.Add(group);
            }
            return groups.ToArray();
        }

        /// <summary>Whether a Thing of <paramref name="def"/> belongs in <paramref name="group"/>. Decided from
        /// the def alone, as RimWorld's is, so membership never changes while a Thing is on the map.</summary>
        public static bool Includes(this ThingRequestGroup group, ThingDef def)
        {
            switch (group)
            {
                case ThingRequestGroup.Undefined:
                    return false;
                case ThingRequestGroup.Everything:
                    return true;
                case ThingRequestGroup.Pawn:
                    return def.category == ThingCategory.Pawn;
                case ThingRequestGroup.Building:
                    return def.category == ThingCategory.Building;
                case ThingRequestGroup.Item:
                    return def.category == ThingCategory.Item;
                case ThingRequestGroup.Plant:
                    return def.category == ThingCategory.Plant;
                case ThingRequestGroup.HaulableEver:
                    return def.EverHaulable;
                case ThingRequestGroup.BuildingArtificial:
                    // RimWorld: def.IsBuildingArtificial, a building that is not natural rock or a rock vein.
                    // This port spells that ThingDef.mineable == false.
                    return def.category == ThingCategory.Building && !def.mineable;
                case ThingRequestGroup.Filth:
                    return def.category == ThingCategory.Filth;
                case ThingRequestGroup.Blueprint:
                    return def.category == ThingCategory.Blueprint;
                case ThingRequestGroup.BuildingFrame:
                    return def.category == ThingCategory.Frame;
                case ThingRequestGroup.PotentialBillGiver:
                    return IsPotentialBillGiver(def);
                case ThingRequestGroup.ResearchBench:
                    // RimWorld: typeof(Building_ResearchBench).IsAssignableFrom(def.thingClass). This port has
                    // one research bench, an ordinary Building identified by its def (ResearchWorkDefOf), not a
                    // Building subclass, so the def itself is the test.
                    return ReferenceEquals(def, ResearchWorkDefOf.ResearchBench);
                default:
                    throw new System.ArgumentException("Unknown ThingRequestGroup " + group, nameof(group));
            }
        }

        /// <summary>
        /// RimWorld asks <c>!def.AllRecipes.NullOrEmpty()</c>. This port's benches are not all alike: most
        /// carry a <see cref="CompProperties_BillGiver"/> (<see cref="Crafting.WorkGiver_DoBill"/> serves those),
        /// and the butcher table carries none and is a bench only because <c>ButcherAnimal</c> lists it in
        /// <c>recipeUsers</c> (<see cref="AI.WorkGiver_ButcherCorpse"/> finds it that way). Either mark makes a
        /// def a potential bill giver, so a def the old whole-building walk would have offered to either giver
        /// is still in the group. Only buildings and pawns are asked at all: nothing else holds a bill stack,
        /// and <see cref="ThingDef.AllRecipes"/> is a lazy per-def cache that is not worth filling for every
        /// item and plant that spawns.
        /// </summary>
        private static bool IsPotentialBillGiver(ThingDef def)
        {
            if (def.category != ThingCategory.Building && def.category != ThingCategory.Pawn) return false;
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_BillGiver) return true;
                }
            }
            return def.AllRecipes.Count > 0;
        }
    }
}
