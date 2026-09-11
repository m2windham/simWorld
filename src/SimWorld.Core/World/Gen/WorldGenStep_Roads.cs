using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Links each settlement to its 1-2 nearest neighbours by the cheapest passable route (RimWorld:
    /// <c>Verse.WorldGenStep_Roads</c>, here Dijkstra instead of RimWorld's exact pathfinder). Cost per tile
    /// is its biome's <see cref="BiomeDef.movementDifficulty"/> scaled by <see cref="Hilliness"/>; a tile
    /// that is impassable (biome or hilliness), or whose biome refuses roads outright
    /// (<see cref="BiomeDef.allowRoads"/> — see <see cref="RoadsForbidden"/>), is never part of the graph, so
    /// a road can never cross one.
    /// The cost function and the search itself live in <see cref="TilePathfinder"/>, shared with
    /// <c>Siting.TradePositionScorer</c> (spec §5b.2).
    /// </summary>
    public class WorldGenStep_Roads : WorldGenStep
    {
        public override void GenerateFresh(string seed, World world)
        {
            WorldGrid grid = world.grid;
            IReadOnlyList<RoadDef> roadsByPriorityDesc = SortedByPriorityDescending();
            if (roadsByPriorityDesc.Count == 0)
            {
                return;
            }

            var settlements = new List<WorldObject>(world.Settlements);
            if (settlements.Count < 2)
            {
                return;
            }

            int maxPathLength = Math.Max(8, grid.TilesCount / 50);
            var linked = new HashSet<(int, int)>();

            foreach (WorldObject settlement in settlements)
            {
                List<WorldObject> nearest = NearestOthers(grid, settlements, settlement, 2);
                foreach (WorldObject target in nearest)
                {
                    (int a, int b) key = settlement.tile < target.tile ? (settlement.tile, target.tile) : (target.tile, settlement.tile);
                    if (linked.Contains(key))
                    {
                        continue;
                    }

                    List<int>? path = TilePathfinder.ShortestPath(grid, settlement.tile, target.tile, maxPathLength, RoadsForbidden);
                    if (path == null)
                    {
                        continue;
                    }

                    RoadDef road = ChooseRoadDef(roadsByPriorityDesc, settlement.faction, target.faction);
                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        WriteRoadLink(grid, path[i], path[i + 1], road);
                    }
                    linked.Add(key);
                }
            }
        }

        /// <summary>
        /// A tile no road may be laid across (RimWorld: <c>BiomeDef.allowRoads</c>, consulted by its own road
        /// generation — the exact call site is not sourceable in this sandbox, so the shape is pinned by
        /// tests rather than copied). The two endpoints are settlements, and a settlement only ever stands on
        /// a <see cref="BiomeDef.canBuildBase"/> tile, so this only ever removes tiles from the middle of a
        /// route.
        ///
        /// <para/>It is an exclusion from the graph rather than a check on the finished path on purpose: a
        /// road that would have crossed sea ice should go round it if there is a way round, and only fail to
        /// link when there is not. A tile with no biome stays allowed, which is what the step did before the
        /// biome was consulted.
        ///
        /// <para/>This step draws no randomness — it is settlement positions and Dijkstra — so consulting the
        /// biome here cannot move the world seed's sequence for any later step.
        /// </summary>
        private static bool RoadsForbidden(Tile tile) => tile.biome != null && !tile.biome.allowRoads;

        private static List<WorldObject> NearestOthers(WorldGrid grid, List<WorldObject> settlements, WorldObject from, int count)
        {
            var others = new List<WorldObject>();
            foreach (WorldObject candidate in settlements)
            {
                if (!ReferenceEquals(candidate, from)) others.Add(candidate);
            }
            others.Sort((a, b) => grid.ApproxDistanceInTiles(from.tile, a.tile).CompareTo(grid.ApproxDistanceInTiles(from.tile, b.tile)));
            if (others.Count > count)
            {
                others.RemoveRange(count, others.Count - count);
            }
            return others;
        }

        private static void WriteRoadLink(WorldGrid grid, int a, int b, RoadDef road)
        {
            List<RoadLink> fromA = grid.Tiles[a].potentialRoads;
            int existing = fromA.FindIndex(l => l.neighbor == b);
            if (existing >= 0)
            {
                if (fromA[existing].road.priority >= road.priority) return;
                fromA.RemoveAt(existing);
                grid.Tiles[b].potentialRoads.RemoveAll(l => l.neighbor == a);
            }
            grid.Tiles[a].potentialRoads.Add(new RoadLink(b, road));
            grid.Tiles[b].potentialRoads.Add(new RoadLink(a, road));
        }

        private static IReadOnlyList<RoadDef> SortedByPriorityDescending()
        {
            var defs = new List<RoadDef>(DefDatabase<RoadDef>.AllDefsListForReading);
            defs.Sort((a, b) => b.priority.CompareTo(a.priority));
            return defs;
        }

        private static RoadDef ChooseRoadDef(IReadOnlyList<RoadDef> byPriorityDesc, Faction? a, Faction? b)
        {
            TechLevel techLevel = Max(TechLevelOf(a), TechLevelOf(b));
            foreach (RoadDef road in byPriorityDesc)
            {
                if (road.minTechLevel <= techLevel) return road;
            }
            return byPriorityDesc[byPriorityDesc.Count - 1];
        }

        private static TechLevel TechLevelOf(Faction? faction) => faction?.def?.techLevel ?? TechLevel.Neolithic;

        private static TechLevel Max(TechLevel a, TechLevel b) => a >= b ? a : b;
    }
}
