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
    /// Overhead mountain sits only over the mountain (RimWorld: natural rock roof is laid over the mountain
    /// mass, and caves carved out of it keep theirs). The playtest's inspector reported "Thick rock roof" over
    /// open grass: the roof step graded every non-rock cell by how much rock stood within two cells, so the
    /// grass at the foot of a cliff was roofed — measured on three 200x200 solo-start maps, 1,478 to 1,603
    /// open cells under thin rock roof and 9 to 19 under thick.
    /// </summary>
    public class MountainRoofTests : ContentTestBase
    {
        public MountainRoofTests(CoreContentFixture content) : base(content)
        {
        }

        private static GenStepDef StepDef(string name) => DefDatabase<GenStepDef>.GetNamed(name);

        [Theory]
        [InlineData(Hilliness.LargeHills, "roof-foot-a")]
        [InlineData(Hilliness.Mountainous, "roof-foot-b")]
        [InlineData(Hilliness.Impassable, "roof-foot-c")]
        public void Rock_roof_lies_only_over_rock_and_the_caves_carved_from_it(Hilliness hills, string seed)
        {
            var tile = new Tile { hilliness = hills, biome = DefDatabase<BiomeDef>.GetNamed("TemperateForest"), elevation = 100f, rainfall = 1200f };
            var map = new CoreMap(80, 80, SimWorld.Map.TerrainDefOf.Soil);
            var ctx = new MapGenContext(map, tile, 1, seed);
            new GenStep_ElevationFertility { def = StepDef("ElevationFertility") }.Generate(ctx);
            new GenStep_Terrain { def = StepDef("MapTerrain") }.Generate(ctx);
            new GenStep_RocksAndMountains { def = StepDef("RocksAndMountains") }.Generate(ctx);
            bool[] mountainBeforeCaves = (bool[])ctx.rock.Clone();
            new GenStep_Caves { def = StepDef("Caves") }.Generate(ctx);
            new GenStep_Roofs { def = StepDef("Roofs") }.Generate(ctx);

            int rockRoofed = 0, openAtFoot = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                int i = map.cellIndices.CellToIndex(c);
                RoofDef? roof = map.roofGrid.RoofAt(c);
                bool natural = roof == RoofDefOf.RoofRockThick || roof == RoofDefOf.RoofRockThin;
                if (natural) rockRoofed++;
                if (!mountainBeforeCaves[i])
                {
                    Assert.False(natural, c + " is open ground under " + roof?.defName);
                    if (Enumerable.Range(-2, 5).Any(dx => Enumerable.Range(-2, 5).Any(dz =>
                    {
                        var n = new IntVec3(c.x + dx, 0, c.z + dz);
                        return GenGrid.InBounds(n, map) && ctx.rock[map.cellIndices.CellToIndex(n)];
                    }))) openAtFoot++;
                }
            }
            Assert.True(rockRoofed > 0, "the hills should raise some mountain to roof");
            Assert.True(openAtFoot > 0, "the map should have open ground beside the rock for this to test");
        }
    }
}
