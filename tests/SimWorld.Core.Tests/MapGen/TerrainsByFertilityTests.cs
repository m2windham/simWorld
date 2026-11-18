using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.MapGen
{
    /// <summary>
    /// A map's ground is its biome's <c>terrainsByFertility</c> bands read at each cell's fertility (RimWorld:
    /// <c>GenStep_Terrain.TerrainFrom</c> through <c>TerrainThreshold.TerrainAtValue</c>), not one soil ladder
    /// shared by every biome with an all-sand rule for dry ground. So a dry shrubland still has soil to farm,
    /// and a desert is sand and stony ground however much rain its tile happens to get.
    /// </summary>
    public class TerrainsByFertilityTests : ContentTestBase
    {
        public TerrainsByFertilityTests(CoreContentFixture content) : base(content)
        {
        }

        private static TerrainDef Terrain(string name) => DefDatabase<TerrainDef>.GetNamed(name);

        private static Tile MakeTile(string biome, float rainfall) => new Tile
        {
            biome = DefDatabase<BiomeDef>.GetNamed(biome),
            hilliness = Hilliness.Flat,
            elevation = 100f,
            rainfall = rainfall,
        };

        private static Dictionary<string, int> Ground(CoreMap map)
        {
            var counts = new Dictionary<string, int>();
            foreach (IntVec3 c in map.AllCells)
            {
                string name = map.terrainGrid.TerrainAt(c).defName;
                counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
            }
            return counts;
        }

        [Fact]
        public void TerrainAtValue_takes_the_first_band_holding_the_value_ends_inclusive()
        {
            var bands = new List<TerrainThreshold>
            {
                new TerrainThreshold { terrain = Terrain("Sand"), min = -999f, max = 0f },
                new TerrainThreshold { terrain = Terrain("Soil"), min = 0f, max = 0.5f },
            };

            Assert.Same(Terrain("Sand"), TerrainThreshold.TerrainAtValue(bands, -0.3f));
            Assert.Same(Terrain("Sand"), TerrainThreshold.TerrainAtValue(bands, 0f)); // shared edge: the earlier band
            Assert.Same(Terrain("Soil"), TerrainThreshold.TerrainAtValue(bands, 0.5f));
            Assert.Null(TerrainThreshold.TerrainAtValue(bands, 0.6f));
            Assert.Null(TerrainThreshold.TerrainAtValue(null, 0f));
        }

        [Fact]
        public void Every_biome_a_settlement_can_stand_on_says_what_its_ground_is_made_of()
        {
            foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading.Where(b => b.canBuildBase))
            {
                Assert.True(biome.terrainsByFertility is { Count: > 0 }, biome.defName + " has no terrainsByFertility");
                Assert.All(biome.terrainsByFertility!, band => Assert.NotNull(band.terrain));
            }
        }

        [Fact]
        public void A_dry_shrubland_map_still_has_soil_to_farm()
        {
            CoreMap map = MapGenerator.GenerateMap(MakeTile("AridShrubland", rainfall: 200f), 1, "shrubland-ground", new IntVec2(80, 80));
            Dictionary<string, int> ground = Ground(map);

            Assert.True(ground.GetValueOrDefault("Soil") > 0, "no soil on a dry shrubland map");
            Assert.True(ground.GetValueOrDefault("Sand") > 0, "no sand on a dry shrubland map");
        }

        [Fact]
        public void A_desert_map_is_sand_and_stony_ground_however_wet_its_tile()
        {
            CoreMap map = MapGenerator.GenerateMap(MakeTile("Desert", rainfall: 600f), 1, "desert-ground", new IntVec2(80, 80));
            Dictionary<string, int> ground = Ground(map);

            Assert.Equal(0, ground.GetValueOrDefault("Soil"));
            Assert.Equal(0, ground.GetValueOrDefault("SoilRich"));
            Assert.True(ground.GetValueOrDefault("Sand") > ground.GetValueOrDefault("Gravel"), "a desert is mostly sand");
        }

        [Fact]
        public void A_temperate_forest_map_is_soil_with_no_sand_or_gravel()
        {
            CoreMap map = MapGenerator.GenerateMap(MakeTile("TemperateForest", rainfall: 1200f), 1, "forest-ground", new IntVec2(80, 80));
            Dictionary<string, int> ground = Ground(map);

            Assert.True(ground.GetValueOrDefault("Soil") > 0);
            Assert.Equal(0, ground.GetValueOrDefault("Sand"));
            Assert.Equal(0, ground.GetValueOrDefault("Gravel"));
        }

        [Fact]
        public void The_ground_a_tile_can_be_made_of_is_its_biomes_bands()
        {
            Assert.Equal(new[] { "Sand", "Gravel", "Soil" },
                GenStep_Terrain.BaseTerrainsFor(MakeTile("AridShrubland", rainfall: 200f)).Select(t => t.defName));
            Assert.Equal(new[] { "Sand" },
                GenStep_Terrain.BaseTerrainsFor(MakeTile("ExtremeDesert", rainfall: 50f)).Select(t => t.defName));
        }
    }
}
