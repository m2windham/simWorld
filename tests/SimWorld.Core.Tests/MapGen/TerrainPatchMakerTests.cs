using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Noise;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Siting;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.MapGen
{
    /// <summary>
    /// A biome's <c>terrainPatchMakers</c> lay patches over its fertility bands (RimWorld:
    /// <c>TerrainPatchMaker</c>, consulted first in <c>GenStep_Terrain.TerrainFrom</c>). Before they were
    /// ported, a forest map was soil and almost nothing else: its bands put rich soil above a fertility this
    /// port's grid all but never reaches. Shares are pinned as bands, not as the chosen cut points.
    /// </summary>
    public class TerrainPatchMakerTests : ContentTestBase
    {
        public TerrainPatchMakerTests(CoreContentFixture content) : base(content)
        {
        }

        private static TerrainDef Terrain(string name) => DefDatabase<TerrainDef>.GetNamed(name);

        private static Tile MakeTile(string biome, float swampiness = 0f) => new Tile
        {
            biome = DefDatabase<BiomeDef>.GetNamed(biome),
            hilliness = Hilliness.Flat,
            elevation = 100f,
            rainfall = 1200f,
            swampiness = swampiness,
        };

        private static CoreMap Generate(Tile tile, string seed) => MapGenerator.GenerateMap(tile, 1, seed, new IntVec2(200, 200));

        private static float Share(CoreMap map, params string[] terrains)
        {
            int hits = map.AllCells.Count(c => terrains.Contains(map.terrainGrid.TerrainAt(c).defName));
            return (float)hits / map.cellIndices.NumGridCells;
        }

        [Fact]
        public void A_patch_lays_its_band_only_inside_its_fertility_window()
        {
            var maker = new TerrainPatchMaker
            {
                minFertility = 0f,
                maxFertility = 0.5f,
                thresholds = { new TerrainThreshold { terrain = Terrain("SoilRich"), min = -999f, max = 999f } },
            };
            Perlin noise = maker.MakeNoise(7);
            var c = new IntVec3(10, 0, 10);

            Assert.Same(Terrain("SoilRich"), maker.TerrainAt(c, noise, 0.25f));
            Assert.Null(maker.TerrainAt(c, noise, -0.1f));
            Assert.Null(maker.TerrainAt(c, noise, 0.6f));
        }

        [Theory]
        [InlineData("forest-a")]
        [InlineData("forest-b")]
        [InlineData("forest-c")]
        public void A_temperate_forest_map_has_rich_soil_patches(string seed)
        {
            float rich = Share(Generate(MakeTile("TemperateForest"), seed), "SoilRich");
            Assert.InRange(rich, 0.02f, 0.12f);
        }

        [Fact]
        public void A_temperate_forest_map_has_a_few_wet_hollows_and_is_mostly_soil()
        {
            CoreMap map = Generate(MakeTile("TemperateForest"), "forest-a");
            Assert.InRange(Share(map, "MarshyTerrain", "Mud"), 0.002f, 0.06f);
            Assert.True(Share(map, "Soil") > 0.8f);
        }

        [Fact]
        public void A_swamp_map_is_marsh_and_water_between_soil()
        {
            CoreMap map = Generate(MakeTile("TemperateSwamp"), "swamp-a");
            Assert.InRange(Share(map, "Marsh", "WaterShallow"), 0.1f, 0.45f);
            Assert.True(Share(map, "MarshyTerrain") > 0.05f);
            Assert.True(Share(map, "Soil", "SoilRich") > 0.3f, "a swamp still has dry ground to build and farm on");
        }

        /// <summary>RimWorld's map generator does not read tile swampiness; the swamp biomes it selects lay the
        /// marsh. The port's old stand-in painted a whole forest tile marsh above swampiness 0.3.</summary>
        [Fact]
        public void A_wet_forest_tile_is_still_forest_ground()
        {
            CoreMap map = Generate(MakeTile("TemperateForest", swampiness: 0.6f), "forest-a");
            Assert.Equal(0f, Share(map, "Marsh"));
            Assert.True(Share(map, "Soil") > 0.8f);
        }

        /// <summary><see cref="GenStep_Terrain.BaseTerrainsFor"/> (and so <see cref="FoundingGround.CanFarm"/>)
        /// names every ground a generated map of the tile actually shows, patches included.</summary>
        [Fact]
        public void Every_ground_a_map_shows_is_one_its_tile_says_it_can_be_made_of()
        {
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading.Where(b => b.canBuildBase))
            {
                Tile tile = MakeTile(biome.defName);
                var said = new HashSet<TerrainDef>(GenStep_Terrain.BaseTerrainsFor(tile));
                foreach (string seed in new[] { "base-a", "base-b" })
                {
                    CoreMap map = MapGenerator.GenerateMap(tile, 1, seed, new IntVec2(120, 120));
                    foreach (IntVec3 c in map.AllCells)
                    {
                        TerrainDef shown = map.terrainGrid.TerrainAt(c);
                        Assert.True(said.Contains(shown), biome.defName + " map shows " + shown.defName + " its tile does not name");
                    }
                }
            }
        }

        [Fact]
        public void A_swamp_tile_names_its_patch_ground_and_can_be_farmed()
        {
            string[] names = GenStep_Terrain.BaseTerrainsFor(MakeTile("TemperateSwamp")).Select(t => t.defName).ToArray();
            Assert.Contains("Marsh", names);
            Assert.Contains("WaterShallow", names);
            Assert.Contains("SoilRich", names);
            Assert.True(FoundingGround.CanFarm(MakeTile("TemperateSwamp")));
        }
    }
}
