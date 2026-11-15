using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Weather
{
    /// <summary>
    /// A big enough fire brings the rain (RimWorld: <c>FireWatcher</c>, read by <c>WeatherDecider</c>). The
    /// map-scale remedy for a wildfire that nobody can reach: the current weather is hurried along and the
    /// next one leans hard toward rain, which <c>Fire.TryExtinguishFromRain</c> then does the work of.
    /// <para/>
    /// Found on a threat-free founding band (<c>docs/perf/storyteller-pressure</c>): after a flashstorm one
    /// fire became a hundred, the sky stayed clear for two days and all twenty citizens died in it. The tests
    /// pin the behaviour — a small fire changes nothing, a large one shortens the weather and makes rain far
    /// likelier, a flashstorm's dry spell still wins — never the literals.
    /// </summary>
    public class FireWatcherTests : ContentTestBase
    {
        public FireWatcherTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap()
        {
            var map = new CoreMap(16, 16, TerrainDefOf.Soil) { outdoorTemperature = 20f };
            return map;
        }

        /// <summary>Sets <paramref name="count"/> wooden walls alight at full size, one per cell.</summary>
        private static void Burn(CoreMap map, int count)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            ThingDef wood = DefDatabase<ThingDef>.GetNamed("WoodLog");
            int lit = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (lit == count) break;
                GenSpawn.Spawn(ThingMaker.MakeThing(wall, wood), c, map);
                Assert.True(FireUtility.TryStartFireIn(c, map, Fire.MaxFireSize));
                lit++;
            }
            foreach (Fire f in FireUtility.AllFires(map).Cast<Fire>()) f.fireSize = Fire.MaxFireSize;
            map.weatherManager.FireWatcher.UpdateObservations();
        }

        [Fact]
        public void A_handful_of_fires_is_not_a_large_fire_and_a_burning_field_is()
        {
            CoreMap small = NewMap();
            Burn(small, 5);
            Assert.True(small.weatherManager.FireWatcher.FireDanger > 0f);
            Assert.False(small.weatherManager.FireWatcher.LargeFireDangerPresent);

            CoreMap big = NewMap();
            Burn(big, 80);
            Assert.True(big.weatherManager.FireWatcher.LargeFireDangerPresent);
        }

        [Fact]
        public void A_map_with_no_fire_has_no_danger()
        {
            CoreMap map = NewMap();
            Assert.False(map.weatherManager.FireWatcher.LargeFireDangerPresent);
            Assert.Equal(0f, map.weatherManager.FireWatcher.FireDanger);
        }

        /// <summary>The weather a map starts with is due to last <see cref="WeatherDecider.CurWeatherDuration"/>
        /// ticks. Under a large fire it has moved on by a third of that; on a quiet map it has not.</summary>
        [Fact]
        public void A_large_fire_hurries_the_weather_along()
        {
            int ticks = WeatherDecider.InitialWeatherDurationTicks / 3;

            CoreMap quiet = NewMap();
            for (int i = 0; i < ticks; i++) quiet.weatherManager.WeatherManagerTick();
            Assert.Equal(ticks, quiet.weatherManager.CurWeatherAge);

            CoreMap burning = NewMap();
            Burn(burning, 80);
            for (int i = 0; i < ticks; i++) burning.weatherManager.WeatherManagerTick();
            Assert.True(burning.weatherManager.CurWeatherAge < ticks, "the weather should have changed under a large fire");
        }

        /// <summary>In a desert, where most weather is dry, a large fire makes dry weather the rare draw. Desert
        /// rather than a bare map because a map with no climate already draws rain most of the time.</summary>
        [Fact]
        public void A_large_fire_makes_rain_far_likelier_next()
        {
            CoreMap quiet = NewMap();
            quiet.weatherManager.Climate = Desert();
            CoreMap burning = NewMap();
            burning.weatherManager.Climate = Desert();
            Burn(burning, 80);

            int quietWet = WetDraws(quiet, 2000);
            int burningWet = WetDraws(burning, 2000);
            int quietDry = 2000 - quietWet;
            int burningDry = 2000 - burningWet;
            Assert.True(burningDry * 3 < quietDry, $"dry draws: {burningDry} under a large fire, {quietDry} without");
            Assert.True(burningWet > 1500, $"rain should be by far the likeliest next weather under a large fire, was {burningWet}/2000");
        }

        private static MapClimate Desert() =>
            new MapClimate
            {
                biome = DefDatabase<global::SimWorld.World.BiomeDef>.GetNamed("Desert"),
                averageTemperature = 24f,
                rainfall = 150f,
                latitude = 30f,
                longitude = 0f,
            };

        /// <summary>RimWorld's flashstorm holds the rain off whatever the fire watcher says; the large fire
        /// only gets its rain once that hold is over.</summary>
        [Fact]
        public void A_flashstorm_dry_spell_still_holds_the_rain_off_a_large_fire()
        {
            CoreMap burning = NewMap();
            Burn(burning, 80);
            burning.weatherManager.Decider.DisableRainFor(30000);
            Assert.Equal(0, WetDraws(burning, 500));
        }

        private static int WetDraws(CoreMap map, int draws)
        {
            int wet = 0;
            for (int i = 0; i < draws; i++)
            {
                WeatherDef? next = map.weatherManager.Decider.ChooseNextWeather();
                if (next != null && next.rainRate > WeatherDecider.LargeFireMinRainRate) wet++;
            }
            return wet;
        }
    }
}
