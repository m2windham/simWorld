using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Weather;

using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// The founding band is set down where its first crop can ripen before the cold
    /// (<c>Game.PickStartingTile</c>, <see cref="Game.GrowsLongEnoughToFound"/>). A translation: RimWorld's
    /// player reads the growing period before settling; this port picks the tile itself. On the threat-free
    /// bench the populated world of seed 102 put a band of twenty on a tundra, where its rice stalled half grown
    /// and all of them starved.
    /// </summary>
    [Collection("GlobalDefs")]
    public class FoundingGrowingSeasonTests : ContentTestBase
    {
        public FoundingGrowingSeasonTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_tundra_has_too_short_a_summer_to_found_on_and_a_temperate_tile_does_not()
        {
            var tundra = new MapClimate { averageTemperature = -8f, latitude = -63f };
            var temperate = new MapClimate { averageTemperature = 10f, latitude = 40f };
            float needed = Game.FoundingGrowingDaysNeeded();

            Assert.True(needed > 0f);
            Assert.True(TwelfthUtility.GrowingTwelfths(tundra) * GenDate.DaysPerTwelfth < needed);
            Assert.True(TwelfthUtility.GrowingTwelfths(temperate) * GenDate.DaysPerTwelfth >= needed);
            Assert.True(TwelfthUtility.GrowingTwelfths(tundra) < TwelfthUtility.GrowingTwelfths(temperate));
        }

        [Fact]
        public void The_populated_world_of_seed_102_founds_its_band_where_a_crop_can_ripen()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "102", subdivisionOverride: 3, soloStart: false, bandSize: 20);
            int tile = game.CivilizationTarget.Tile;

            Assert.True(Game.GrowsLongEnoughToFound(game.World!, tile, Game.FoundingGrowingDaysNeeded()),
                "the band was founded where its first crop cannot ripen before winter");
        }
    }
}
