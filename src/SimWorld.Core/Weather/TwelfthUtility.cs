using System;
using SimWorld.Sim;

namespace SimWorld.Weather
{
    /// <summary>
    /// Which twelfth of the year a new game starts in (RimWorld: <c>RimWorld.TwelfthUtility.FindStartingWarmTwelfth</c>
    /// and <c>GenTemperature.EarliestTwelfthInAverageTemperatureRange</c>, read through
    /// <c>GenTicks.ConfiguredTicksAbsAtGameStart</c>).
    ///
    /// <para/><b>The defect this closes.</b> This port started every game at absolute tick 0 — the first day of
    /// Aprimay, which is spring in the north and the first day of <i>fall</i> in the south. A southern band
    /// sowed its field, watched it grow for a week, and then sat through a 30-day winter in which nothing grows
    /// (bench, band 20 seed 102: outdoor temperature 15 °C on day 1, below 0 °C from day 7 to past day 40,
    /// rice stalled at 14% grown, all 21 dead). RimWorld does not start a colony into its winter: it starts it
    /// at the first twelfth of the warm part of the tile's year, in either hemisphere.
    ///
    /// <para/><b>Recalled, not decompiled.</b> The shape below — the first twelfth whose seasonal average is
    /// at least <see cref="StartingWarmTwelfthMinTemperature"/>, walked back to the start of that warm run, and
    /// the first twelfth of summer for a tile that never gets that warm — is RimWorld's as remembered, as is
    /// the 16 °C threshold and the averaging over the seasonal term alone (no day/night swing). Pinned by
    /// behaviour (a southern tile no longer starts with its winter ahead of it), not by the literal.
    /// RimWorld's extra few hours past the twelfth boundary are not ported: a start on a whole twelfth leaves
    /// the hour of day exactly where tick 0 put it, so nothing that reads the clock's hour moves.
    /// </summary>
    public static class TwelfthUtility
    {
        /// <summary>A twelfth whose seasonal mean reaches this is "warm" for the starting-season pick
        /// (RimWorld: the <c>16f</c> in <c>FindStartingWarmTwelfth</c>; recalled).</summary>
        public const float StartingWarmTwelfthMinTemperature = 16f;

        /// <summary>Samples per twelfth when averaging its temperature (RimWorld averages a fixed sample of the
        /// twelfth the same way; the count is this port's).</summary>
        private const int SamplesPerTwelfth = 120;

        /// <summary>The tile's seasonal mean over <paramref name="twelfth"/> (0..11): its annual mean plus the
        /// season, with neither the day/night swing nor any weather (RimWorld:
        /// <c>GenTemperature.AverageTemperatureAtTileForTwelfth</c>).</summary>
        public static float AverageTemperatureForTwelfth(MapClimate climate, int twelfth)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            long start = (long)twelfth * GenDate.TicksPerTwelfth;
            float sum = 0f;
            for (int i = 0; i < SamplesPerTwelfth; i++)
            {
                long tick = start + (long)i * GenDate.TicksPerTwelfth / SamplesPerTwelfth;
                sum += climate.averageTemperature + GenTemperature.OffsetFromSeasonCycle(tick, climate.latitude, climate.longitude);
            }
            return sum / SamplesPerTwelfth;
        }

        /// <summary>The twelfth that opens the first run of twelfths whose mean lies in
        /// [<paramref name="min"/>, <paramref name="max"/>], wrapping round the year end; -1 when none does
        /// (RimWorld: <c>GenTemperature.EarliestTwelfthInAverageTemperatureRange</c>).</summary>
        public static int EarliestTwelfthInAverageTemperatureRange(MapClimate climate, float min, float max)
        {
            for (int i = 0; i < GenDate.TwelfthsPerYear; i++)
            {
                if (!InRange(climate, i, min, max)) continue;
                if (i != 0) return i;
                // The first twelfth of the year is warm: walk back across the year end to where the warm run
                // actually begins, so a game is not dropped into the middle of its summer.
                int t = 0;
                for (int j = 0; j < GenDate.TwelfthsPerYear; j++)
                {
                    int previous = (t + GenDate.TwelfthsPerYear - 1) % GenDate.TwelfthsPerYear;
                    if (!InRange(climate, previous, min, max)) return t;
                    t = previous;
                }
                return 0; // warm all year
            }
            return -1;
        }

        /// <summary>The twelfth a game on this climate starts in (RimWorld: <c>FindStartingWarmTwelfth</c>):
        /// the start of its warm run, or the first twelfth of summer for a tile that is never that warm.</summary>
        public static int FindStartingWarmTwelfth(MapClimate climate)
        {
            int twelfth = EarliestTwelfthInAverageTemperatureRange(climate, StartingWarmTwelfthMinTemperature, 9999f);
            return twelfth >= 0 ? twelfth : FirstTwelfthOfSummer(climate.latitude);
        }

        /// <summary>First twelfth of summer at <paramref name="latitude"/>, by <see cref="SeasonUtility.GetSeason"/>'s
        /// own quarters: the fourth twelfth in the north, half a year on in the south.</summary>
        public static int FirstTwelfthOfSummer(float latitude) => latitude >= 0f ? 3 : 9;

        /// <summary>Absolute tick a game founded on <paramref name="climate"/> starts at: the first tick of its
        /// starting twelfth (RimWorld: <c>GenTicks.ConfiguredTicksAbsAtGameStart</c>, less its hour offset).</summary>
        public static int StartingTicksAbs(MapClimate climate) => FindStartingWarmTwelfth(climate) * GenDate.TicksPerTwelfth;

        private static bool InRange(MapClimate climate, int twelfth, float min, float max)
        {
            float t = AverageTemperatureForTwelfth(climate, twelfth);
            return t >= min && t <= max;
        }
    }
}
