using SimWorld.Defs;
using SimWorld.Thoughts;

namespace SimWorld.Social
{
    /// <summary>
    /// The two memories a finished social fight leaves (RimWorld: <c>ThoughtDefOf.HadCatharticFight</c> and
    /// <c>ThoughtDefOf.HadAngeringFight</c>, handed out 50/50 by
    /// <c>MentalState_SocialFighting.PostEnd</c>). Its own <c>[DefOf]</c> class in its own file, per
    /// CLAUDE.md — <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type.
    /// </summary>
    [DefOf]
    public static class SocialFightThoughtDefOf
    {
        public static ThoughtDef HadCatharticFight = null!;

        public static ThoughtDef HadAngeringFight = null!;
    }
}
