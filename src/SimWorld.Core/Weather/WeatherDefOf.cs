using SimWorld.Defs;
using SimWorld.Thoughts;

namespace SimWorld.Weather
{
    /// <summary>
    /// The weathers the code itself names (RimWorld: <c>Verse.WeatherDefOf</c>). Its own <c>[DefOf]</c> class
    /// in its own file rather than an addition to a shared one — <see cref="DefOfHelper"/> binds by scanning
    /// every <c>[DefOf]</c> type, so a new class cannot collide with another lane mid-edit (CLAUDE.md).
    /// </summary>
    [DefOf]
    public static class WeatherDefOf
    {
        /// <summary>What a map starts in and falls back to: dry, still, no penalty to anything
        /// (RimWorld: <c>WeatherDefOf.Clear</c>).</summary>
        public static WeatherDef Clear = null!;

        /// <summary>Plain rain — the weather that actually puts fires out.</summary>
        public static WeatherDef Rain = null!;

        /// <summary>Rain cold enough to fall as snow; the <c>temperatureRange</c> gate is what picks between
        /// the two, exactly as RimWorld's does.</summary>
        public static WeatherDef SnowGentle = null!;
    }

    /// <summary>The mood memory a pawn caught out in the rain carries (RimWorld: <c>ThoughtDefOf.SoakingWet</c>,
    /// reached there through <c>WeatherDef.exposedThought</c> — same here, so this binding exists for the
    /// content test and for tests that assert the thought by name, not to be read from code).</summary>
    [DefOf]
    public static class WeatherThoughtDefOf
    {
        public static ThoughtDef SoakingWet = null!;
    }
}
