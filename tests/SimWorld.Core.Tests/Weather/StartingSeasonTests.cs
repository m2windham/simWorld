using System.Linq;

using SimWorld.Map;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Weather;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Weather
{
    /// <summary>
    /// A new game starts at the opening of its tile's warm season, in either hemisphere
    /// (<see cref="TwelfthUtility"/>; RimWorld: <c>TwelfthUtility.FindStartingWarmTwelfth</c>, recalled).
    ///
    /// <para/><b>The defect these keep closed.</b> Every game started at absolute tick 0, the first of Aprimay —
    /// spring in the north, fall in the south. On the threat-free bench a southern band (band 20, seed 102)
    /// sowed its field, saw 15 °C on day 1 and below freezing from day 7, its rice stalled at 14% grown, and
    /// all 21 starved: the crop it was provisioned to wait for never came.
    /// </summary>
    [Collection("GlobalDefs")]
    public class StartingSeasonTests : ContentTestBase
    {
        public StartingSeasonTests(CoreContentFixture content) : base(content)
        {
        }

        private static MapClimate Temperate(float latitude) => new MapClimate
        {
            averageTemperature = 10f,
            rainfall = 1000f,
            latitude = latitude,
            longitude = 0f,
        };

        /// <summary>Seasonal mean over the <paramref name="days"/> after <paramref name="startAbs"/>.</summary>
        private static float SeasonalMeanOver(MapClimate climate, long startAbs, int days)
        {
            float sum = 0f;
            int samples = days * 4;
            for (int i = 0; i < samples; i++)
            {
                long tick = startAbs + (long)i * GenDate.TicksPerDay / 4;
                sum += climate.averageTemperature + GenTemperature.OffsetFromSeasonCycle(tick, climate.latitude, climate.longitude);
            }
            return sum / samples;
        }

        [Theory]
        [InlineData(40f)]
        [InlineData(-40f)]
        public void A_temperate_tile_in_either_hemisphere_starts_with_its_warm_half_of_the_year_ahead(float latitude)
        {
            MapClimate climate = Temperate(latitude);
            int start = TwelfthUtility.StartingTicksAbs(climate);

            Assert.True(SeasonalMeanOver(climate, start, GenDate.DaysPerYear / 2) > climate.averageTemperature,
                "The thirty days after the start should be the warm half of the year.");
            // And it is the opening of that warm run, not its middle: the twelfth before it is colder.
            float before = TwelfthUtility.AverageTemperatureForTwelfth(climate,
                (start / GenDate.TicksPerTwelfth + GenDate.TwelfthsPerYear - 1) % GenDate.TwelfthsPerYear);
            float first = TwelfthUtility.AverageTemperatureForTwelfth(climate, start / GenDate.TicksPerTwelfth);
            Assert.True(before < first);
        }

        [Fact]
        public void A_tile_never_warm_enough_starts_at_the_first_twelfth_of_its_summer()
        {
            var cold = new MapClimate { averageTemperature = -20f, latitude = -50f };
            Assert.Equal(-1, TwelfthUtility.EarliestTwelfthInAverageTemperatureRange(
                cold, TwelfthUtility.StartingWarmTwelfthMinTemperature, 9999f));
            Assert.Equal(TwelfthUtility.FirstTwelfthOfSummer(-50f) * GenDate.TicksPerTwelfth, TwelfthUtility.StartingTicksAbs(cold));
            Assert.Equal(Season.Summer, GenDate.SeasonAt(TwelfthUtility.StartingTicksAbs(cold), 0f, -50f));
        }

        /// <summary>
        /// The wiring: <c>Game.NewGame</c> on a southern tile starts its calendar in that tile's warm season,
        /// not on Aprimay 1 (its fall).
        /// </summary>
        [Fact]
        public void A_new_game_on_a_southern_tile_does_not_start_with_its_winter_ahead()
        {
            const string seed = "starting-season";
            Game probe = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            global::SimWorld.World.World world = probe.World!;
            int southern = Enumerable.Range(0, world.grid.TilesCount)
                .Where(i => !world.grid.Tiles[i].WaterCovered && world.grid.Tiles[i].biome != null)
                .Where(i => world.grid.LongLatOf(i).Item1 < -30)
                .First();
            MapClimate climate = MapClimate.ForTile(world, southern)!;
            // The case this test exists for: on the old calendar this tile's first thirty days are its cold half.
            Assert.True(SeasonalMeanOver(climate, 0, GenDate.DaysPerYear / 2) < climate.averageTemperature);

            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true,
                startTile: southern, bandSize: 20);

            int startAbs = game.TickManager.GameStartAbsTick;
            Assert.Equal(TwelfthUtility.StartingTicksAbs(climate), startAbs);
            Assert.True(SeasonalMeanOver(climate, startAbs, GenDate.DaysPerYear / 2) > climate.averageTemperature,
                "A southern founding still starts into its winter.");
        }

        /// <summary>A moved calendar has to reach the thermometer: the outdoor temperature reads the absolute
        /// date (RimWorld: <c>GenTemperature</c> reads <c>TicksAbs</c>), not ticks since the game began.</summary>
        [Fact]
        public void The_outdoor_temperature_follows_the_calendar_not_the_game_clock()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            MapClimate climate = Temperate(45f);
            map.weatherManager.Climate = climate;
            int halfYear = GenDate.TicksPerYear / 2;
            Find.TickManager.SetGameStartAbsTick(halfYear);

            Find.TickManager.DoSingleTick();
            map.MapTick();

            float byCalendar = GenTemperature.OutdoorTemperatureAt(climate, Find.TickManager.TicksAbs, map.weatherManager.TemperatureOffset);
            float byGameClock = GenTemperature.OutdoorTemperatureAt(climate, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset);
            Assert.True(System.Math.Abs(byCalendar - byGameClock) > 5f, "half a year apart should be a different season");
            Assert.Equal(byCalendar, map.outdoorTemperature, 3);
        }

        [Fact]
        public void The_start_tick_cannot_move_once_the_clock_has()
        {
            Find.TickManager.DoSingleTick();
            Assert.Throws<System.InvalidOperationException>(() => Find.TickManager.SetGameStartAbsTick(GenDate.TicksPerTwelfth));
        }
    }
}
