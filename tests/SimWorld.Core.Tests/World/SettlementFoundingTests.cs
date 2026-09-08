using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;
using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// Region partition, terrain-derived deposits, era-weighted site scoring, trade position and the
    /// solo-start world-generation mode (spec §5b). "World" is aliased to <c>global::SimWorld.World.World</c>
    /// throughout — this test namespace's own last segment is also "World", which would otherwise shadow it.
    /// </summary>
    public class SettlementFoundingTests : ContentTestBase
    {
        public SettlementFoundingTests(CoreContentFixture content) : base(content)
        {
        }

        private static global::SimWorld.World.World Generate(
            string seed,
            int subdivision = 4,
            bool soloStart = false)
        {
            return WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision, soloStart);
        }

        // ----- Regions -----

        [Fact]
        public void Every_land_tile_is_in_exactly_one_region_and_water_tiles_are_in_none()
        {
            global::SimWorld.World.World world = Generate("region-partition");
            WorldGrid grid = world.grid;

            var regionOfTile = new Dictionary<int, int>();
            foreach (WorldRegion region in world.regions)
            {
                foreach (int t in region.tiles)
                {
                    Assert.False(regionOfTile.ContainsKey(t), $"Tile {t} appears in more than one region.");
                    regionOfTile[t] = region.id;
                }
            }

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].WaterCovered)
                {
                    Assert.False(regionOfTile.ContainsKey(i), $"Water tile {i} was assigned to a region.");
                }
                else
                {
                    Assert.True(regionOfTile.ContainsKey(i), $"Land tile {i} was not assigned to any region.");
                }
            }

            Assert.True(world.regions.Count > 1, "Expected more than one region for this seed/grid size.");
        }

        [Fact]
        public void Same_seed_produces_identical_regions_and_deposits()
        {
            global::SimWorld.World.World w1 = Generate("region-determinism");
            global::SimWorld.World.World w2 = Generate("region-determinism");

            Assert.Equal(w1.regions.Count, w2.regions.Count);
            for (int r = 0; r < w1.regions.Count; r++)
            {
                Assert.Equal(w1.regions[r].name, w2.regions[r].name);
                Assert.Equal(w1.regions[r].tiles, w2.regions[r].tiles);
                Assert.Equal(w1.regions[r].dominantBiome, w2.regions[r].dominantBiome);
            }

            for (int i = 0; i < w1.grid.TilesCount; i++)
            {
                IReadOnlyList<TileDeposit> a = w1.grid.Tiles[i].Deposits;
                IReadOnlyList<TileDeposit> b = w2.grid.Tiles[i].Deposits;
                Assert.Equal(a.Count, b.Count);
                foreach (TileDeposit d in a)
                {
                    Assert.Equal(d.magnitude, w2.grid.Tiles[i].DepositMagnitude(d.def));
                }
            }
        }

        [Fact]
        public void Region_boundaries_preferentially_fall_on_natural_frontiers()
        {
            global::SimWorld.World.World world = Generate("region-frontiers");
            WorldGrid grid = world.grid;

            var regionOfTile = new Dictionary<int, int>();
            foreach (WorldRegion region in world.regions)
            {
                foreach (int t in region.tiles) regionOfTile[t] = region.id;
            }

            int boundaryEdges = 0, boundaryFrontier = 0;
            int interiorEdges = 0, interiorFrontier = 0;

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].WaterCovered) continue;
                foreach (int j in grid.NeighborsOf(i))
                {
                    if (j <= i || grid.Tiles[j].WaterCovered) continue;

                    bool isFrontier = IsNaturalFrontier(grid, i, j);
                    if (regionOfTile[i] != regionOfTile[j])
                    {
                        boundaryEdges++;
                        if (isFrontier) boundaryFrontier++;
                    }
                    else
                    {
                        interiorEdges++;
                        if (isFrontier) interiorFrontier++;
                    }
                }
            }

            Assert.True(boundaryEdges > 0, "Expected at least one region boundary for this seed.");
            Assert.True(interiorEdges > 0, "Expected at least one interior edge for this seed.");
            float boundaryFrontierRate = (float)boundaryFrontier / boundaryEdges;
            float interiorFrontierRate = (float)interiorFrontier / interiorEdges;
            Assert.True(
                boundaryFrontierRate > interiorFrontierRate,
                $"Boundary edges should cross natural frontiers more often than interior edges ({boundaryFrontierRate} vs {interiorFrontierRate}).");
        }

        private static bool IsNaturalFrontier(WorldGrid grid, int a, int b)
        {
            Tile ta = grid.Tiles[a];
            Tile tb = grid.Tiles[b];
            if (ta.biome != tb.biome) return true;
            if (Math.Abs(ta.elevation - tb.elevation) > RegionTuning.RidgeElevationThresholdMeters) return true;
            if (ta.Rivers.Any(r => r.neighbor == b)) return true;
            bool coastalA = grid.NeighborsOf(a).Any(n => grid.Tiles[n].WaterCovered);
            bool coastalB = grid.NeighborsOf(b).Any(n => grid.Tiles[n].WaterCovered);
            return coastalA != coastalB;
        }

        [Fact]
        public void Regions_get_unique_nonempty_names()
        {
            global::SimWorld.World.World world = Generate("region-naming");
            List<string> names = world.regions.Select(r => r.name).ToList();
            Assert.NotEmpty(names);
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Region_summary_matches_a_direct_average_of_its_tiles()
        {
            global::SimWorld.World.World world = Generate("region-summary");
            WorldGrid grid = world.grid;

            foreach (WorldRegion region in world.regions)
            {
                Assert.NotEmpty(region.tiles);
                float expectedTemp = region.tiles.Average(t => grid.Tiles[t].temperature);
                float expectedRain = region.tiles.Average(t => grid.Tiles[t].rainfall);
                Assert.True(Math.Abs(expectedTemp - region.meanTemperature) < 0.01f, $"mean temperature: expected {expectedTemp}, got {region.meanTemperature}.");
                Assert.True(Math.Abs(expectedRain - region.meanRainfall) < 0.1f, $"mean rainfall: expected {expectedRain}, got {region.meanRainfall}.");
                Assert.NotNull(region.dominantBiome);
            }
        }

        // ----- Deposits -----

        [Fact]
        public void Water_tiles_never_carry_deposits()
        {
            global::SimWorld.World.World world = Generate("deposit-water");
            foreach (Tile tile in world.grid.Tiles)
            {
                if (tile.WaterCovered) Assert.Empty(tile.Deposits);
            }
        }

        [Fact]
        public void Ore_and_stone_are_absent_from_flat_land()
        {
            global::SimWorld.World.World world = Generate("deposit-stone");
            foreach (Tile tile in world.grid.Tiles)
            {
                if (tile.WaterCovered || tile.hilliness != Hilliness.Flat) continue;
                Assert.Equal(0f, tile.DepositMagnitude(DepositDefOf.Stone));
                Assert.Equal(0f, tile.DepositMagnitude(DepositDefOf.Ore));
            }
        }

        [Fact]
        public void Salt_appears_on_tiles_touching_open_ocean()
        {
            global::SimWorld.World.World world = Generate("deposit-salt");
            WorldGrid grid = world.grid;
            bool foundCoastalSalt = false;

            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                if (tile.WaterCovered) continue;
                bool touchesOpenOcean = grid.NeighborsOf(i).Any(n => grid.Tiles[n].WaterCovered && !grid.Tiles[n].lakeCandidate);
                if (touchesOpenOcean)
                {
                    Assert.True(tile.DepositMagnitude(DepositDefOf.Salt) > 0f, $"Coastal tile {i} should carry a Salt deposit.");
                    foundCoastalSalt = true;
                }
            }
            Assert.True(foundCoastalSalt, "Expected at least one tile touching open ocean for this seed.");
        }

        [Fact]
        public void Timber_and_game_track_the_tiles_biome_densities()
        {
            global::SimWorld.World.World world = Generate("deposit-biome-density");
            WorldGrid grid = world.grid;

            foreach (Tile tile in grid.Tiles)
            {
                if (tile.WaterCovered || tile.biome == null) continue;
                float expectedTimber = GenMath.Clamp01(tile.biome.plantDensity / DepositTuning.PlantDensityNormalizer);
                float expectedGame = GenMath.Clamp01(tile.biome.animalDensity / DepositTuning.AnimalDensityNormalizer);
                Assert.Equal(expectedTimber, tile.DepositMagnitude(DepositDefOf.Timber), 3);
                Assert.Equal(expectedGame, tile.DepositMagnitude(DepositDefOf.Game), 3);
            }
        }

        [Fact]
        public void Ford_only_appears_on_low_tiles_whose_river_link_reaches_land()
        {
            global::SimWorld.World.World world = Generate("deposit-ford");
            WorldGrid grid = world.grid;

            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                if (tile.DepositMagnitude(DepositDefOf.Ford) <= 0f) continue;

                Assert.True(tile.elevation <= DepositTuning.FordMaxElevationMeters);
                Assert.True(tile.Rivers.Any(r => !grid.Tiles[r.neighbor].WaterCovered), $"Ford tile {i} should have a river link to another land tile.");
            }
        }

        [Fact]
        public void Every_deposit_category_appears_somewhere_across_a_few_seeds()
        {
            var totals = new Dictionary<DepositDef, int>();
            foreach (DepositDef def in DefDatabase<DepositDef>.AllDefsListForReading) totals[def] = 0;

            foreach (string seed in new[] { "deposit-sweep-a", "deposit-sweep-b", "deposit-sweep-c", "deposit-sweep-d", "deposit-sweep-e" })
            {
                global::SimWorld.World.World world = Generate(seed);
                foreach (Tile tile in world.grid.Tiles)
                {
                    foreach (TileDeposit d in tile.Deposits) totals[d.def]++;
                }
            }

            foreach (KeyValuePair<DepositDef, int> kv in totals)
            {
                Assert.True(kv.Value > 0, $"Deposit {kv.Key.defName} never appeared across any sampled seed.");
            }
        }

        [Fact]
        public void Coal_never_appears_above_small_hills_where_ore_instead_dominates()
        {
            global::SimWorld.World.World world = Generate("coal-vs-ore-hilliness");
            foreach (Tile tile in world.grid.Tiles)
            {
                if (tile.WaterCovered) continue;
                if (tile.hilliness == Hilliness.LargeHills || tile.hilliness == Hilliness.Mountainous || tile.hilliness == Hilliness.Impassable)
                {
                    Assert.Equal(0f, tile.DepositMagnitude(DepositDefOf.Coal));
                }
            }
        }

        [Fact]
        public void Coal_and_ore_are_distinctly_distributed_across_a_generated_world()
        {
            global::SimWorld.World.World world = Generate("coal-ore-distribution", subdivision: 5);
            WorldGrid grid = world.grid;

            var coal = new List<double>();
            var ore = new List<double>();
            foreach (Tile tile in grid.Tiles)
            {
                if (tile.WaterCovered) continue;
                coal.Add(tile.DepositMagnitude(DepositDefOf.Coal));
                ore.Add(tile.DepositMagnitude(DepositDefOf.Ore));
            }

            Assert.True(coal.Sum() > 0, "Expected some coal to appear across this world.");
            Assert.True(ore.Sum() > 0, "Expected some ore to appear across this world.");

            double correlation = PearsonCorrelation(coal, ore);
            Assert.True(
                correlation < 0.2,
                $"Coal and ore should not track each other across tiles if they are genuinely distinctly distributed (spec §5b.2); got correlation {correlation}.");

            // Tiles where ore is strongly present (hills/mountains, per DepositTuning) should carry little to
            // no coal at all, and the reverse should hold for tiles where coal is strongly present.
            double meanCoalWhereOreStrong = MeanWhere(grid, t => t.DepositMagnitude(DepositDefOf.Ore) > 0.5f, t => t.DepositMagnitude(DepositDefOf.Coal));
            double meanOreWhereCoalStrong = MeanWhere(grid, t => t.DepositMagnitude(DepositDefOf.Coal) > 0.5f, t => t.DepositMagnitude(DepositDefOf.Ore));
            double meanCoalOverall = coal.Average();
            double meanOreOverall = ore.Average();

            Assert.True(meanCoalWhereOreStrong <= meanCoalOverall, $"Expected coal to be no more common than average where ore is strong ({meanCoalWhereOreStrong} vs {meanCoalOverall}).");
            Assert.True(meanOreWhereCoalStrong <= meanOreOverall, $"Expected ore to be no more common than average where coal is strong ({meanOreWhereCoalStrong} vs {meanOreOverall}).");
        }

        private static double MeanWhere(WorldGrid grid, Func<Tile, bool> where, Func<Tile, float> select)
        {
            var values = new List<double>();
            foreach (Tile tile in grid.Tiles)
            {
                if (tile.WaterCovered || !where(tile)) continue;
                values.Add(select(tile));
            }
            return values.Count > 0 ? values.Average() : 0.0;
        }

        private static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            int n = a.Count;
            double meanA = a.Average();
            double meanB = b.Average();

            double covariance = 0.0, varA = 0.0, varB = 0.0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                covariance += da * db;
                varA += da * da;
                varB += db * db;
            }

            if (varA <= 0.0 || varB <= 0.0) return 0.0;
            return covariance / Math.Sqrt(varA * varB);
        }

        [Fact]
        public void Region_resource_profile_is_the_mean_of_its_tiles_deposits()
        {
            global::SimWorld.World.World world = Generate("deposit-aggregate");
            WorldGrid grid = world.grid;

            foreach (WorldRegion region in world.regions)
            {
                foreach (DepositDef def in DefDatabase<DepositDef>.AllDefsListForReading)
                {
                    float expected = region.tiles.Average(t => grid.Tiles[t].DepositMagnitude(def));
                    float actual = region.resourceProfile.MeanMagnitudeOf(def);
                    Assert.True(
                        Math.Abs(expected - actual) < 1e-4f,
                        $"{def.defName}: expected mean {expected} but region profile has {actual}.");
                }
            }
        }

        // ----- Site scoring -----

        [Fact]
        public void Water_tiles_score_zero()
        {
            global::SimWorld.World.World world = Generate("site-water-zero");
            WorldGrid grid = world.grid;
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");

            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].WaterCovered)
                {
                    Assert.Equal(0f, SiteScorer.Score(grid, i, weights));
                }
            }
        }

        [Fact]
        public void Necessities_are_multiplicative_and_zero_the_score_when_any_one_is_missing()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            int tileId = 5;
            Tile tile = grid.Tiles[tileId];
            tile.elevation = 50f;
            tile.temperature = 15f;

            // No deposits at all yet: fails both the water and food necessity gates.
            Assert.Equal(0f, SiteScorer.Score(grid, tileId, weights));

            // Fresh water alone still fails the food gate.
            tile.deposits.Add(new TileDeposit(DepositDefOf.FreshWater, 0.9f));
            Assert.Equal(0f, SiteScorer.Score(grid, tileId, weights));

            // Both necessities now met (plus a survivable temperature already set): score turns positive.
            tile.deposits.Add(new TileDeposit(DepositDefOf.Game, 0.9f));
            Assert.True(SiteScorer.Score(grid, tileId, weights) > 0f);

            // An unsurvivable temperature zeroes the score even with both other necessities met.
            tile.temperature = -80f;
            Assert.Equal(0f, SiteScorer.Score(grid, tileId, weights));
        }

        [Fact]
        public void Site_score_is_discriminating_and_era_weighting_changes_the_ordering()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            const int riverValley = 10;
            const int dryHighland = 20;

            Tile valley = grid.Tiles[riverValley];
            valley.elevation = 50f;
            valley.temperature = 15f;
            valley.deposits.Add(new TileDeposit(DepositDefOf.FreshWater, 0.9f));
            valley.deposits.Add(new TileDeposit(DepositDefOf.ArableSoil, 0.8f));
            valley.deposits.Add(new TileDeposit(DepositDefOf.Timber, 0.7f));
            valley.deposits.Add(new TileDeposit(DepositDefOf.Game, 0.6f));
            valley.deposits.Add(new TileDeposit(DepositDefOf.Flint, 0.5f));

            Tile highland = grid.Tiles[dryHighland];
            highland.elevation = 900f;
            highland.temperature = 5f;
            highland.deposits.Add(new TileDeposit(DepositDefOf.FreshWater, 0.2f));
            highland.deposits.Add(new TileDeposit(DepositDefOf.ArableSoil, 0.2f));
            highland.deposits.Add(new TileDeposit(DepositDefOf.Ore, 0.9f));
            highland.deposits.Add(new TileDeposit(DepositDefOf.Stone, 0.8f));
            highland.deposits.Add(new TileDeposit(DepositDefOf.DefensibleGround, 0.7f));

            SiteWeightDef neolithic = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            SiteWeightDef industrial = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_Industrial");

            float valleyNeolithic = SiteScorer.Score(grid, riverValley, neolithic);
            float highlandNeolithic = SiteScorer.Score(grid, dryHighland, neolithic);
            Assert.True(valleyNeolithic > 0f);
            Assert.True(highlandNeolithic > 0f);
            Assert.True(
                valleyNeolithic > highlandNeolithic,
                $"Neolithic weighting should favor the river valley with soil and timber ({valleyNeolithic} vs {highlandNeolithic}).");

            float valleyIndustrial = SiteScorer.Score(grid, riverValley, industrial);
            float highlandIndustrial = SiteScorer.Score(grid, dryHighland, industrial);
            Assert.True(valleyIndustrial > 0f);
            Assert.True(highlandIndustrial > 0f);
            Assert.True(
                highlandIndustrial > valleyIndustrial,
                $"Industrial weighting should flip the ordering to favor the ore/stone highland ({highlandIndustrial} vs {valleyIndustrial}).");
        }

        [Fact]
        public void Coal_swings_the_industrial_era_score_far_more_than_the_earliest_era()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            const int tileId = 7;
            Tile tile = grid.Tiles[tileId];
            tile.elevation = 50f;
            tile.temperature = 15f;
            tile.deposits.Add(new TileDeposit(DepositDefOf.FreshWater, 0.9f));
            tile.deposits.Add(new TileDeposit(DepositDefOf.Game, 0.9f));

            SiteWeightDef sticksAndStones = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            SiteWeightDef industrial = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_Industrial");

            float withoutCoalSticks = SiteScorer.Score(grid, tileId, sticksAndStones);
            float withoutCoalIndustrial = SiteScorer.Score(grid, tileId, industrial);

            tile.deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.8f));

            float withCoalSticks = SiteScorer.Score(grid, tileId, sticksAndStones);
            float withCoalIndustrial = SiteScorer.Score(grid, tileId, industrial);

            // Isolate coal's own contribution to the score in each era (everything else on the tile is
            // unchanged), so the comparison is attributable to coal specifically rather than to some other
            // difference between the two SiteWeightDefs.
            float coalContributionSticks = withCoalSticks - withoutCoalSticks;
            float coalContributionIndustrial = withCoalIndustrial - withoutCoalIndustrial;

            Assert.True(coalContributionIndustrial > 0f, "Coal should raise the Industrial-era score at all.");
            Assert.True(
                coalContributionIndustrial > coalContributionSticks * 5f,
                $"Coal should swing the Industrial-era score far more than the Sticks & Stones one ({coalContributionIndustrial} vs {coalContributionSticks}).");
        }

        // ----- Trade position -----

        [Fact]
        public void Trade_position_scores_are_deterministic_and_bounded()
        {
            global::SimWorld.World.World world = Generate("trade-determinism");
            WorldGrid grid = world.grid;
            List<int> candidates = LandTiles(grid);

            IReadOnlyDictionary<int, float> scoresA = TradePositionScorer.Score(grid, candidates, new RandomStream(777));
            IReadOnlyDictionary<int, float> scoresB = TradePositionScorer.Score(grid, candidates, new RandomStream(777));

            Assert.NotEmpty(scoresA);
            Assert.Equal(scoresA.Count, scoresB.Count);
            foreach (KeyValuePair<int, float> kv in scoresA)
            {
                Assert.Equal(kv.Value, scoresB[kv.Key]);
            }
            Assert.All(scoresA.Values, v => Assert.InRange(v, 0f, 1f));
            Assert.Contains(1f, scoresA.Values);
        }

        [Fact]
        public void Trade_position_scorer_reuses_the_roads_cost_function_and_scores_the_shared_path()
        {
            global::SimWorld.World.World world = Generate("trade-shared-path");
            WorldGrid grid = world.grid;
            List<int> settlementTiles = world.worldObjects.Select(o => o.tile).ToList();
            Assert.True(settlementTiles.Count >= 2, "Expected at least two settlements for this seed.");

            int maxPathLength = Math.Max(8, grid.TilesCount / 50);
            (int a, int b, List<int> path)? reachablePair = null;
            for (int i = 0; i < settlementTiles.Count && reachablePair == null; i++)
            {
                for (int j = i + 1; j < settlementTiles.Count; j++)
                {
                    List<int>? path = TilePathfinder.ShortestPath(grid, settlementTiles[i], settlementTiles[j], maxPathLength);
                    if (path != null)
                    {
                        reachablePair = (settlementTiles[i], settlementTiles[j], path);
                        break;
                    }
                }
            }
            Assert.True(reachablePair.HasValue, "Expected at least one pair of settlements reachable within maxPathLength for this seed.");

            IReadOnlyDictionary<int, float> scores = TradePositionScorer.Score(
                grid,
                new List<int> { reachablePair!.Value.a, reachablePair.Value.b },
                new RandomStream(1),
                samplePairs: 1,
                maxPathLength: maxPathLength);
            foreach (int tile in reachablePair.Value.path)
            {
                Assert.True(scores.ContainsKey(tile), $"Tile {tile} on the shared shortest path should have a nonzero trade position score.");
            }
        }

        [Fact]
        public void Trade_position_scorer_returns_empty_for_fewer_than_two_candidates()
        {
            global::SimWorld.World.World world = Generate("trade-too-few");
            WorldGrid grid = world.grid;
            List<int> oneTile = LandTiles(grid).Take(1).ToList();
            Assert.Empty(TradePositionScorer.Score(grid, oneTile, new RandomStream(1)));
        }

        private static List<int> LandTiles(WorldGrid grid)
        {
            var land = new List<int>();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (!grid.Tiles[i].WaterCovered) land.Add(i);
            }
            return land;
        }

        // ----- Solo start -----

        [Fact]
        public void Solo_start_creates_only_the_player_faction_and_no_rival_settlements()
        {
            global::SimWorld.World.World world = Generate("solo-start", soloStart: true);

            Assert.NotEmpty(world.factions);
            Assert.All(world.factions, f => Assert.True(f.def.isPlayer));
            Assert.All(world.worldObjects, o => Assert.True(o.faction == null || o.faction.def.isPlayer));
        }

        [Fact]
        public void Solo_start_defaults_to_false_and_leaves_ordinary_generation_unchanged()
        {
            global::SimWorld.World.World world = Generate("solo-default");
            Assert.False(world.info.soloStart);
            Assert.True(world.factions.Select(f => f.def).Distinct().Count() > 1, "Ordinary (non-solo) generation should still create more than one faction def.");
        }

        // ----- Scribe -----

        [Fact]
        public void Scribe_round_trip_preserves_solo_start_and_the_player_only_faction_list()
        {
            global::SimWorld.World.World original = Generate("solo-scribe", 2, soloStart: true);
            string xml = Scribe.SaveToString(original, "world");

            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            Assert.True(loaded.info.soloStart);
            Assert.All(loaded.factions, f => Assert.True(f.def.isPlayer));
        }

        [Fact]
        public void Scribe_round_trip_regenerates_identical_regions_and_deposits()
        {
            global::SimWorld.World.World original = Generate("regions-scribe", 2);
            string xml = Scribe.SaveToString(original, "world");

            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Equal(original.regions.Count, loaded.regions.Count);
            for (int r = 0; r < original.regions.Count; r++)
            {
                Assert.Equal(original.regions[r].name, loaded.regions[r].name);
                Assert.Equal(original.regions[r].tiles, loaded.regions[r].tiles);
            }
            for (int i = 0; i < original.grid.TilesCount; i++)
            {
                Assert.Equal(original.grid.Tiles[i].Deposits.Count, loaded.grid.Tiles[i].Deposits.Count);
            }
        }
    }
}
