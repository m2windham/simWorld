using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.MapGen;
using SimWorld.Sim;
using SimWorld.Tests.Content;
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
            Assert.Equal(6, DefDatabase<GenStepDef>.DefCount);
            Assert.Equal(1, DefDatabase<MapGeneratorDef>.DefCount);
            Assert.NotNull(MapGeneratorDefOf.Base);
            Assert.Equal(6, MapGeneratorDefOf.Base.genSteps.Count);

            Assert.NotNull(MapGenTerrainDefOf.SoilRich);
            Assert.NotNull(MapGenTerrainDefOf.Marsh);
            Assert.NotNull(MapGenTerrainDefOf.MarshyTerrain);

            Assert.NotNull(MapGenThingDefOf.Sandstone);
            Assert.NotNull(MapGenThingDefOf.Granite);
            Assert.NotNull(MapGenThingDefOf.Limestone);
            Assert.NotNull(MapGenThingDefOf.MineableSteel);
            Assert.NotNull(MapGenThingDefOf.ChunkSandstone);
            Assert.NotNull(MapGenThingDefOf.ChunkGranite);
            Assert.NotNull(MapGenThingDefOf.ChunkLimestone);
            Assert.NotNull(MapGenThingDefOf.WildPlant);
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
