using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Partitions every land tile into a <see cref="WorldRegion"/> (spec §5b.1): floods out simultaneously
    /// from scattered seeds, each step taking the cheapest available frontier tile — a multi-source Dijkstra
    /// over a crossing-cost graph that is cheap between similar tiles and expensive across a natural
    /// frontier (biome change, a ridge, a river, a coastline; see <see cref="RegionTuning"/>), so region
    /// edges land on real features instead of an arbitrary grid. Seeds are placed per connected landmass
    /// (never across water, the same as the flood itself) so every land tile ends up in exactly one region
    /// and no island is ever missed. Runs after <see cref="WorldGenStep_Rivers"/> (region edges read biome,
    /// elevation and river data) and before <see cref="WorldGenStep_Deposits"/> (which fills in each
    /// region's resource profile once regions exist).
    /// </summary>
    public class WorldGenStep_Regions : WorldGenStep
    {
        public override void GenerateFresh(string seed, World world)
        {
            RandomStream rand = SeededStream(seed);
            WorldGrid grid = world.grid;
            int n = grid.TilesCount;

            List<List<int>> components = ConnectedLandComponents(grid);
            var seedTiles = new List<int>();
            foreach (List<int> component in components)
            {
                int desired = Math.Max(1, (int)Math.Round(component.Count / (double)RegionTuning.TargetTilesPerRegion));
                float minDistance = MinSeedDistance(component.Count, desired);
                seedTiles.AddRange(PickSeedTiles(grid, component, desired, minDistance, rand));
            }

            var regions = new List<WorldRegion>(seedTiles.Count);
            var regionOf = new int[n];
            var cost = new float[n];
            for (int i = 0; i < n; i++)
            {
                regionOf[i] = -1;
                cost[i] = float.PositiveInfinity;
            }

            var queue = new SortedSet<(float cost, int tile)>(Comparer<(float cost, int tile)>.Create((x, y) =>
            {
                int c = x.cost.CompareTo(y.cost);
                return c != 0 ? c : x.tile.CompareTo(y.tile);
            }));

            for (int s = 0; s < seedTiles.Count; s++)
            {
                int tile = seedTiles[s];
                regions.Add(new WorldRegion { id = s });
                regionOf[tile] = s;
                cost[tile] = 0f;
                queue.Add((0f, tile));
            }

            var finalized = new bool[n];
            while (queue.Count > 0)
            {
                (float cost, int tile) current = queue.Min;
                queue.Remove(current);
                if (finalized[current.tile]) continue;
                finalized[current.tile] = true;

                foreach (int neighbor in grid.NeighborsOf(current.tile))
                {
                    if (finalized[neighbor] || grid.Tiles[neighbor].WaterCovered) continue;

                    float edgeCost = CrossingCost(grid, current.tile, neighbor);
                    float candidateCost = current.cost + edgeCost;
                    if (candidateCost < cost[neighbor])
                    {
                        queue.Remove((cost[neighbor], neighbor));
                        cost[neighbor] = candidateCost;
                        regionOf[neighbor] = regionOf[current.tile];
                        queue.Add((candidateCost, neighbor));
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (grid.Tiles[i].WaterCovered) continue;
                int r = regionOf[i];
                if (r >= 0) regions[r].tiles.Add(i);
            }
            regions.RemoveAll(r => r.tiles.Count == 0);
            for (int i = 0; i < regions.Count; i++) regions[i].id = i;

            SummarizeAndName(grid, regions, rand);
            world.regions = regions;
        }

        /// <summary>Connected components of the land-only tile graph (never crossing a water tile), the same
        /// flood-fill shape <c>WorldGenStep_Biomes.MarkLakeCandidates</c> uses for water bodies.</summary>
        private static List<List<int>> ConnectedLandComponents(WorldGrid grid)
        {
            int n = grid.TilesCount;
            var visited = new bool[n];
            var components = new List<List<int>>();
            var queue = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                if (visited[i] || grid.Tiles[i].WaterCovered) continue;
                var component = new List<int>();
                queue.Clear();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int neighbor in grid.NeighborsOf(current))
                    {
                        if (!visited[neighbor] && !grid.Tiles[neighbor].WaterCovered)
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                components.Add(component);
            }
            return components;
        }

        /// <summary>Roughly the radius (in tiles) of one seed's average share of the landmass, scaled down so
        /// seeds spread rather than cluster; 0 when only one seed is wanted (nothing to separate it from).</summary>
        private static float MinSeedDistance(int componentTileCount, int desiredSeedCount)
        {
            if (desiredSeedCount <= 1) return 0f;
            double avgTilesPerSeed = componentTileCount / (double)desiredSeedCount;
            double avgRadius = Math.Sqrt(avgTilesPerSeed / Math.PI);
            return (float)Math.Max(1.0, avgRadius * RegionTuning.SeedMinSeparationFactor);
        }

        /// <summary>
        /// Weighted-random pick with a minimum-separation rejection loop — the same shape as
        /// <see cref="WorldGenStep_Factions.PickSettlementTile"/>, applied to region seeds. Every non-empty
        /// <paramref name="candidates"/> list yields at least one seed (the first pick always succeeds,
        /// since nothing has been placed yet to be "too close" to), which is what guarantees every connected
        /// landmass gets covered.
        /// </summary>
        private static List<int> PickSeedTiles(WorldGrid grid, List<int> candidates, int count, float minDistance, RandomStream rand)
        {
            var placed = new List<int>();
            const int MaxAttemptsPerSeed = 200;
            for (int s = 0; s < count; s++)
            {
                for (int attempt = 0; attempt < MaxAttemptsPerSeed; attempt++)
                {
                    if (!GenCollection.TryRandomElementByWeight(candidates, _ => 1f, rand, out int candidate)) break;

                    bool tooClose = placed.Contains(candidate);
                    if (!tooClose)
                    {
                        foreach (int p in placed)
                        {
                            if (grid.ApproxDistanceInTiles(candidate, p) < minDistance)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }
                    if (!tooClose)
                    {
                        placed.Add(candidate);
                        break;
                    }
                }
            }
            return placed;
        }

        private static float CrossingCost(WorldGrid grid, int a, int b)
        {
            Tile ta = grid.Tiles[a];
            Tile tb = grid.Tiles[b];
            float cost = RegionTuning.BaseCrossingCost;

            if (ta.biome != tb.biome)
            {
                cost += RegionTuning.BiomeChangeCost;
            }

            float elevationDiff = Math.Abs(ta.elevation - tb.elevation);
            cost += elevationDiff > RegionTuning.RidgeElevationThresholdMeters
                ? RegionTuning.RidgeCrossingCost
                : elevationDiff * RegionTuning.ElevationCostPerMeter;

            if (HasRiverLinkBetween(ta, b))
            {
                cost += RegionTuning.RiverCrossingCost;
            }

            if (IsCoastal(grid, a) != IsCoastal(grid, b))
            {
                cost += RegionTuning.CoastalMismatchCost;
            }

            return cost;
        }

        private static bool HasRiverLinkBetween(Tile tile, int neighbor)
        {
            foreach (RiverLink link in tile.Rivers)
            {
                if (link.neighbor == neighbor) return true;
            }
            return false;
        }

        private static bool IsCoastal(WorldGrid grid, int tileId)
        {
            foreach (int n in grid.NeighborsOf(tileId))
            {
                if (grid.Tiles[n].WaterCovered) return true;
            }
            return false;
        }

        private static void SummarizeAndName(WorldGrid grid, List<WorldRegion> regions, RandomStream rand)
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorldRegion region in regions)
            {
                var biomeCounts = new Dictionary<BiomeDef, int>();
                float temperatureSum = 0f;
                float rainfallSum = 0f;
                foreach (int t in region.tiles)
                {
                    Tile tile = grid.Tiles[t];
                    if (tile.biome != null)
                    {
                        biomeCounts.TryGetValue(tile.biome, out int c);
                        biomeCounts[tile.biome] = c + 1;
                    }
                    temperatureSum += tile.temperature;
                    rainfallSum += tile.rainfall;
                }

                BiomeDef? dominant = null;
                int best = -1;
                foreach (KeyValuePair<BiomeDef, int> kv in biomeCounts)
                {
                    if (kv.Value > best)
                    {
                        best = kv.Value;
                        dominant = kv.Key;
                    }
                }

                region.dominantBiome = dominant;
                region.meanTemperature = region.tiles.Count > 0 ? temperatureSum / region.tiles.Count : 0f;
                region.meanRainfall = region.tiles.Count > 0 ? rainfallSum / region.tiles.Count : 0f;
                region.name = RegionNameMaker.MakeRegionName(rand, usedNames);
                usedNames.Add(region.name);
            }
        }
    }
}
