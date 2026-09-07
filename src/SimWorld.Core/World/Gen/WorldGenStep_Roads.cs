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
    /// that is impassable (biome or hilliness) is never part of the graph, so a road can never cross one.
    /// </summary>
    public class WorldGenStep_Roads : WorldGenStep
    {
        private static readonly Dictionary<Hilliness, float> HillinessCostMultiplier = new Dictionary<Hilliness, float>
        {
            [Hilliness.Undefined] = 1f,
            [Hilliness.Flat] = 1f,
            [Hilliness.SmallHills] = 1.3f,
            [Hilliness.LargeHills] = 1.8f,
            [Hilliness.Mountainous] = 2.6f,
            [Hilliness.Impassable] = float.PositiveInfinity,
        };

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

                    List<int>? path = ShortestPath(grid, settlement.tile, target.tile, maxPathLength);
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

        /// <summary>Dijkstra over the tile graph; null when no route exists within <paramref name="maxLength"/> hops or every route is blocked by an impassable tile.</summary>
        private static List<int>? ShortestPath(WorldGrid grid, int from, int to, int maxLength)
        {
            int n = grid.TilesCount;
            var cost = new float[n];
            var prev = new int[n];
            var visited = new bool[n];
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.PositiveInfinity;
                prev[i] = -1;
            }
            cost[from] = 0f;

            var queue = new SortedSet<(float cost, int tile)>(Comparer<(float cost, int tile)>.Create((x, y) =>
            {
                int c = x.cost.CompareTo(y.cost);
                return c != 0 ? c : x.tile.CompareTo(y.tile);
            }));
            queue.Add((0f, from));

            while (queue.Count > 0)
            {
                (float cost, int tile) current = queue.Min;
                queue.Remove(current);
                if (visited[current.tile]) continue;
                visited[current.tile] = true;
                if (current.tile == to) break;

                foreach (int neighbor in grid.NeighborsOf(current.tile))
                {
                    if (visited[neighbor]) continue;
                    float edgeCost = TileCost(grid.Tiles[neighbor]);
                    if (float.IsPositiveInfinity(edgeCost)) continue;
                    float candidate = current.cost + edgeCost;
                    if (candidate < cost[neighbor])
                    {
                        queue.Remove((cost[neighbor], neighbor));
                        cost[neighbor] = candidate;
                        prev[neighbor] = current.tile;
                        queue.Add((candidate, neighbor));
                    }
                }
            }

            if (float.IsPositiveInfinity(cost[to]))
            {
                return null;
            }

            var path = new List<int>();
            int step = to;
            while (step != -1)
            {
                path.Add(step);
                if (step == from) break;
                step = prev[step];
            }
            path.Reverse();
            if (path.Count == 0 || path[0] != from || path.Count - 1 > maxLength)
            {
                return null;
            }
            return path;
        }

        private static float TileCost(Tile tile)
        {
            if (tile.WaterCovered || tile.biome == null || tile.biome.impassable)
            {
                return float.PositiveInfinity;
            }
            float hillinessMultiplier = HillinessCostMultiplier.TryGetValue(tile.hilliness, out float m) ? m : 1f;
            if (float.IsPositiveInfinity(hillinessMultiplier))
            {
                return float.PositiveInfinity;
            }
            return tile.biome.movementDifficulty * hillinessMultiplier;
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
