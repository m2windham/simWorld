using System;
using System.Globalization;

namespace SimWorld.Sim
{
    /// <summary>Tick unit constants and conversions (RimWorld: <c>Verse.GenTicks</c>).</summary>
    public static class GenTicks
    {
        public const int TicksPerRealSecond = 60;
        public const int TickRareInterval = 250;
        public const int TickLongInterval = 2000;

        public static int SecondsToTicks(float seconds) => (int)Math.Round(TicksPerRealSecond * seconds);

        public static float TicksToSeconds(int ticks) => (float)ticks / TicksPerRealSecond;

        /// <summary>"12.5s" style readout.</summary>
        public static string ToStringSecondsFromTicks(int ticks) =>
            TicksToSeconds(ticks).ToString("0.#", CultureInfo.InvariantCulture) + "s";
    }
}
