using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Sim
{
    public class SimDigestTests : ContentTestBase
    {
        public SimDigestTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void The_digest_reads_without_drawing_and_changes_when_the_map_does()
        {
            var map = new CoreMap(10, 10, TerrainDefOf.Soil);
            Thing rock = GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), new IntVec3(3, 0, 3), map);
            uint draws = Rand.Current.Iterations;

            string first = SimDigest.Line(map);
            Assert.Equal(first, SimDigest.Line(map));
            Assert.Equal(draws, Rand.Current.Iterations);

            rock.Destroy();
            Assert.NotEqual(first, SimDigest.Line(map));
        }
    }
}
