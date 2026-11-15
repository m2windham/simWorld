using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// Rain against fire, RimWorld's way: every unroofed fire is rained on every interval and loses size,
    /// so a new spark dies at once and a big fire dies slowly.
    ///
    /// <para/>Found tracing a burning settlement (<c>docs/perf/storyteller-pressure</c>): with rain a 4%
    /// chance per interval of putting a fire out whole, fires went from 268 to 341 while it rained.
    /// </summary>
    public class FireRainTests : ContentTestBase
    {
        public FireRainTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(12, 12, TerrainDefOf.Soil);

        private static Fire FireOnAWall(CoreMap map, IntVec3 cell, float size)
        {
            Thing wall = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall"), DefDatabase<ThingDef>.GetNamed("WoodLog"));
            GenSpawn.Spawn(wall, cell, map);
            Assert.True(FireUtility.TryStartFireIn(cell, map, size));
            return FireUtility.AllFires(map).Cast<Fire>().Single(f => f.Position == cell);
        }

        [Fact]
        public void A_new_spark_is_out_at_its_first_interval_of_rain()
        {
            CoreMap map = NewMap();
            Fire spark = FireOnAWall(map, new IntVec3(5, 0, 5), Fire.MinFireSize);

            Assert.Equal(1, FireUtility.ExtinguishFiresFromRain(map, 1f));
            Assert.True(spark.Destroyed);
        }

        [Fact]
        public void A_big_fire_shrinks_every_interval_and_takes_many_to_go_out()
        {
            CoreMap map = NewMap();
            Fire fire = FireOnAWall(map, new IntVec3(5, 0, 5), Fire.MaxFireSize);

            Assert.Equal(0, FireUtility.ExtinguishFiresFromRain(map, 1f));
            Assert.False(fire.Destroyed);
            Assert.True(fire.fireSize < Fire.MaxFireSize, "every interval of rain takes something off");

            int intervals = 1;
            while (!fire.Destroyed && intervals < 100)
            {
                float before = fire.fireSize;
                FireUtility.ExtinguishFiresFromRain(map, 1f);
                intervals++;
                Assert.True(fire.Destroyed || fire.fireSize < before);
            }

            Assert.True(fire.Destroyed);
            Assert.InRange(intervals, 10, 30);
        }

        [Fact]
        public void A_fire_in_the_rain_stops_being_big_enough_to_spread_before_it_goes_out()
        {
            CoreMap map = NewMap();
            Fire fire = FireOnAWall(map, new IntVec3(5, 0, 5), Fire.MaxFireSize);

            int intervalsAbleToSpread = 0;
            int intervals = 0;
            while (!fire.Destroyed && intervals < 100)
            {
                if (fire.fireSize >= Fire.MinFireSizeToSpread) intervalsAbleToSpread++;
                FireUtility.ExtinguishFiresFromRain(map, 1f);
                intervals++;
            }

            Assert.True(intervalsAbleToSpread < intervals);
        }

        [Fact]
        public void A_roof_or_a_mere_drizzle_leaves_a_fire_alone()
        {
            CoreMap map = NewMap();
            var sheltered = new IntVec3(8, 0, 8);
            Fire roofed = FireOnAWall(map, sheltered, 1f);
            map.roofGrid.SetRoof(sheltered, RoofDefOf.RoofConstructed);
            Fire open = FireOnAWall(map, new IntVec3(3, 0, 3), 1f);

            FireUtility.ExtinguishFiresFromRain(map, Fire.MinRainRateToExtinguish / 2f);
            Assert.Equal(1f, open.fireSize, 4);

            FireUtility.ExtinguishFiresFromRain(map, 1f);
            Assert.Equal(1f, roofed.fireSize, 4);
            Assert.True(open.fireSize < 1f);
        }
    }
}
