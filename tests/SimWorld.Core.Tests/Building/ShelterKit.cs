using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Raises houses for tests without a settlement to plan them: a compound to keep the records, and
    /// every wall, door and bed of a plan spawned finished, as <see cref="Frame.CompleteConstruction"/> leaves
    /// them.</summary>
    internal static class ShelterKit
    {
        public static DwellingDef Rung(string defName) => DefDatabase<DwellingDef>.GetNamed(defName);

        /// <summary>The map's compound, or a small unplanned one in its south-west corner.</summary>
        public static SettlementCompound Compound(CoreMap map) =>
            map.settlementCompound ??= SettlementCompound.WithInterior(new CellRect(1, 1, 2, 2));

        /// <summary>A finished house of <paramref name="def"/> with its front row on <paramref name="origin"/>'s
        /// row (turned North) and its south-west corner there.</summary>
        public static Dwelling Raise(CoreMap map, DwellingDef def, IntVec3 origin, Faction? owner = null,
            int householdId = Pawn_RelationsTracker.None, Rot4? rot = null)
        {
            IntVec2 size = def.Size;
            var dwelling = new Dwelling(def, new CellRect(origin.x, origin.z, size.x, size.z), rot ?? Rot4.North)
            {
                householdId = householdId,
            };
            Compound(map).dwellings.Add(dwelling);
            Build(map, dwelling, owner);
            return dwelling;
        }

        /// <summary>A house the planner chose (<see cref="DwellingPlanner.TryFindPlot"/>) on a real map, recorded
        /// for <paramref name="householdId"/>, its parts spawned finished and its roof laid, or null when no plot
        /// is free.</summary>
        public static Dwelling? RaiseOnPlot(CoreMap map, DwellingDef def, int householdId, Faction? owner = null)
        {
            SettlementCompound compound = map.settlementCompound!;
            Dwelling? dwelling = DwellingPlanner.TryFindPlot(map, compound, def);
            if (dwelling == null) return null;
            dwelling.householdId = householdId;
            compound.dwellings.Add(dwelling);
            Build(map, dwelling, owner);
            Roof(map, dwelling);
            return dwelling;
        }

        private static void Build(CoreMap map, Dwelling dwelling, Faction? owner)
        {
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 partRot) in dwelling.Parts)
            {
                (ThingDef thingDef, ThingDef? stuff) = DwellingUtility.PartDef(map, dwelling, kind);
                if (thingDef.MadeFromStuff) stuff ??= GenStuff.DefaultStuffFor(thingDef);
                Thing t = ThingMaker.MakeThing(thingDef, stuff);
                if (owner != null) t.SetFaction(owner);
                GenSpawn.Spawn(t, cell, map, partRot);
            }
        }

        /// <summary>Roofs the house, walls and floor but not an open court, with its own roof, as a builder would.</summary>
        public static void Roof(CoreMap map, Dwelling dwelling)
        {
            RoofDef roof = dwelling.def.roofDef ?? RoofDefOf.RoofConstructed;
            IntVec2 size = dwelling.def.Size;
            for (int x = 0; x < size.x; x++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    if (dwelling.def.PartAt(x, z) != DwellingPartKind.Outside) map.roofGrid.SetRoof(dwelling.CellAt(x, z), roof);
                }
            }
        }
    }
}
