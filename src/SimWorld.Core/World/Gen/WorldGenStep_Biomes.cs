using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Assigns every tile a <see cref="BiomeDef"/> (RimWorld: <c>Verse.WorldGenStep_Terrain</c>'s biome pass,
    /// split out here). Water tiles are first flood-filled into connected bodies so small, enclosed ones can
    /// score as <c>Lake</c> instead of <c>Ocean</c>; then every tile picks the highest-scoring
    /// <see cref="BiomeWorker"/> among all loaded biomes (each worker gates on <see cref="Tile.WaterCovered"/>
    /// itself, so land and water biomes never compete for the same tile).
    /// </summary>
    public class WorldGenStep_Biomes : WorldGenStep
    {
        public override void GenerateFresh(string seed, World world)
        {
            WorldGrid grid = world.grid;
            MarkLakeCandidates(grid);

            IReadOnlyList<BiomeDef> biomes = DefDatabase<BiomeDef>.AllDefsListForReading;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                BiomeDef? best = null;
                float bestScore = float.NegativeInfinity;
                for (int b = 0; b < biomes.Count; b++)
                {
                    float score = biomes[b].Worker.GetScore(tile, i);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = biomes[b];
                    }
                }
                tile.biome = best ?? (tile.WaterCovered ? BiomeDefOf.Ocean : BiomeDefOf.TemperateForest);
            }
        }

        /// <summary>Any connected water component with at most this many tiles counts as a lake rather than open ocean.</summary>
        private static int LakeMaxSizeFor(int tilesCount) => Math.Max(5, tilesCount / 50);

        private static void MarkLakeCandidates(WorldGrid grid)
        {
            int n = grid.TilesCount;
            var visited = new bool[n];
            int lakeMaxSize = LakeMaxSizeFor(n);
            var component = new List<int>();
            var queue = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                if (visited[i] || !grid.Tiles[i].WaterCovered)
                {
                    continue;
                }
                component.Clear();
                queue.Clear();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in grid.NeighborsOf(current))
                    {
                        if (!visited[neighbor] && grid.Tiles[neighbor].WaterCovered)
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                bool isLake = component.Count <= lakeMaxSize;
                for (int c = 0; c < component.Count; c++)
                {
                    grid.Tiles[component[c]].lakeCandidate = isLake;
                }
            }
        }
    }
}
