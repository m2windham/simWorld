using SimWorld.Defs;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// The social memory being hurt by someone leaves. Its own <c>[DefOf]</c> class rather than an addition to
    /// a shared one, per <c>CLAUDE.md</c>.
    /// </summary>
    [DefOf]
    public static class HarmThoughtDefOf
    {
        /// <summary>RimWorld: <c>ThoughtDefOf.HarmedMe</c>, given by
        /// <see cref="Health.Pawn_HealthTracker.PostApplyDamage"/>.</summary>
        public static ThoughtDef HarmedMe = null!;
    }
}
