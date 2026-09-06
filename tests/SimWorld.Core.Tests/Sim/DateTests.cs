using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Sim
{
    public class DateTests
    {
        [Fact]
        public void Calendar_constants_match_rimworld()
        {
            Assert.Equal(60000, GenDate.TicksPerDay);
            Assert.Equal(2500, GenDate.TicksPerHour);
            Assert.Equal(900000, GenDate.TicksPerQuadrum);
            Assert.Equal(3600000, GenDate.TicksPerYear);
            Assert.Equal(60, GenDate.DaysPerYear);
            Assert.Equal(60, GenTicks.TicksPerRealSecond);
        }

        [Fact]
        public void GenTicks_converts_seconds()
        {
            Assert.Equal(90, GenTicks.SecondsToTicks(1.5f));
            Assert.Equal(1.5f, GenTicks.TicksToSeconds(90));
            Assert.Equal("1.5s", GenTicks.ToStringSecondsFromTicks(90));
            Assert.Equal("2s", GenTicks.ToStringSecondsFromTicks(120));
        }

        [Fact]
        public void Tick_zero_is_day_one_of_Aprimay_5500()
        {
            Assert.Equal("Day 1 of Aprimay, 5500", GenDate.DateReadoutStringAt(0, 0f));
            Assert.Equal(0, GenDate.HourOfDay(0, 0f));
            Assert.Equal(0, GenDate.DaysPassedAt(0));
            Assert.Equal(Quadrum.Aprimay, GenDate.QuadrumAt(0, 0f));
        }

        [Fact]
        public void Dates_advance_through_quadrums_and_years()
        {
            long ticks = GenDate.TicksPerDay * 15L;
            Assert.Equal("Day 1 of Jugust, 5500", GenDate.DateReadoutStringAt(ticks, 0f));

            ticks = GenDate.TicksPerYear + GenDate.TicksPerDay * 44L + GenDate.TicksPerHour * 13L;
            Assert.Equal(5501, GenDate.Year(ticks, 0f));
            Assert.Equal(44, GenDate.DayOfYear(ticks, 0f));
            Assert.Equal(Quadrum.Septober, GenDate.QuadrumAt(ticks, 0f));
            Assert.Equal(14, GenDate.DayOfQuadrum(ticks, 0f));
            Assert.Equal(13, GenDate.HourOfDay(ticks, 0f));
            Assert.Equal("Day 15 of Septober, 5501, 13h", GenDate.DateFullStringAt(ticks, 0f));
            Assert.Equal(104, GenDate.DaysPassedAt(ticks));
        }

        [Fact]
        public void Longitude_shifts_local_time_and_never_goes_negative()
        {
            Assert.Equal(12, GenDate.HourOfDay(0, 180f));
            Assert.Equal(30000, GenDate.LocalTicksOffsetFromLongitude(180f));
            Assert.Equal(18, GenDate.HourOfDay(0, -90f));
            Assert.Equal(59, GenDate.DayOfYear(0, -90f));
            Assert.Equal(5499, GenDate.Year(0, -90f));
            Assert.Equal(Quadrum.Decembary, GenDate.QuadrumAt(0, -90f));
            Assert.InRange(GenDate.YearPercent(0, -90f), 0.99f, 1f);
        }

        [Theory]
        [InlineData(0.1f, 45f, Season.Spring)]
        [InlineData(0.3f, 45f, Season.Summer)]
        [InlineData(0.6f, 45f, Season.Fall)]
        [InlineData(0.9f, 45f, Season.Winter)]
        [InlineData(0.1f, -45f, Season.Fall)]
        [InlineData(0.6f, -45f, Season.Spring)]
        [InlineData(0.5f, 5f, Season.PermanentSummer)]
        [InlineData(0.5f, -70f, Season.PermanentWinter)]
        [InlineData(1.2f, 45f, Season.Spring)]
        public void Seasons_follow_latitude_and_year_fraction(float yearPct, float latitude, Season expected)
        {
            Assert.Equal(expected, SeasonUtility.GetReportedSeason(yearPct, latitude));
        }

        [Fact]
        public void Season_at_ticks_uses_local_year_fraction()
        {
            long midSummer = GenDate.TicksPerYear * 3L / 8L;
            Assert.Equal(Season.Summer, GenDate.SeasonAt(midSummer, 0f, 40f));
            Assert.Equal(Season.Winter, GenDate.SeasonAt(midSummer, 0f, -40f));
            Assert.Equal("Permanent winter", SeasonUtility.Label(Season.PermanentWinter));
        }

        [Fact]
        public void GenMath_modulo_and_floor_division()
        {
            Assert.Equal(4, GenMath.PositiveMod(-1, 5));
            Assert.Equal(4L, GenMath.PositiveMod(-1L, 5L));
            Assert.Equal(-1L, GenMath.FloorDiv(-1L, 5L));
            Assert.Equal(0L, GenMath.FloorDiv(4L, 5L));
            Assert.Equal(-2L, GenMath.FloorDiv(-6L, 5L));
        }
    }
}
