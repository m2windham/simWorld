using SimWorld.Building;
using SimWorld.Tests.Content;
using SimWorld.Weather;
using Xunit;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The field is sized for the tile's growing period
    /// (<see cref="FarmingInitiative.GrowingShareOfYear(MapClimate)"/>): a tile that grows all year keeps the
    /// field <see cref="FarmingTuning.FieldSizeMargin"/> always gave it, a tile with a winter grows a bigger one
    /// to eat through it, and a tile that barely grows gets a bounded one rather than an infinite one. Asserted
    /// as orderings and bands, not as the unsourced cap.
    /// </summary>
    public class FarmingSeasonTests : ContentTestBase
    {
        public FarmingSeasonTests(CoreContentFixture content) : base(content)
        {
        }

        // On the equator the seasonal swing is zero, so every twelfth's mean is the tile's annual mean.
        private static MapClimate Equatorial(float averageTemperature) =>
            new MapClimate { averageTemperature = averageTemperature, latitude = 0f, longitude = 0f };

        [Fact]
        public void A_map_with_no_climate_sizes_its_field_as_before() =>
            Assert.Equal(1f, FarmingInitiative.GrowingShareOfYear((MapClimate?)null));

        [Fact]
        public void A_tile_that_grows_all_year_keeps_its_field() =>
            Assert.Equal(1f, FarmingInitiative.GrowingShareOfYear(Equatorial(25f)));

        [Fact]
        public void A_tile_with_a_winter_wants_a_bigger_field_and_a_frozen_one_not_an_unbounded_one()
        {
            float allYear = FarmingInitiative.GrowingShareOfYear(Equatorial(25f));
            float seasonal = FarmingInitiative.GrowingShareOfYear(
                new MapClimate { averageTemperature = 8f, latitude = 50f, longitude = 0f });
            float frozen = FarmingInitiative.GrowingShareOfYear(Equatorial(-15f));

            Assert.True(seasonal < allYear, "a tile with a winter grows for part of the year, so its field is bigger");
            Assert.True(frozen <= seasonal);
            Assert.True(frozen > 0f, "a tile that never grows must not ask for an infinite field");
            Assert.Equal(1f / FarmingTuning.MaxColdFieldFactor, frozen, 3);
        }

        [Fact]
        public void The_share_follows_the_growing_period_a_player_reads_off_the_world_map()
        {
            var climate = new MapClimate { averageTemperature = 12f, latitude = 45f, longitude = 0f };
            int growing = TwelfthUtility.GrowingTwelfths(climate);
            Assert.InRange(growing, 4, 11); // a tile with a real winter and a real summer

            Assert.Equal((float)growing / 12f, FarmingInitiative.GrowingShareOfYear(climate), 3);
        }
    }
}
