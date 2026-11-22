using SimWorld.Building;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Weather;
using Xunit;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The field is sized for the season its crop will grow in
    /// (<see cref="FarmingInitiative.SeasonGrowthFactor(MapClimate, long)"/>): a warm season leaves it as
    /// <see cref="FarmingTuning.FieldSizeMargin"/> always sized it, a cold one enlarges it, and a frozen one
    /// enlarges it by a bounded amount rather than without limit. Asserted as orderings and bands, not as the
    /// unsourced cap.
    /// </summary>
    public class FarmingSeasonTests : ContentTestBase
    {
        public FarmingSeasonTests(CoreContentFixture content) : base(content)
        {
        }

        // On the equator the seasonal swing is zero, so the twelfth's mean is the tile's annual mean.
        private static MapClimate Climate(float averageTemperature) =>
            new MapClimate { averageTemperature = averageTemperature, latitude = 0f, longitude = 0f };

        [Fact]
        public void A_map_with_no_climate_sizes_its_field_as_before() =>
            Assert.Equal(1f, FarmingInitiative.SeasonGrowthFactor(null, 0L));

        [Fact]
        public void A_warm_season_leaves_the_field_alone() =>
            Assert.Equal(1f, FarmingInitiative.SeasonGrowthFactor(Climate(25f), 0L));

        [Fact]
        public void A_cold_season_wants_a_bigger_field_and_not_an_unbounded_one()
        {
            float warm = FarmingInitiative.SeasonGrowthFactor(Climate(25f), 0L);
            float cool = FarmingInitiative.SeasonGrowthFactor(Climate(6f), 0L);
            float frozen = FarmingInitiative.SeasonGrowthFactor(Climate(-15f), 0L);

            Assert.True(cool < warm, "a cool season grows a crop slower, so the field divided by it is bigger");
            Assert.True(frozen <= cool);
            Assert.True(frozen > 0f, "a frozen season must not ask for an infinite field");
            Assert.Equal(1f / FarmingTuning.MaxColdFieldFactor, frozen, 3);
        }

        [Fact]
        public void Every_twelfth_of_a_seasonal_tile_reads_within_the_bounds_and_winter_reads_lower_than_summer()
        {
            float lowest = 1f, highest = 0f;
            var north = new MapClimate { averageTemperature = 10f, latitude = 50f, longitude = 0f };
            for (int twelfth = 0; twelfth < GenDate.TwelfthsPerYear; twelfth++)
            {
                long tick = (long)twelfth * GenDate.TicksPerTwelfth;
                float factor = FarmingInitiative.SeasonGrowthFactor(north, tick);
                Assert.InRange(factor, 1f / FarmingTuning.MaxColdFieldFactor - 0.001f, 1f);
                lowest = System.Math.Min(lowest, factor);
                highest = System.Math.Max(highest, factor);
            }
            Assert.True(lowest < highest, "a tile with seasons reads its winter lower than its summer");
        }
    }
}
