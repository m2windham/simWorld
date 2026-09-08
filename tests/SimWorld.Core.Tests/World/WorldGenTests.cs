using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Noise;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// World generation: noise, the icosphere grid, terrain/biome/river/road/settlement generation, and
    /// Scribe round-tripping. "World" is aliased to <c>global::SimWorld.World.World</c> throughout — this
    /// test namespace's own last segment is also "World", which would otherwise shadow the type.
    /// </summary>
    public class WorldGenTests : ContentTestBase
    {
        public WorldGenTests(CoreContentFixture content) : base(content)
        {
        }

        private static global::SimWorld.World.World Generate(
            string seed,
            int subdivision = 4,
            OverallRainfall rainfall = OverallRainfall.Normal,
            OverallTemperature temperature = OverallTemperature.Normal,
            OverallPopulation population = OverallPopulation.Normal)
        {
            return WorldGenerator.GenerateWorld(seed, 0.3f, rainfall, temperature, population, "Test", subdivision);
        }

        // ----- Noise -----

        [Fact]
        public void Perlin_is_deterministic_for_the_same_seed_and_differs_for_another()
        {
            var a = new Perlin(1.5, 2.0, 0.5, 6, 111, NoiseQuality.Standard);
            var b = new Perlin(1.5, 2.0, 0.5, 6, 111, NoiseQuality.Standard);
            var c = new Perlin(1.5, 2.0, 0.5, 6, 222, NoiseQuality.Standard);

            bool everDiffered = false;
            for (int i = 0; i < 20; i++)
            {
                double x = i * 0.37, y = i * 0.51, z = i * 0.19;
                Assert.Equal(a.GetValue(x, y, z), b.GetValue(x, y, z));
                if (a.GetValue(x, y, z) != c.GetValue(x, y, z)) everDiffered = true;
            }
            Assert.True(everDiffered, "Perlin noise with a different seed should diverge from the original somewhere.");
        }

        [Fact]
        public void Perlin_and_ridged_multifractal_values_stay_in_a_bounded_range()
        {
            var perlin = new Perlin(1.5, 2.0, 0.5, 6, 12345, NoiseQuality.Standard);
            var ridged = new RidgedMultifractal(1.5, 2.0, 6, 54321, NoiseQuality.Standard);
            var rand = new RandomStream(999);
            for (int i = 0; i < 500; i++)
            {
                double x = rand.Range(-10f, 10f);
                double y = rand.Range(-10f, 10f);
                double z = rand.Range(-10f, 10f);
                Assert.InRange(perlin.GetValue(x, y, z), -2.0, 2.0);
                Assert.InRange(ridged.GetValue(x, y, z), -2.0, 2.0);
            }
        }

        [Fact]
        public void Noise_combinators_compose_as_expected()
        {
            var a = new Const(2.0);
            var b = new Const(3.0);
            Assert.Equal(5.0, new Add(a, b).GetValue(0, 0, 0));
            Assert.Equal(6.0, new Multiply(a, b).GetValue(0, 0, 0));
            Assert.Equal(7.0, new ScaleBias(a, 2.0, 3.0).GetValue(0, 0, 0));
            Assert.Equal(-2.0, new Invert(a).GetValue(0, 0, 0));
            Assert.Equal(2.0, new Abs(new Invert(a)).GetValue(0, 0, 0));
            Assert.Equal(1.0, new Clamp(a, -1.0, 1.0).GetValue(0, 0, 0));
        }

        // ----- Icosphere grid -----

        [Theory]
        [InlineData(0, 12)]
        [InlineData(1, 42)]
        [InlineData(2, 162)]
        [InlineData(3, 642)]
        [InlineData(4, 2562)]
        public void Icosphere_tile_count_matches_10_times_4_to_the_n_plus_2(int subdivision, int expectedTiles)
        {
            WorldGrid grid = WorldGrid.Generate(subdivision);
            Assert.Equal(expectedTiles, grid.TilesCount);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Icosphere_has_exactly_12_pentagons_and_every_other_tile_is_a_hexagon(int subdivision)
        {
            WorldGrid grid = WorldGrid.Generate(subdivision);
            int pentagons = 0, hexagons = 0;
            var neighborList = new List<int>();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                grid.GetTileNeighbors(i, neighborList);
                Assert.InRange(neighborList.Count, 5, 6);
                if (neighborList.Count == 5) pentagons++;
                else hexagons++;
            }
            Assert.Equal(12, pentagons);
            Assert.Equal(grid.TilesCount - 12, hexagons);
        }

        [Fact]
        public void Icosphere_neighbor_relationship_is_symmetric()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            for (int i = 0; i < grid.TilesCount; i++)
            {
                foreach (int neighbor in grid.NeighborsOf(i))
                {
                    Assert.True(grid.IsNeighbor(neighbor, i), $"{neighbor} lists {i} as a neighbor but not vice versa.");
                }
            }
        }

        [Fact]
        public void LongLat_stays_within_valid_ranges()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            for (int i = 0; i < grid.TilesCount; i++)
            {
                (double lat, double lon) = grid.LongLatOf(i);
                Assert.InRange(lat, -90.0, 90.0);
                Assert.InRange(lon, -180.0, 180.0);
            }
        }

        [Fact]
        public void Icosphere_is_purely_geometric_and_never_depends_on_a_seed()
        {
            WorldGrid a = WorldGrid.Generate(3);
            WorldGrid b = WorldGrid.Generate(3);
            for (int i = 0; i < a.TilesCount; i++)
            {
                Assert.Equal(a.GetTileCenter(i), b.GetTileCenter(i));
            }
        }

        // ----- Determinism -----

        [Fact]
        public void Same_seed_produces_identical_elevation_biome_and_settlements()
        {
            global::SimWorld.World.World w1 = Generate("determinism-seed", 3);
            global::SimWorld.World.World w2 = Generate("determinism-seed", 3);

            for (int i = 0; i < w1.grid.TilesCount; i++)
            {
                Assert.Equal(w1.grid.Tiles[i].elevation, w2.grid.Tiles[i].elevation);
                Assert.Equal(w1.grid.Tiles[i].biome, w2.grid.Tiles[i].biome);
            }
            Assert.Equal(w1.worldObjects.Select(o => o.tile), w2.worldObjects.Select(o => o.tile));
        }

        [Fact]
        public void Different_seeds_produce_different_terrain()
        {
            global::SimWorld.World.World w1 = Generate("seed-one", 3);
            global::SimWorld.World.World w2 = Generate("seed-two", 3);

            int differing = 0;
            for (int i = 0; i < w1.grid.TilesCount; i++)
            {
                if (w1.grid.Tiles[i].elevation != w2.grid.Tiles[i].elevation) differing++;
            }
            Assert.True(differing > w1.grid.TilesCount / 2, "Two different seeds should disagree on most tiles' elevation.");
        }

        [Fact]
        public void Land_fraction_is_in_the_plausible_thirty_to_thirty_five_percent_band()
        {
            foreach (string seed in new[] { "land-a", "land-b", "land-c" })
            {
                global::SimWorld.World.World world = Generate(seed, 3);
                int land = 0;
                for (int i = 0; i < world.grid.TilesCount; i++)
                {
                    if (!world.grid.Tiles[i].WaterCovered) land++;
                }
                float fraction = (float)land / world.grid.TilesCount;
                Assert.InRange(fraction, 0.2f, 0.5f);
            }
        }

        [Fact]
        public void Polar_tiles_are_colder_on_average_than_equatorial_tiles()
        {
            global::SimWorld.World.World world = Generate("temp-check", 4);
            float polarSum = 0f, equatorSum = 0f;
            int polarN = 0, equatorN = 0;
            for (int i = 0; i < world.grid.TilesCount; i++)
            {
                (double lat, _) = world.grid.LongLatOf(i);
                float temperature = world.grid.Tiles[i].temperature;
                if (Math.Abs(lat) > 70) { polarSum += temperature; polarN++; }
                if (Math.Abs(lat) < 15) { equatorSum += temperature; equatorN++; }
            }
            Assert.True(polarN > 0 && equatorN > 0);
            Assert.True(polarSum / polarN < equatorSum / equatorN);
        }

        [Fact]
        public void VeryHot_setting_is_warmer_on_average_than_VeryCold()
        {
            global::SimWorld.World.World hot = Generate("temp-check", 4, temperature: OverallTemperature.VeryHot);
            global::SimWorld.World.World cold = Generate("temp-check", 4, temperature: OverallTemperature.VeryCold);
            Assert.True(hot.grid.Tiles.Average(t => t.temperature) > cold.grid.Tiles.Average(t => t.temperature));
        }

        // ----- Biomes -----

        [Fact]
        public void Water_tiles_get_only_ocean_or_lake_and_land_tiles_never_do()
        {
            foreach (string seed in new[] { "biome-a", "biome-b", "biome-c" })
            {
                global::SimWorld.World.World world = Generate(seed, 4);
                for (int i = 0; i < world.grid.TilesCount; i++)
                {
                    Tile tile = world.grid.Tiles[i];
                    bool isOceanOrLake = tile.biome == BiomeDefOf.Ocean || tile.biome == BiomeDefOf.Lake;
                    if (tile.WaterCovered)
                    {
                        Assert.True(isOceanOrLake || tile.biome?.defName == "SeaIce", $"Water tile {i} got land biome {tile.biome?.defName}.");
                    }
                    else
                    {
                        Assert.False(isOceanOrLake, $"Land tile {i} got a water biome.");
                    }
                }
            }
        }

        // ----- Rivers -----

        [Fact]
        public void Rivers_only_flow_downhill_and_every_river_tile_eventually_reaches_water()
        {
            global::SimWorld.World.World world = Generate("river-check", 4);
            WorldGrid grid = world.grid;

            int riverEdgeCount = 0;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                riverEdgeCount += grid.Tiles[i].Rivers.Count(r => r.neighbor > i);
            }
            Assert.True(riverEdgeCount > 0, "Expected at least one river edge for this seed.");

            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile start = grid.Tiles[i];
                if (start.WaterCovered || start.Rivers.Count == 0) continue;

                int current = i;
                bool reachedWater = false;
                for (int steps = 0; steps <= grid.TilesCount; steps++)
                {
                    Tile tile = grid.Tiles[current];
                    if (tile.WaterCovered) { reachedWater = true; break; }
                    int next = -1;
                    float lowest = tile.elevation;
                    foreach (RiverLink link in tile.Rivers)
                    {
                        float neighborElevation = grid.Tiles[link.neighbor].elevation;
                        if (neighborElevation < lowest) { lowest = neighborElevation; next = link.neighbor; }
                    }
                    if (next < 0) break;
                    current = next;
                }
                Assert.True(reachedWater, $"River starting at tile {i} never reached water by following its downhill links.");
            }
        }

        // ----- Roads -----

        [Fact]
        public void Roads_connect_settlements_and_only_cross_passable_land()
        {
            global::SimWorld.World.World world = Generate("road-check", 4);
            WorldGrid grid = world.grid;

            int roadEdgeCount = 0;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                roadEdgeCount += tile.Roads.Count(r => r.neighbor > i);
                foreach (RoadLink link in tile.Roads)
                {
                    Assert.False(tile.WaterCovered, $"Road crosses water at tile {i}.");
                    Assert.NotNull(tile.biome);
                    Assert.False(tile.biome!.impassable, $"Road crosses an impassable biome at tile {i}.");
                    Assert.NotEqual(Hilliness.Impassable, tile.hilliness);
                }
            }
            Assert.True(roadEdgeCount > 0, "Expected at least one road edge connecting settlements for this seed.");
            Assert.True(world.Settlements.Count() >= 2);
        }

        // ----- Factions & settlements -----

        [Fact]
        public void Every_faction_def_with_a_required_count_is_created()
        {
            global::SimWorld.World.World world = Generate("faction-check", 3);
            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                int actual = world.factions.Count(f => f.def == def);
                Assert.True(actual >= def.requiredCountAtGameStart, $"{def.defName} required {def.requiredCountAtGameStart} but got {actual}.");
                Assert.True(actual <= def.maxCountAtGameStart, $"{def.defName} allowed at most {def.maxCountAtGameStart} but got {actual}.");
            }
        }

        [Fact]
        public void Settlement_counts_per_faction_and_minimum_distance_are_respected()
        {
            global::SimWorld.World.World world = Generate("settlement-check", 4);
            WorldGrid grid = world.grid;

            var byFaction = world.worldObjects
                .Where(o => o.faction != null)
                .GroupBy(o => o.faction!)
                .ToList();
            Assert.NotEmpty(byFaction);
            foreach (var group in byFaction)
            {
                Assert.InRange(group.Count(), 1, WorldGenStep_Factions.SettlementsPerFactionRange.max);
            }

            int minDistance = Math.Max(2, (int)Math.Round(20.0 * Math.Sqrt(grid.TilesCount / 100000.0)));
            var settlements = world.worldObjects;
            for (int a = 0; a < settlements.Count; a++)
            {
                for (int b = a + 1; b < settlements.Count; b++)
                {
                    Assert.True(
                        grid.ApproxDistanceInTiles(settlements[a].tile, settlements[b].tile) >= minDistance,
                        $"Settlements at {settlements[a].tile} and {settlements[b].tile} are closer than the minimum distance.");
                }
            }
        }

        // ----- Content -----

        [Fact]
        public void World_generation_content_is_fully_loaded_and_bound()
        {
            Assert.Equal(14, DefDatabase<BiomeDef>.DefCount);
            Assert.Equal(4, DefDatabase<RiverDef>.DefCount);
            Assert.Equal(4, DefDatabase<RoadDef>.DefCount);
            Assert.Equal(7, DefDatabase<WorldGenStepDef>.DefCount);
            Assert.Equal(4, DefDatabase<FactionDef>.DefCount);
            Assert.True(DefDatabase<WorldObjectDef>.DefCount >= 1);
            Assert.Equal(12, DefDatabase<DepositDef>.DefCount);
            Assert.Equal(8, DefDatabase<global::SimWorld.World.Siting.SiteWeightDef>.DefCount);

            Assert.NotNull(BiomeDefOf.Ocean);
            Assert.NotNull(BiomeDefOf.Lake);
            Assert.NotNull(BiomeDefOf.TemperateForest);
            Assert.NotNull(BiomeDefOf.Desert);
            Assert.NotNull(BiomeDefOf.Tundra);
            Assert.NotNull(BiomeDefOf.IceSheet);
            Assert.NotNull(WorldObjectDefOf.Settlement);
            Assert.NotNull(FactionDefOf.PlayerCivilization);
            Assert.True(FactionDefOf.PlayerCivilization.isPlayer);

            Assert.NotNull(DepositDefOf.FreshWater);
            Assert.NotNull(DepositDefOf.ArableSoil);
            Assert.NotNull(DepositDefOf.Clay);
            Assert.NotNull(DepositDefOf.Flint);
            Assert.NotNull(DepositDefOf.Stone);
            Assert.NotNull(DepositDefOf.Ore);
            Assert.NotNull(DepositDefOf.Coal);
            Assert.NotNull(DepositDefOf.Salt);
            Assert.NotNull(DepositDefOf.Timber);
            Assert.NotNull(DepositDefOf.Game);
            Assert.NotNull(DepositDefOf.Ford);
            Assert.NotNull(DepositDefOf.DefensibleGround);
        }

        // ----- Scribe -----

        [Fact]
        public void Scribe_round_trip_regenerates_an_identical_grid_and_keeps_settlements()
        {
            global::SimWorld.World.World original = Generate("scribe-check", 2);
            string xml = Scribe.SaveToString(original, "world");

            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Equal(original.grid.TilesCount, loaded.grid.TilesCount);
            for (int i = 0; i < original.grid.TilesCount; i++)
            {
                Assert.Equal(original.grid.Tiles[i].elevation, loaded.grid.Tiles[i].elevation);
                Assert.Equal(original.grid.Tiles[i].biome, loaded.grid.Tiles[i].biome);
                Assert.Equal(original.grid.Tiles[i].hilliness, loaded.grid.Tiles[i].hilliness);
            }

            Assert.Equal(original.worldObjects.Count, loaded.worldObjects.Count);
            Assert.Equal(
                original.worldObjects.Select(o => (o.tile, o.def.defName, o.faction?.name)),
                loaded.worldObjects.Select(o => (o.tile, o.def.defName, o.faction?.name)));
            Assert.Equal(
                original.factions.Select(f => (f.def.defName, f.name)),
                loaded.factions.Select(f => (f.def.defName, f.name)));
        }
    }
}
