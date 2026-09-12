using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.MapGen;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

namespace SimWorld.Tests.MapGen
{
    /// <summary>
    /// Local map generation: the GenStep pipeline, the tile→map correspondence (spec §5, §11.2's "the seam"),
    /// determinism and Scribe round-tripping. "Map" and "World" are qualified <c>global::</c> throughout —
    /// this test namespace's own last segment ("MapGen") doesn't collide with either, but <c>SimWorld.Tests.Map</c>
    /// and <c>SimWorld.Tests.World</c> exist elsewhere in the suite and would otherwise shadow those two types.
    /// </summary>
    public class MapGenTests : ContentTestBase
    {
        public MapGenTests(CoreContentFixture content) : base(content)
        {
        }

        private static Tile MakeTile(
            Hilliness hilliness = Hilliness.Flat,
            float elevation = 100f,
            float rainfall = 1200f,
            float swampiness = 0f,
            string biomeDefName = "TemperateForest",
            bool river = false,
            params (DepositDef def, float magnitude)[] deposits)
        {
            var tile = new Tile
            {
                biome = DefDatabase<BiomeDef>.GetNamed(biomeDefName),
                hilliness = hilliness,
                elevation = elevation,
                rainfall = rainfall,
                swampiness = swampiness,
            };
            if (river)
            {
                RiverDef riverDef = DefDatabase<RiverDef>.GetNamed("River");
                tile.potentialRivers.Add(new RiverLink(999, riverDef));
            }
            foreach ((DepositDef def, float magnitude) in deposits)
            {
                tile.deposits.Add(new TileDeposit(def, magnitude));
            }
            return tile;
        }

        private static int RockCount(global::SimWorld.Map.Map map)
        {
            int count = 0;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (map.edificeGrid[c] is { } edifice && edifice.def.mineable) count++;
            }
            return count;
        }

