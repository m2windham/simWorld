using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Crafting
{
    public static partial class MaterialLadder
    {
        /// <summary>Wall cells replaced at once per renewal of budget (<see cref="RenewalTuning.MaxUnderway"/>): a
        /// wall is renewed a stretch at a time, not a cell at a time. The port's own figure, unsourced.</summary>
        public const int WallCellsPerStructure = 8;

        /// <summary>
        /// Renewal of standing walls (<c>docs/design/era-progression.md</c> §7.7, beside
        /// <see cref="UpgradeBlueprints"/>): plans a replace-in-place blueprint (<see cref="GenConstruct.PlaceReplacement"/>)
        /// over each of the settlement's own walls that is weaker than the wall it would raise today, the era's stone
        /// (<see cref="StoneWallMaterials.PreferredWallDef"/>) or else a spare bulk metal
        /// (<see cref="BestBuildingMetal"/>). A wall of a house is left to the house's renewal, and a heritage wall is
        /// never touched. At most <paramref name="maxUnderway"/> times <see cref="WallCellsPerStructure"/> replacements
        /// stand at once, and none is planned unless the stock holds <see cref="RenewalTuning.MaterialReserve"/> times
        /// its cost over what other sites already claim. Walls are taken in map order, so the same map plans the same
        /// walls. Returns how many it planned.
        /// </summary>
        public static int UpgradeStanding(Map.Map map, int maxUnderway)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ThingDef wallDef = StoneWallMaterials.PreferredWallDef(map);
            ThingDef? stuff = null;
            if (ReferenceEquals(wallDef, ConstructionThingDefOf.Wall))
            {
                stuff = BestBuildingMetal(map, wallDef);
                if (stuff == null) return 0;
            }
            int strength = MaxHitPointsOf(wallDef, stuff);

            int underway = 0;
            var sites = new List<Thing>(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            sites.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            for (int i = 0; i < sites.Count; i++)
            {
                if (GenConstruct.ThingToReplace(sites[i]) != null) underway++;
            }
            int budget = maxUnderway * WallCellsPerStructure - underway;
            if (budget <= 0) return 0;

            var walls = new List<Thing>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is Building.Building b) || b.Faction == null || b.isHeritage) continue;
                if (GenConstruct.ReplaceTag(b.def) != "Wall" || DwellingUtility.DwellingAt(map, b.Position) != null) continue;
                if (b.MaxHitPoints >= strength) continue;
                walls.Add(b);
            }
            walls.Sort((a, b) => a.Position.x != b.Position.x ? a.Position.x.CompareTo(b.Position.x) : a.Position.z.CompareTo(b.Position.z));

            List<ThingDefCountClass> cost = wallDef.CostListAdjusted(stuff);
            int planned = 0;
            for (int i = 0; i < walls.Count && planned < budget; i++)
            {
                if (!ReserveHolds(map, cost)) break;
                if (GenConstruct.PlaceReplacement(wallDef, stuff, walls[i].Position, map) != null) planned++;
            }
            return planned;
        }

        /// <summary>Whether, for every material of <paramref name="cost"/>, what is spare covers it
        /// <see cref="RenewalTuning.MaterialReserve"/> times over.</summary>
        private static bool ReserveHolds(Map.Map map, List<ThingDefCountClass> cost)
        {
            for (int i = 0; i < cost.Count; i++)
            {
                if (Spare(map, cost[i].thingDef) < (int)Math.Ceiling(cost[i].count * RenewalTuning.MaterialReserve)) return false;
            }
            return true;
        }

        private static int MaxHitPointsOf(ThingDef def, ThingDef? stuff) => stuff == null
            ? def.BaseMaxHitPoints
            : (int)Math.Round(def.GetStatValue(StatDefOf.MaxHitPoints, stuff), MidpointRounding.ToEven);
    }
}
