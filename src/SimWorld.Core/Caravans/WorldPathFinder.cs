using System;
using System.Collections.Generic;
using SimWorld.World;

namespace SimWorld.Caravans
{
    /// <summary>
    /// Finds a caravan's route across the tile graph (RimWorld: <c>RimWorld.Planet.WorldPathFinder</c>).
    /// Generalises <c>Gen.WorldGenStep_Roads</c>'s own Dijkstra into a reusable A*; the heuristic is kept
    /// at zero (making this exactly Dijkstra/uniform-cost search) rather than guessing a tight admissible
    /// bound from content-defined road multipliers, so the result is always the true cheapest route
    /// regardless of what movement multipliers content defines.
    /// </summary>
    public static class WorldPathFinder
    {
        /// <summary>True and fills <paramref name="path"/> (start..end inclusive) when a route exists; false and an empty path otherwise.</summary>
        public static bool FindPath(WorldGrid grid, int start, int end, out List<int> path)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            path = new List<int>();

            if (start == end)
            {
                path.Add(start);
                return true;
            }

            var gScore = new Dictionary<int, float> { [start] = 0f };
            var cameFrom = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            var open = new SortedSet<(float cost, int tile)>(Comparer<(float cost, int tile)>.Create((x, y) =>
            {
                int c = x.cost.CompareTo(y.cost);
                return c != 0 ? c : x.tile.CompareTo(y.tile);
            }));
            open.Add((0f, start));

            while (open.Count > 0)
            {
                (float cost, int tile) current = open.Min;
                open.Remove(current);
                if (!visited.Add(current.tile)) continue;

                if (current.tile == end)
                {
                    ReconstructPath(cameFrom, start, end, path);
                    return true;
                }

                foreach (int neighbor in grid.NeighborsOf(current.tile))
                {
                    if (visited.Contains(neighbor)) continue;
                    float edgeCost = WorldPathGrid.MovementCostBetween(grid, current.tile, neighbor);
                    if (edgeCost >= WorldPathGrid.ImpassableMovementDifficulty) continue;

                    float tentative = gScore[current.tile] + edgeCost;
                    if (!gScore.TryGetValue(neighbor, out float existing) || tentative < existing)
                    {
                        if (gScore.ContainsKey(neighbor)) open.Remove((existing, neighbor));
                        gScore[neighbor] = tentative;
                        cameFrom[neighbor] = current.tile;
                        open.Add((tentative, neighbor));
                    }
                }
            }

            path = new List<int>();
            return false;
        }

        private static void ReconstructPath(Dictionary<int, int> cameFrom, int start, int end, List<int> outPath)
        {
            outPath.Clear();
            int step = end;
            outPath.Add(step);
            while (step != start)
            {
                step = cameFrom[step];
                outPath.Add(step);
            }
            outPath.Reverse();
        }
    }
}