        private static int WaterCellCount(global::SimWorld.Map.Map map)
        {
            int count = 0;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (map.terrainGrid.TerrainAt(c).IsWater) count++;
            }
            return count;
        }

        private static int ThingCountOfDef(global::SimWorld.Map.Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Count;

        // ----- Content -----

        [Fact]
        public void MapGen_content_is_fully_loaded_and_bound()
        {
            // Nine since GenSteps_Animals.xml added the wildlife step (see SimWorld.MapGen.GenStep_Animals).
            Assert.Equal(9, DefDatabase<GenStepDef>.DefCount);
            Assert.Equal(1, DefDatabase<MapGeneratorDef>.DefCount);
            Assert.NotNull(MapGeneratorDefOf.Base);
            Assert.Equal(9, MapGeneratorDefOf.Base.genSteps.Count);

            Assert.NotNull(MapGenTerrainDefOf.SoilRich);
            Assert.NotNull(MapGenTerrainDefOf.Marsh);
            Assert.NotNull(MapGenTerrainDefOf.MarshyTerrain);
            Assert.NotNull(MapGenTerrainDefOf.StreetDirtPath);
            Assert.NotNull(MapGenTerrainDefOf.StreetDirtRoad);
            Assert.NotNull(MapGenTerrainDefOf.StreetStoneRoad);
            Assert.NotNull(MapGenTerrainDefOf.StreetAncientAsphalt);

            Assert.NotNull(MapGenThingDefOf.Sandstone);
            Assert.NotNull(MapGenThingDefOf.Granite);
            Assert.NotNull(MapGenThingDefOf.Limestone);
            Assert.NotNull(MapGenThingDefOf.MineableSteel);
            Assert.NotNull(MapGenThingDefOf.ChunkSandstone);
            Assert.NotNull(MapGenThingDefOf.ChunkGranite);
            Assert.NotNull(MapGenThingDefOf.ChunkLimestone);
            Assert.NotNull(MapGenThingDefOf.WildPlant);

            Assert.NotNull(MapGenThingDefOf.Wall);
            Assert.NotNull(MapGenThingDefOf.BlocksSandstone);
            Assert.NotNull(MapGenThingDefOf.Steel);
            Assert.NotNull(MapGenThingDefOf.WoodLog);
            Assert.NotNull(MapGenThingDefOf.Silver);
            Assert.NotNull(MapGenThingDefOf.MeleeWeapon_Knife);
            Assert.NotNull(MapGenThingDefOf.MeleeWeapon_Club);
            Assert.NotNull(MapGenThingDefOf.Bow_Short);
        }

        // ----- Determinism -----

        [Fact]
        public void Same_seed_and_tile_produce_an_identical_map()
        {
            Tile tile = MakeTile(Hilliness.Mountainous, deposits: (DepositDefOf.Ore, 0.6f));
            var size = new global::SimWorld.Map.IntVec2(40, 40);

            global::SimWorld.Map.Map a = MapGenerator.GenerateMap(tile, 5, "determinism-seed", size);
            global::SimWorld.Map.Map b = MapGenerator.GenerateMap(tile, 5, "determinism-seed", size);

            foreach (global::SimWorld.Map.IntVec3 c in a.AllCells)
            {
                Assert.Equal(a.terrainGrid.TerrainAt(c), b.terrainGrid.TerrainAt(c));
                Assert.Equal(a.roofGrid.RoofAt(c), b.roofGrid.RoofAt(c));
            }

            var aThings = a.listerThings.AllThings.Select(t => (t.def.defName, t.Position)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            var bThings = b.listerThings.AllThings.Select(t => (t.def.defName, t.Position)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            Assert.Equal(aThings, bThings);
        }

        [Fact]
        public void Different_seeds_produce_different_terrain_texture()
        {
            Tile tile = MakeTile(Hilliness.LargeHills);
            var size = new global::SimWorld.Map.IntVec2(40, 40);

            global::SimWorld.Map.Map a = MapGenerator.GenerateMap(tile, 1, "seed-one", size);
            global::SimWorld.Map.Map b = MapGenerator.GenerateMap(tile, 1, "seed-two", size);

            int differing = 0;
            foreach (global::SimWorld.Map.IntVec3 c in a.AllCells)
            {
                if (a.terrainGrid.TerrainAt(c) != b.terrainGrid.TerrainAt(c)) differing++;
            }
            Assert.True(differing > 0, "Two different seeds on the same tile should disagree on at least some terrain.");
        }

        // ----- The seam: tile -> map correspondence -----

        [Fact]
        public void A_tile_with_an_ore_deposit_produces_a_map_with_ore_and_a_tile_without_does_not()
        {
            var size = new global::SimWorld.Map.IntVec2(50, 50);
            Tile withOre = MakeTile(Hilliness.Mountainous, deposits: (DepositDefOf.Ore, 0.8f));
            Tile withoutOre = MakeTile(Hilliness.Mountainous);

            global::SimWorld.Map.Map mapWithOre = MapGenerator.GenerateMap(withOre, 1, "ore-check-with", size);
            global::SimWorld.Map.Map mapWithoutOre = MapGenerator.GenerateMap(withoutOre, 1, "ore-check-without", size);

            Assert.True(ThingCountOfDef(mapWithOre, MapGenThingDefOf.MineableSteel) > 0, "A tile with an Ore deposit should produce ore veins on its map.");
            Assert.Equal(0, ThingCountOfDef(mapWithoutOre, MapGenThingDefOf.MineableSteel));
        }

        [Fact]
        public void A_tile_on_a_river_produces_a_map_with_water_crossing_it_and_a_tile_without_does_not()
        {
            var size = new global::SimWorld.Map.IntVec2(50, 50);
            Tile riverTile = MakeTile(Hilliness.Flat, river: true);
            Tile noRiverTile = MakeTile(Hilliness.Flat, river: false);

            global::SimWorld.Map.Map riverMap = MapGenerator.GenerateMap(riverTile, 1, "river-check-wet", size);
            global::SimWorld.Map.Map dryMap = MapGenerator.GenerateMap(noRiverTile, 1, "river-check-dry", size);

            Assert.True(WaterCellCount(riverMap) > 0, "A tile on a river should produce a map with water crossing it.");
            Assert.Equal(0, WaterCellCount(dryMap));
        }

        [Fact]
        public void Higher_hilliness_produces_more_rock_on_average()
        {
            var size = new global::SimWorld.Map.IntVec2(50, 50);
            int flat = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.Flat), 1, "hilliness-check-flat", size));
            int small = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.SmallHills), 1, "hilliness-check-small", size));
            int large = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.LargeHills), 1, "hilliness-check-large", size));
            int mountainous = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.Mountainous), 1, "hilliness-check-mountainous", size));
            int impassable = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.Impassable), 1, "hilliness-check-impassable", size));

            Assert.Equal(0, flat);
            Assert.True(small < large, $"SmallHills ({small}) should have less rock than LargeHills ({large}).");
            Assert.True(large < mountainous, $"LargeHills ({large}) should have less rock than Mountainous ({mountainous}).");
            Assert.True(mountainous < impassable, $"Mountainous ({mountainous}) should have less rock than Impassable ({impassable}).");
            Assert.True(impassable < size.Area, "Even Impassable hilliness should leave some open ground on the map.");
        }

        [Fact]
        public void A_stone_deposit_increases_rock_coverage_beyond_hilliness_alone()
        {
            var size = new global::SimWorld.Map.IntVec2(50, 50);
            int plain = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.SmallHills), 1, "stone-check-plain", size));
            int rich = RockCount(MapGenerator.GenerateMap(MakeTile(Hilliness.SmallHills, deposits: (DepositDefOf.Stone, 1f)), 1, "stone-check-rich", size));
            Assert.True(rich > plain, $"A rich Stone deposit ({rich}) should read as more rock than none ({plain}).");
        }

        [Fact]
        public void Richer_biome_plant_density_and_timber_scatter_more_wild_plants()
        {
            var size = new global::SimWorld.Map.IntVec2(60, 60);
            Tile lush = MakeTile(Hilliness.Flat, biomeDefName: "TropicalRainforest", rainfall: 3000f, deposits: (DepositDefOf.Timber, 1f));
            Tile sparse = MakeTile(Hilliness.Flat, biomeDefName: "ExtremeDesert", rainfall: 40f);

            global::SimWorld.Map.Map lushMap = MapGenerator.GenerateMap(lush, 1, "plant-check-lush", size);
            global::SimWorld.Map.Map sparseMap = MapGenerator.GenerateMap(sparse, 1, "plant-check-sparse", size);

            int lushCount = ThingCountOfDef(lushMap, MapGenThingDefOf.WildPlant);
            int sparseCount = ThingCountOfDef(sparseMap, MapGenThingDefOf.WildPlant);
            Assert.True(lushCount > sparseCount, $"A lush biome ({lushCount} plants) should scatter more wild plants than a sparse one ({sparseCount}).");
        }

        // ----- Caves -----

        [Fact]
        public void Caves_carve_open_passages_through_previously_solid_rock()
        {
            Tile tile = MakeTile(Hilliness.Impassable);
            var map = new global::SimWorld.Map.Map(50, 50, global::SimWorld.Map.TerrainDefOf.Soil);
            var ctx = new MapGenContext(map, tile, 1, "cave-check");

            new GenStep_ElevationFertility { def = StepDef("ElevationFertility") }.Generate(ctx);
            new GenStep_Terrain { def = StepDef("MapTerrain") }.Generate(ctx);
            new GenStep_RocksAndMountains { def = StepDef("RocksAndMountains") }.Generate(ctx);
            int rockBeforeCaves = ctx.rock.Count(r => r);

            new GenStep_Caves { def = StepDef("Caves") }.Generate(ctx);
            int rockAfterCaves = ctx.rock.Count(r => r);

            Assert.True(rockBeforeCaves > 0, "Impassable hilliness should place some rock to carve through.");
            Assert.True(rockAfterCaves < rockBeforeCaves, "Carving caves should clear some previously-solid rock cells.");
        }

        // ----- Roofs -----

        [Fact]
        public void Rock_cells_get_a_thick_roof_and_flat_open_ground_gets_none()
        {
            var size = new global::SimWorld.Map.IntVec2(40, 40);
            global::SimWorld.Map.Map mountain = MapGenerator.GenerateMap(MakeTile(Hilliness.Impassable), 1, "roof-check", size);
            global::SimWorld.Map.Map flat = MapGenerator.GenerateMap(MakeTile(Hilliness.Flat), 2, "roof-check", size);

            bool anyThickRoof = false, anyThinRoof = false;
            foreach (global::SimWorld.Map.IntVec3 c in mountain.AllCells)
            {
                global::SimWorld.Map.RoofDef? roof = mountain.roofGrid.RoofAt(c);
                if (roof == global::SimWorld.Map.RoofDefOf.RoofRockThick) anyThickRoof = true;
                if (roof == global::SimWorld.Map.RoofDefOf.RoofRockThin) anyThinRoof = true;
            }
            Assert.True(anyThickRoof, "A near-total mountain map should have some thick-roofed rock.");
            Assert.True(anyThinRoof, "A near-total mountain map should have some thin-roofed cave passage.");

            foreach (global::SimWorld.Map.IntVec3 c in flat.AllCells)
            {
                Assert.False(flat.roofGrid.Roofed(c), "Flat, non-mountain ground should never be roofed.");
            }
        }

        // ----- The seam entry point -----

        [Fact]
        public void GenerateMapFor_reads_the_settlements_world_tile_and_defaults_to_250_by_250()
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld("mapgen-seam-seed", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 3);
            global::SimWorld.World.WorldObject settlement = world.Settlements.First();

            global::SimWorld.Map.Map map = MapGenerator.GenerateMapFor(settlement, world);

            Assert.Equal(settlement.tile, map.tile);
            Assert.Equal(MapGenerator.DefaultMapSizeX, map.Size.x);
            Assert.Equal(MapGenerator.DefaultMapSizeZ, map.Size.z);
        }

        [Fact]
        public void GenerateMapFor_honours_an_explicit_size_override()
        {
            global::SimWorld.World.World world = WorldGenerator.GenerateWorld("mapgen-size-seed", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", 3);
            global::SimWorld.World.WorldObject settlement = world.Settlements.First();

            global::SimWorld.Map.Map map = MapGenerator.GenerateMapFor(settlement, world, new global::SimWorld.Map.IntVec2(30, 35));

            Assert.Equal(30, map.Size.x);
            Assert.Equal(35, map.Size.z);
        }

        // ----- Interior map sizing (Settlement.EnterMap's own tuning) -----

        [Fact]
        public void MapSizeForPopulation_never_shrinks_as_population_grows()
        {
            int[] populations = { 0, 10, 39, 40, 90, 250, 900, 3000, 50000 };
            global::SimWorld.Map.IntVec2 previous = MapGenTuning.MapSizeForPopulation(populations[0]);
            foreach (int population in populations.Skip(1))
            {
                global::SimWorld.Map.IntVec2 size = MapGenTuning.MapSizeForPopulation(population);
                Assert.True(size.x >= previous.x, $"Population {population} produced a smaller map ({size.x}) than a lower population did ({previous.x}).");
                Assert.Equal(size.x, size.z); // square, like every other map this generator produces
                previous = size;
            }
            Assert.True(previous.x > MapGenTuning.MapSizeForPopulation(populations[0]).x,
                "A huge settlement should end up with a strictly larger map than an empty one, not a no-op constant.");
        }

        // ----- Roads: world-tile roads carried onto the map as a street (spec §5, §11.2) -----

        private static (int tileId, int neighborA, int neighborB) DivergentNeighborPair(WorldGrid grid)
        {
            for (int tileId = 0; tileId < grid.TilesCount; tileId++)
            {
                IReadOnlyList<int> neighbors = grid.NeighborsOf(tileId);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    for (int j = i + 1; j < neighbors.Count; j++)
                    {
                        global::SimWorld.Vector3 da = (grid.GetTileCenter(neighbors[i]) - grid.GetTileCenter(tileId)).Normalized;
                        global::SimWorld.Vector3 db = (grid.GetTileCenter(neighbors[j]) - grid.GetTileCenter(tileId)).Normalized;
                        // More than 90 degrees apart: unambiguously different sides of the settlement, not just noise.
                        if (global::SimWorld.Vector3.Dot(da, db) < 0f)
                        {
                            return (tileId, neighbors[i], neighbors[j]);
                        }
                    }
                }
            }
            throw new InvalidOperationException("No sufficiently divergent neighbour pair found at this subdivision level.");
        }

        private static global::SimWorld.Map.IntVec3 ClosestStreetCellToBorder(global::SimWorld.Map.Map map, global::SimWorld.Map.TerrainDef streetTerrain)
        {
            global::SimWorld.Map.IntVec3 best = default;
            int bestDist = int.MaxValue;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (map.terrainGrid.TerrainAt(c) != streetTerrain) continue;
                int distToEdge = Math.Min(Math.Min(c.x, map.Size.x - 1 - c.x), Math.Min(c.z, map.Size.z - 1 - c.z));
                if (distToEdge < bestDist)
                {
                    bestDist = distToEdge;
                    best = c;
                }
            }
            Assert.True(bestDist != int.MaxValue, "Expected at least one street cell.");
            return best;
        }

        [Fact]
        public void A_tile_with_a_road_produces_a_map_with_a_street_and_a_tile_without_does_not()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            RoadDef road = DefDatabase<RoadDef>.GetNamed("DirtRoad");
            var size = new global::SimWorld.Map.IntVec2(60, 60);
            const int tileId = 0;
            int neighbor = grid.NeighborsOf(tileId)[0];

            Tile withRoad = MakeTile(Hilliness.Flat);
            withRoad.potentialRoads.Add(new RoadLink(neighbor, road));
            Tile withoutRoad = MakeTile(Hilliness.Flat);

            global::SimWorld.Map.Map roadMap = MapGenerator.GenerateMap(withRoad, tileId, "road-presence", size, grid: grid);
            global::SimWorld.Map.Map plainMap = MapGenerator.GenerateMap(withoutRoad, tileId, "road-presence", size, grid: grid);

            int streetCells = roadMap.AllCells.Count(c => roadMap.terrainGrid.TerrainAt(c) == MapGenTerrainDefOf.StreetDirtRoad);
            Assert.True(streetCells > 0, "A tile with a road link should produce a map with some street terrain.");
            Assert.Equal(0, plainMap.AllCells.Count(c => plainMap.terrainGrid.TerrainAt(c) == MapGenTerrainDefOf.StreetDirtRoad));
        }

        [Fact]
        public void A_road_with_no_grid_available_draws_nothing_rather_than_guess_a_heading()
        {
            RoadDef road = DefDatabase<RoadDef>.GetNamed("DirtRoad");
            var size = new global::SimWorld.Map.IntVec2(50, 50);
            Tile tile = MakeTile(Hilliness.Flat);
            tile.potentialRoads.Add(new RoadLink(999, road));

            // No grid argument: the tile-only overload every other test in this file already uses.
            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, 1, "road-no-grid", size);

            Assert.Equal(0, map.AllCells.Count(c => map.terrainGrid.TerrainAt(c) == MapGenTerrainDefOf.StreetDirtRoad));
        }

        [Fact]
        public void A_road_enters_the_map_from_the_side_facing_its_real_neighbour_not_a_fixed_or_opposite_one()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            (int tileId, int neighborA, int neighborB) = DivergentNeighborPair(grid);
            RoadDef road = DefDatabase<RoadDef>.GetNamed("DirtRoad");
            var size = new global::SimWorld.Map.IntVec2(60, 60);

            Tile tileA = MakeTile(Hilliness.Flat);
            tileA.potentialRoads.Add(new RoadLink(neighborA, road));
            Tile tileB = MakeTile(Hilliness.Flat);
            tileB.potentialRoads.Add(new RoadLink(neighborB, road));

            // Same seed string for both: direction is the only thing that differs between the two maps.
            global::SimWorld.Map.Map mapA = MapGenerator.GenerateMap(tileA, tileId, "road-direction", size, grid: grid);
            global::SimWorld.Map.Map mapB = MapGenerator.GenerateMap(tileB, tileId, "road-direction", size, grid: grid);

            global::SimWorld.Map.IntVec3 entryA = ClosestStreetCellToBorder(mapA, MapGenTerrainDefOf.StreetDirtRoad);
            global::SimWorld.Map.IntVec3 entryB = ClosestStreetCellToBorder(mapB, MapGenTerrainDefOf.StreetDirtRoad);

            double apart = Math.Sqrt(Math.Pow(entryA.x - entryB.x, 2) + Math.Pow(entryA.z - entryB.z, 2));
            Assert.True(apart > size.x * 0.3,
                $"Two neighbours more than 90 degrees apart from {tileId} should enter the map at clearly different points ({entryA} vs {entryB}, {apart:F1} cells apart).");
        }

        [Fact]
        public void A_river_enters_the_map_from_the_side_facing_the_tile_it_flows_to()
        {
            // Rivers used to pick their axis with a coin flip even though RiverLink carries the same real
            // neighbour data RoadLink does. Same shape of proof as the road test: two tiles differing only in
            // which neighbour their river runs to must put their water in clearly different places.
            WorldGrid grid = WorldGrid.Generate(3);
            (int tileId, int neighborA, int neighborB) = DivergentNeighborPair(grid);
            RiverDef river = DefDatabase<RiverDef>.GetNamed("River");
            var size = new global::SimWorld.Map.IntVec2(60, 60);

            Tile tileA = MakeTile(Hilliness.Flat);
            tileA.potentialRivers.Add(new RiverLink(neighborA, river));
            Tile tileB = MakeTile(Hilliness.Flat);
            tileB.potentialRivers.Add(new RiverLink(neighborB, river));

            // Same seed string for both: direction is the only thing that differs.
            global::SimWorld.Map.Map mapA = MapGenerator.GenerateMap(tileA, tileId, "river-direction", size, grid: grid);
            global::SimWorld.Map.Map mapB = MapGenerator.GenerateMap(tileB, tileId, "river-direction", size, grid: grid);

            global::SimWorld.Map.IntVec3 entryA = ClosestWaterCellToBorder(mapA);
            global::SimWorld.Map.IntVec3 entryB = ClosestWaterCellToBorder(mapB);

            double apart = Math.Sqrt(Math.Pow(entryA.x - entryB.x, 2) + Math.Pow(entryA.z - entryB.z, 2));
            Assert.True(apart > size.x * 0.3,
                $"A river to two neighbours more than 90 degrees apart from {tileId} should enter at clearly different points ({entryA} vs {entryB}, {apart:F1} cells apart).");
        }

        [Fact]
        public void A_river_still_crosses_the_map_when_no_grid_is_available()
        {
            // The fallback path: no grid means no real heading, so the axis is still a coin flip — but the one
            // correspondence spec §5 calls load-bearing (a river tile always produces water crossing the map)
            // must hold either way.
            RiverDef river = DefDatabase<RiverDef>.GetNamed("River");
            Tile tile = MakeTile(Hilliness.Flat);
            tile.potentialRivers.Add(new RiverLink(999, river));
            var size = new global::SimWorld.Map.IntVec2(60, 60);

            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, 1, "river-no-grid", size);

            Assert.True(WaterCellCount(map) > 0);
            Assert.True(TouchesTwoOppositeBorders(map), "A river should cross the map, not stop inside it.");
        }

        private static global::SimWorld.Map.IntVec3 ClosestWaterCellToBorder(global::SimWorld.Map.Map map)
        {
            global::SimWorld.Map.IntVec3 best = default;
            int bestDist = int.MaxValue;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (!IsWater(map, c)) continue;
                int distToEdge = Math.Min(Math.Min(c.x, map.Size.x - 1 - c.x), Math.Min(c.z, map.Size.z - 1 - c.z));
                if (distToEdge < bestDist)
                {
                    bestDist = distToEdge;
                    best = c;
                }
            }
            Assert.True(bestDist != int.MaxValue, "Expected at least one water cell.");
            return best;
        }

        private static bool IsWater(global::SimWorld.Map.Map map, global::SimWorld.Map.IntVec3 c)
        {
            global::SimWorld.Map.TerrainDef t = map.terrainGrid.TerrainAt(c);
            return t == global::SimWorld.Map.TerrainDefOf.WaterShallow || t == global::SimWorld.Map.TerrainDefOf.WaterDeep;
        }

        private static bool TouchesTwoOppositeBorders(global::SimWorld.Map.Map map)
        {
            bool west = false, east = false, south = false, north = false;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (!IsWater(map, c)) continue;
                if (c.x == 0) west = true;
                if (c.x == map.Size.x - 1) east = true;
                if (c.z == 0) south = true;
                if (c.z == map.Size.z - 1) north = true;
            }
            return (west && east) || (south && north);
        }

        [Fact]
        public void Same_seed_tile_and_grid_produce_an_identical_street()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            int tileId = 0;
            int neighbor = grid.NeighborsOf(tileId)[0];
            RoadDef road = DefDatabase<RoadDef>.GetNamed("StoneRoad");
            var size = new global::SimWorld.Map.IntVec2(50, 50);

            Tile MakeRoadTile()
            {
                Tile t = MakeTile(Hilliness.SmallHills);
                t.potentialRoads.Add(new RoadLink(neighbor, road));
                return t;
            }

            global::SimWorld.Map.Map a = MapGenerator.GenerateMap(MakeRoadTile(), tileId, "road-determinism", size, grid: grid);
            global::SimWorld.Map.Map b = MapGenerator.GenerateMap(MakeRoadTile(), tileId, "road-determinism", size, grid: grid);

            foreach (global::SimWorld.Map.IntVec3 c in a.AllCells)
            {
                Assert.Equal(a.terrainGrid.TerrainAt(c), b.terrainGrid.TerrainAt(c));
            }
        }

        [Fact]
        public void Two_road_links_of_different_priority_leave_the_higher_priority_terrain_showing_where_they_overlap()
        {
            WorldGrid grid = WorldGrid.Generate(3);
            (int tileId, int neighborA, int neighborB) = DivergentNeighborPair(grid);
            RoadDef low = DefDatabase<RoadDef>.GetNamed("DirtPath");
            RoadDef high = DefDatabase<RoadDef>.GetNamed("StoneRoad");
            var size = new global::SimWorld.Map.IntVec2(60, 60);

            Tile tile = MakeTile(Hilliness.Flat);
            tile.potentialRoads.Add(new RoadLink(neighborA, low));
            tile.potentialRoads.Add(new RoadLink(neighborB, high));

            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, tileId, "road-priority", size, grid: grid);

            // Both spokes run to the map's own centre, so the centre cell is where they overlap.
            var center = new global::SimWorld.Map.IntVec3(size.x / 2, 0, size.x / 2);
            Assert.Equal(MapGenTerrainDefOf.StreetStoneRoad, map.terrainGrid.TerrainAt(center));
        }

        // ----- Ruins (GenStep_Ruins; spec: mapgen.ruins) -----

        private static int WallCount(global::SimWorld.Map.Map map) => ThingCountOfDef(map, MapGenThingDefOf.Wall);

        private static int RoofCountOfDef(global::SimWorld.Map.Map map, global::SimWorld.Map.RoofDef roof)
        {
            int count = 0;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (map.roofGrid.RoofAt(c) == roof) count++;
            }
            return count;
        }

        [Fact]
        public void Ruins_place_a_believable_number_of_wall_segments_not_a_scatter_or_a_maze()
        {
            var size = new global::SimWorld.Map.IntVec2(100, 100);
            Tile tile = MakeTile(Hilliness.Flat);
            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, 1, "ruins-believable", size);

            int walls = WallCount(map);
            Assert.True(walls > 0, "Expected at least one ruin wall segment on a 100x100 flat map with this seed.");
            Assert.True(walls < size.Area / 10, $"Ruin walls ({walls}) should stay a small minority of the map, not fill it.");
        }

        [Fact]
        public void Ruin_walls_are_weathered_below_full_hit_points_but_never_to_zero()
        {
            var size = new global::SimWorld.Map.IntVec2(100, 100);
            Tile tile = MakeTile(Hilliness.Flat);
            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, 1, "ruins-weathered", size);

            IReadOnlyList<Thing> walls = map.listerThings.ThingsOfDef(MapGenThingDefOf.Wall);
            Assert.True(walls.Count > 0, "Expected ruin walls on this seed to check weathering against.");

            bool anyWeathered = false;
            foreach (Thing wall in walls)
            {
                Assert.True(wall.HitPoints >= 1, "A spawned wall should never carry zero or negative hit points.");
                Assert.True(wall.HitPoints <= wall.MaxHitPoints, "A spawned wall should never exceed its own max hit points.");
                if (wall.HitPoints < wall.MaxHitPoints) anyWeathered = true;
            }
            Assert.True(anyWeathered, "At least one ruin wall should read as weathered (below full hit points), not pristine.");
        }

        [Fact]
        public void Same_seed_produces_identical_ruins()
        {
            Tile tile = MakeTile(Hilliness.Flat);
            var size = new global::SimWorld.Map.IntVec2(100, 100);

            global::SimWorld.Map.Map a = MapGenerator.GenerateMap(tile, 1, "ruins-determinism", size);
            global::SimWorld.Map.Map b = MapGenerator.GenerateMap(tile, 1, "ruins-determinism", size);

            var wallsA = a.listerThings.ThingsOfDef(MapGenThingDefOf.Wall)
                .Select(t => (t.Position, t.HitPoints)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            var wallsB = b.listerThings.ThingsOfDef(MapGenThingDefOf.Wall)
                .Select(t => (t.Position, t.HitPoints)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();

            Assert.True(wallsA.Count > 0, "Expected ruin walls on this seed to check determinism against.");
            Assert.Equal(wallsA, wallsB);
        }

        [Fact]
        public void Ruin_density_scales_with_map_area()
        {
            Tile tile = MakeTile(Hilliness.Flat);
            var smallSize = new global::SimWorld.Map.IntVec2(60, 60);
            var largeSize = new global::SimWorld.Map.IntVec2(180, 180);

            int smallTotal = 0, largeTotal = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                string seedString = "ruins-density-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                smallTotal += WallCount(MapGenerator.GenerateMap(tile, seed, seedString, smallSize));
                largeTotal += WallCount(MapGenerator.GenerateMap(tile, seed, seedString, largeSize));
            }
            Assert.True(largeTotal > smallTotal,
                $"A map 9x the area ({largeTotal} wall cells across 8 seeds) should place more ruin material on average than a small one ({smallTotal}).");
        }

        [Fact]
        public void Some_ruins_are_roofed_and_some_carry_loot_over_enough_seeds()
        {
            // Flat, with no rock/water of its own: the only source of RoofConstructed on such a map is a ruin.
            Tile tile = MakeTile(Hilliness.Flat);
            var size = new global::SimWorld.Map.IntVec2(80, 80);

            int roofedCells = 0;
            int lootCount = 0;
            for (int seed = 0; seed < 12; seed++)
            {
                global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, seed, "ruins-flavor-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture), size);
                roofedCells += RoofCountOfDef(map, global::SimWorld.Map.RoofDefOf.RoofConstructed);
                lootCount += ThingCountOfDef(map, MapGenThingDefOf.Silver)
                    + ThingCountOfDef(map, MapGenThingDefOf.MeleeWeapon_Knife)
                    + ThingCountOfDef(map, MapGenThingDefOf.MeleeWeapon_Club)
                    + ThingCountOfDef(map, MapGenThingDefOf.Bow_Short);
            }
            Assert.True(roofedCells > 0, "At least one ruin, over 12 seeds, should come up roofed.");
            Assert.True(lootCount > 0, "At least one ruin, over 12 seeds, should carry loot.");
        }

        [Fact]
        public void Ruins_never_wall_off_a_pocket_the_region_graph_cannot_reach_from_the_border()
        {
            // A stress case: a big flat map (no rock or water of its own) crowded with ruins, so any
            // enclosure this step could produce gets every chance to show up. Flat + no river keeps every
            // non-ruin cell walkable, so cell (0,0) — outside every ruin's RuinEdgeMargin by construction —
            // is a safe, always-walkable anchor to reach every other walkable cell from.
            Tile tile = MakeTile(Hilliness.Flat);
            var size = new global::SimWorld.Map.IntVec2(160, 160);
            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(tile, 1, "ruins-reachability", size);

            Assert.True(WallCount(map) > 0, "Expected this seed/size to place ruin walls to actually test enclosure against.");

            map.regionAndRoomUpdater.RebuildIfNeeded();
            var origin = new global::SimWorld.Map.IntVec3(0, 0, 0);
            global::SimWorld.Map.Region? borderRegion = map.regionGrid.RegionAt(origin);
            Assert.NotNull(borderRegion);

            int walkableChecked = 0;
            foreach (global::SimWorld.Map.IntVec3 c in map.AllCells)
            {
                if (!global::SimWorld.Map.GenGrid.Walkable(c, map)) continue;
                global::SimWorld.Map.Region? here = map.regionGrid.RegionAt(c);
                Assert.NotNull(here);
                Assert.True(global::SimWorld.Map.RegionTraverser.WithinRegions(borderRegion!, here!),
                    $"Cell {c} is walkable but unreachable from the map border through the region graph — a ruin walled off a pocket.");
                walkableChecked++;
            }
            Assert.True(walkableChecked > size.Area / 2, "Expected the large majority of a flat, ruin-scattered map to remain walkable.");
        }

        // ----- Scribe -----

        [Fact]
        public void Scribe_round_trip_preserves_terrain_roofs_and_every_spawned_thing()
        {
            Tile tile = MakeTile(Hilliness.Mountainous, river: true, deposits: new[] { (DepositDefOf.Ore, 0.5f), (DepositDefOf.Timber, 0.8f) });
            var size = new global::SimWorld.Map.IntVec2(35, 35);
            global::SimWorld.Map.Map original = MapGenerator.GenerateMap(tile, 7, "scribe-check", size);

            string xml = Scribe.SaveToString(original, "map");
            global::SimWorld.Map.Map loaded = Scribe.Load<global::SimWorld.Map.Map>(xml, "map", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            foreach (global::SimWorld.Map.IntVec3 c in original.AllCells)
            {
                Assert.Equal(original.terrainGrid.TerrainAt(c), loaded.terrainGrid.TerrainAt(c));
                Assert.Equal(original.roofGrid.RoofAt(c), loaded.roofGrid.RoofAt(c));
            }

            var originalThings = original.listerThings.AllThings.Select(t => (t.def.defName, t.Position)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            var loadedThings = loaded.listerThings.AllThings.Select(t => (t.def.defName, t.Position)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            Assert.Equal(originalThings, loadedThings);
        }

        private static GenStepDef StepDef(string defName) => DefDatabase<GenStepDef>.GetNamed(defName);
    }
}
