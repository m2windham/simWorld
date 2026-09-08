using System;
using System.Collections.Generic;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Derives every tile's <see cref="DepositDef"/> magnitudes from terrain that already exists — elevation,
    /// hilliness, rainfall, biome and rivers — never sprinkled at random (spec §5b.2). Uses no randomness of
    /// its own: two worlds generated from the same seed always agree on deposits because the terrain they
    /// read already agreed. Runs after <see cref="WorldGenStep_Regions"/> so it can also fold per-tile
    /// deposits into each region's <see cref="ResourceProfile"/>.
    /// </summary>
    public class WorldGenStep_Deposits : WorldGenStep
    {
        public override void GenerateFresh(string seed, World world)
        {
            WorldGrid grid = world.grid;
            int n = grid.TilesCount;

            for (int i = 0; i < n; i++)
            {
                Tile tile = grid.Tiles[i];
                tile.deposits.Clear();
                if (tile.WaterCovered) continue;

                AddIfPositive(tile, DepositDefOf.FreshWater, FreshWaterMagnitude(grid, i, tile));
                AddIfPositive(tile, DepositDefOf.ArableSoil, ArableSoilMagnitude(grid, i, tile));
                AddIfPositive(tile, DepositDefOf.Clay, ClayMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Flint, FlintMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Stone, StoneMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Ore, OreMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Coal, CoalMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Salt, SaltMagnitude(grid, i, tile));
                AddIfPositive(tile, DepositDefOf.Timber, TimberMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Game, GameMagnitude(tile));
                AddIfPositive(tile, DepositDefOf.Ford, FordMagnitude(grid, tile));
                AddIfPositive(tile, DepositDefOf.DefensibleGround, DefensibleGroundMagnitude(grid, i, tile));
            }

            AggregateRegionProfiles(grid, world.regions);
        }

        private static void AddIfPositive(Tile tile, DepositDef def, float magnitude)
        {
            if (magnitude > 0f)
            {
                tile.deposits.Add(new TileDeposit(def, GenMath.Clamp01(magnitude)));
            }
        }

        // ---- Fresh water: river links, lake-adjacent tiles, high rainfall ----

        private static float FreshWaterMagnitude(WorldGrid grid, int tileId, Tile tile)
        {
            float magnitude = tile.Rivers.Count > 0 ? DepositTuning.FreshWaterRiverMagnitude : 0f;

            foreach (int n in grid.NeighborsOf(tileId))
            {
                Tile neighbor = grid.Tiles[n];
                if (neighbor.WaterCovered && neighbor.lakeCandidate)
                {
                    magnitude = Math.Max(magnitude, DepositTuning.FreshWaterLakeAdjacentMagnitude);
                }
            }

            float rainfallRange = DepositTuning.FreshWaterRainfallSaturationMm - DepositTuning.FreshWaterRainfallFloorMm;
            float rainfallFraction = rainfallRange > 0f
                ? GenMath.Clamp01((tile.rainfall - DepositTuning.FreshWaterRainfallFloorMm) / rainfallRange)
                : 0f;
            magnitude = Math.Max(magnitude, rainfallFraction * DepositTuning.FreshWaterRainfallMaxMagnitude);

            return magnitude;
        }

        // ---- Deep soil / arable: low elevation, low hilliness, floodplain, moderate-to-high rainfall ----

        private static float ArableSoilMagnitude(WorldGrid grid, int tileId, Tile tile)
        {
            if (tile.elevation >= DepositTuning.ArableMaxElevationMeters) return 0f;
            if (tile.hilliness != Hilliness.Flat && tile.hilliness != Hilliness.SmallHills) return 0f;

            float hillinessFactor = tile.hilliness == Hilliness.Flat ? 1f : 0.6f;
            float elevationFactor = 1f - GenMath.Clamp01(tile.elevation / DepositTuning.ArableMaxElevationMeters);
            float rainfallFactor = TriangularFactor(tile.rainfall, DepositTuning.ArableRainfallMinMm, DepositTuning.ArableRainfallMaxMm);

            float magnitude = hillinessFactor * elevationFactor * rainfallFactor;
            if (tile.Rivers.Count > 0)
            {
                magnitude += DepositTuning.ArableFloodplainBonus;
            }
            return magnitude;
        }

        /// <summary>1.0 at the midpoint of [min,max], ramping linearly to 0 at either edge and beyond.</summary>
        private static float TriangularFactor(float value, float min, float max)
        {
            if (value <= min || value >= max) return 0f;
            float mid = (min + max) / 2f;
            return value <= mid
                ? (value - min) / (mid - min)
                : (max - value) / (max - mid);
        }

        // ---- Clay: floodplains and river bends ----

        private static float ClayMagnitude(Tile tile)
        {
            if (tile.Rivers.Count >= 2) return DepositTuning.ClayBendMagnitude;
            if (tile.Rivers.Count == 1 && tile.elevation <= DepositTuning.ClayMaxElevationMeters)
            {
                return DepositTuning.ClayFloodplainMagnitude;
            }
            return 0f;
        }

        // ---- Flint: lowland tiles in dry/temperate biomes ----

        private static float FlintMagnitude(Tile tile)
        {
            if (tile.elevation >= DepositTuning.FlintMaxElevationMeters) return 0f;
            float dryness = GenMath.Clamp01(1f - tile.rainfall / DepositTuning.FlintRainfallCeilingMm);
            return dryness * DepositTuning.FlintMaxMagnitude;
        }

        // ---- Stone: hilliness at or above hilly ----

        private static float StoneMagnitude(Tile tile)
        {
            return tile.hilliness switch
            {
                Hilliness.SmallHills => DepositTuning.StoneMagnitudeSmallHills,
                Hilliness.LargeHills => DepositTuning.StoneMagnitudeLargeHills,
                Hilliness.Mountainous => DepositTuning.StoneMagnitudeMountainous,
                Hilliness.Impassable => DepositTuning.StoneMagnitudeMountainous,
                _ => 0f,
            };
        }

        // ---- Ore: hills and mountains, rarer at lower hilliness ----

        private static float OreMagnitude(Tile tile)
        {
            return tile.hilliness switch
            {
                Hilliness.SmallHills => DepositTuning.OreMagnitudeSmallHills,
                Hilliness.LargeHills => DepositTuning.OreMagnitudeLargeHills,
                Hilliness.Mountainous => DepositTuning.OreMagnitudeMountainous,
                Hilliness.Impassable => DepositTuning.OreMagnitudeMountainous,
                _ => 0f,
            };
        }

        // ---- Coal: ancient swamp/floodplain sediment — low, historically wet ground, never hills or mountains ----

        private static float CoalMagnitude(Tile tile)
        {
            if (tile.elevation >= DepositTuning.CoalMaxElevationMeters) return 0f;
            if (tile.hilliness != Hilliness.Flat && tile.hilliness != Hilliness.SmallHills) return 0f;

            float hillinessFactor = tile.hilliness == Hilliness.Flat ? 1f : DepositTuning.CoalSmallHillsFactor;
            float elevationFactor = 1f - GenMath.Clamp01(tile.elevation / DepositTuning.CoalMaxElevationMeters);

            float swampinessRange = DepositTuning.CoalSwampinessCeiling - DepositTuning.CoalSwampinessFloor;
            float swampinessFactor = swampinessRange > 0f
                ? GenMath.Clamp01((tile.swampiness - DepositTuning.CoalSwampinessFloor) / swampinessRange)
                : 0f;
            float rainfallFactor = TriangularFactor(tile.rainfall, DepositTuning.CoalRainfallMinMm, DepositTuning.CoalRainfallMaxMm);

            // Swampiness (this port's own read of "historically waterlogged ground") is the primary signal;
            // a merely-rainy tile whose swampiness noise sample didn't land high still contributes, just
            // capped below what an actual historic swamp reads as.
            float wetnessFactor = Math.Max(swampinessFactor, rainfallFactor * DepositTuning.CoalRainfallOnlyCeiling);

            float magnitude = hillinessFactor * elevationFactor * wetnessFactor;
            if (tile.Rivers.Count > 0)
            {
                magnitude += DepositTuning.CoalFloodplainBonus;
            }
            return magnitude;
        }

        // ---- Salt: coastal tiles, rarely inland springs ----

        private static float SaltMagnitude(WorldGrid grid, int tileId, Tile tile)
        {
            float magnitude = 0f;
            foreach (int n in grid.NeighborsOf(tileId))
            {
                Tile neighbor = grid.Tiles[n];
                if (neighbor.WaterCovered && !neighbor.lakeCandidate)
                {
                    magnitude = Math.Max(magnitude, DepositTuning.SaltCoastalMagnitude);
                }
            }
            if (tile.elevation >= DepositTuning.SaltSpringMinElevationMeters && tile.rainfall >= DepositTuning.SaltSpringMinRainfallMm)
            {
                magnitude = Math.Max(magnitude, DepositTuning.SaltSpringMagnitude);
            }
            return magnitude;
        }

        // ---- Timber / Game: straight from the biome's own densities ----

        private static float TimberMagnitude(Tile tile) =>
            tile.biome == null ? 0f : GenMath.Clamp01(tile.biome.plantDensity / DepositTuning.PlantDensityNormalizer);

        private static float GameMagnitude(Tile tile) =>
            tile.biome == null ? 0f : GenMath.Clamp01(tile.biome.animalDensity / DepositTuning.AnimalDensityNormalizer);

        // ---- Ford: low-lying river reach whose linked neighbour is land, not a river mouth ----

        private static float FordMagnitude(WorldGrid grid, Tile tile)
        {
            if (tile.elevation > DepositTuning.FordMaxElevationMeters) return 0f;

            bool hasLandCrossing = false;
            foreach (RiverLink link in tile.Rivers)
            {
                if (!grid.Tiles[link.neighbor].WaterCovered)
                {
                    hasLandCrossing = true;
                    break;
                }
            }
            if (!hasLandCrossing) return 0f;

            float t = GenMath.Clamp01(1f - tile.elevation / DepositTuning.FordMaxElevationMeters);
            return GenMath.Lerp(DepositTuning.FordMinMagnitude, 1f, t);
        }

        // ---- Defensible ground: elevation over neighbours' mean, or a bend/peninsula ----

        private static float DefensibleGroundMagnitude(WorldGrid grid, int tileId, Tile tile)
        {
            IReadOnlyList<int> neighbors = grid.NeighborsOf(tileId);
            float neighborElevationSum = 0f;
            foreach (int n in neighbors) neighborElevationSum += grid.Tiles[n].elevation;
            float neighborMeanElevation = neighbors.Count > 0 ? neighborElevationSum / neighbors.Count : tile.elevation;

            float elevationAdvantage = tile.elevation - neighborMeanElevation;
            float heightScore = elevationAdvantage >= DepositTuning.DefensibleElevationMarginMeters
                ? GenMath.Clamp01(elevationAdvantage / (DepositTuning.DefensibleElevationMarginMeters * DepositTuning.DefensibleElevationSaturationFactor))
                : 0f;

            float riverFraction = neighbors.Count > 0 ? tile.Rivers.Count / (float)neighbors.Count : 0f;
            float riverScore = riverFraction >= DepositTuning.DefensibleRiverFractionThreshold
                ? DepositTuning.DefensibleRiverBendMagnitude
                : 0f;

            return Math.Max(heightScore, riverScore);
        }

        // ---- Region resource profiles: the mean of member tiles' deposit magnitudes ----

        private static void AggregateRegionProfiles(WorldGrid grid, List<WorldRegion> regions)
        {
            foreach (WorldRegion region in regions)
            {
                var sums = new Dictionary<DepositDef, float>();
                foreach (int t in region.tiles)
                {
                    foreach (TileDeposit deposit in grid.Tiles[t].Deposits)
                    {
                        sums.TryGetValue(deposit.def, out float sum);
                        sums[deposit.def] = sum + deposit.magnitude;
                    }
                }
                int count = region.tiles.Count;
                foreach (KeyValuePair<DepositDef, float> kv in sums)
                {
                    region.resourceProfile.SetMeanMagnitude(kv.Key, count > 0 ? kv.Value / count : 0f);
                }
            }
        }
    }
}
