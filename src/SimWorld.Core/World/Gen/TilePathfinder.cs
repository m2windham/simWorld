using System.Collections.Generic;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Shared tile-graph cost function and shortest-path search (RimWorld: folded directly into
    /// <c>Verse.WorldGenStep_Roads</c> there; split out here so <see cref="WorldGenStep_Roads"/> and
    /// <c>Siting.TradePositionScorer</c> compute over the exact same notion of "cheap route" — spec §5b.2's
    /// "trade position comes almost free... the same computation, inverted" only holds if both really run
    /// the same code, not a second copy of it).
    /// </summary>
    public static class TilePathfinder
    {
        public static readonly IReadOnlyDictionary<Hilliness, float> HillinessCostMultiplier = new Dictionary<Hilliness, float>
        {
            [Hilliness.Undefined] = 1f,
            [Hilliness.Flat] = 1f,
            [Hilliness.SmallHills] = 1.3f,
            [Hilliness.LargeHills] = 1.8f,
            [Hilliness.Mountainous] = 2.6f,
            [Hilliness.Impassable] = float.PositiveInfinity,
        };

        /// <summary>Cost to enter <paramref name="tile"/>: infinite over water or an impassable biome/hilliness, otherwise the biome's movement difficulty scaled by hilliness.</summary>
        public static float TileCost(Tile tile)
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

        /// <summary>Dijkstra over the tile graph; null when no route exists within <paramref name="maxLength"/> hops or every route is blocked by an impassable tile.</summary>
        public static List<int>? ShortestPath(WorldGrid grid, int from, int to, int maxLength)
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
    }
}
