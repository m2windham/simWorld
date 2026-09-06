using System;
using System.Globalization;

namespace SimWorld.Sim
{
    /// <summary>The four 15-day quadrums of RimWorld's 60-day year (<c>RimWorld.Quadrum</c>).</summary>
    public enum Quadrum
    {
        Aprimay,
        Jugust,
        Septober,
        Decembary,
        Undefined,
    }

    /// <summary>Season as reported for a latitude (RimWorld: <c>RimWorld.Season</c>).</summary>
    public enum Season
    {
        Undefined,
        Spring,
        Summer,
        Fall,
        Winter,
        PermanentSummer,
        PermanentWinter,
    }

    /// <summary>
    /// Calendar math over absolute ticks (RimWorld: <c>RimWorld.GenDate</c>). A day is 60,000 ticks
    /// (24 h × 2,500), a year is 60 days in four quadrums. Local time shifts with longitude.
    /// </summary>
    public static class GenDate
    {
        public const int TicksPerDay = 60000;
        public const int HoursPerDay = 24;
        public const int DaysPerTwelfth = 5;
        public const int TwelfthsPerYear = 12;
        public const int DaysPerYear = 60;
        public const int DaysPerQuadrum = 15;
        public const int QuadrumsPerYear = 4;
        public const int TicksPerHour = TicksPerDay / HoursPerDay;
        public const int TicksPerTwelfth = TicksPerDay * DaysPerTwelfth;
        public const int TicksPerQuadrum = TicksPerDay * DaysPerQuadrum;
        public const int TicksPerYear = TicksPerDay * DaysPerYear;
        public const int DefaultStartingYear = 5500;

        /// <summary>Longitude shifts the local day: +180° is half a day ahead of the reference.</summary>
        public static long LocalTicksOffsetFromLongitude(float longitude) => (long)(longitude / 360f * TicksPerDay);

        public static long LocalTicks(long absTicks, float longitude) => absTicks + LocalTicksOffsetFromLongitude(longitude);

        public static int DaysPassedAt(long absTicks) => (int)GenMath.FloorDiv(absTicks, TicksPerDay);

        /// <summary>Ticks into the local day, 0 ≤ t &lt; 60000, for any tick including negative local times.</summary>
        public static int DayTick(long absTicks, float longitude) => (int)GenMath.PositiveMod(LocalTicks(absTicks, longitude), TicksPerDay);

        public static int HourOfDay(long absTicks, float longitude) => DayTick(absTicks, longitude) / TicksPerHour;

        public static float HourFloat(long absTicks, float longitude) => (float)DayTick(absTicks, longitude) / TicksPerHour;

        public static int DayOfYear(long absTicks, float longitude) =>
            (int)(GenMath.PositiveMod(LocalTicks(absTicks, longitude), TicksPerYear) / TicksPerDay);

        public static int DayOfQuadrum(long absTicks, float longitude) => DayOfYear(absTicks, longitude) % DaysPerQuadrum;

        public static Quadrum QuadrumAt(long absTicks, float longitude) => (Quadrum)(DayOfYear(absTicks, longitude) / DaysPerQuadrum);

        public static int Year(long absTicks, float longitude) =>
            DefaultStartingYear + (int)GenMath.FloorDiv(LocalTicks(absTicks, longitude), TicksPerYear);

        /// <summary>Fraction of the year elapsed, in [0, 1).</summary>
        public static float YearPercent(long absTicks, float longitude) =>
            (float)GenMath.PositiveMod(LocalTicks(absTicks, longitude), TicksPerYear) / TicksPerYear;

        public static Season SeasonAt(long absTicks, float longitude, float latitude) =>
            SeasonUtility.GetReportedSeason(YearPercent(absTicks, longitude), latitude);

        public static string QuadrumLabel(Quadrum quadrum)
        {
            switch (quadrum)
            {
                case Quadrum.Aprimay: return "Aprimay";
                case Quadrum.Jugust: return "Jugust";
                case Quadrum.Septober: return "Septober";
                case Quadrum.Decembary: return "Decembary";
                default: return "Undefined";
            }
        }

        /// <summary>"Day 3 of Aprimay, 5500" (RimWorld's date readout, 1-based day).</summary>
        public static string DateReadoutStringAt(long absTicks, float longitude)
        {
            int day = DayOfQuadrum(absTicks, longitude) + 1;
            return "Day " + day.ToString(CultureInfo.InvariantCulture) + " of " + QuadrumLabel(QuadrumAt(absTicks, longitude)) + ", " + Year(absTicks, longitude).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"13h" appended to the date readout.</summary>
        public static string DateFullStringAt(long absTicks, float longitude) =>
            DateReadoutStringAt(absTicks, longitude) + ", " + HourOfDay(absTicks, longitude).ToString(CultureInfo.InvariantCulture) + "h";
    }

    /// <summary>Season lookup by latitude band (RimWorld: <c>RimWorld.SeasonUtility</c>).</summary>
    public static class SeasonUtility
    {
        /// <summary>Below this |latitude| there are no seasons: permanent summer.</summary>
        public const float TropicalLatitude = 8f;

        /// <summary>Above this |latitude| there are no seasons: permanent winter.</summary>
        public const float PolarLatitude = 68f;

        public static Season GetReportedSeason(float yearPct, float latitude)
        {
            float abs = Math.Abs(latitude);
            if (abs < TropicalLatitude) return Season.PermanentSummer;
            if (abs > PolarLatitude) return Season.PermanentWinter;
            return GetSeason(yearPct, latitude);
        }

        /// <summary>Northern hemisphere: spring starts the year; southern hemisphere is offset half a year.</summary>
        public static Season GetSeason(float yearPct, float latitude)
        {
            if (yearPct < 0f || yearPct >= 1f)
            {
                yearPct -= (float)Math.Floor(yearPct);
            }
            if (latitude < 0f)
            {
                yearPct = (yearPct + 0.5f) % 1f;
            }
            if (yearPct < 0.25f) return Season.Spring;
            if (yearPct < 0.5f) return Season.Summer;
            if (yearPct < 0.75f) return Season.Fall;
            return Season.Winter;
        }

        public static string Label(Season season)
        {
            switch (season)
            {
                case Season.Spring: return "Spring";
                case Season.Summer: return "Summer";
                case Season.Fall: return "Fall";
                case Season.Winter: return "Winter";
                case Season.PermanentSummer: return "Permanent summer";
                case Season.PermanentWinter: return "Permanent winter";
                default: return "Undefined";
            }
        }
    }
}
