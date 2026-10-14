using SimWorld.Defs;

namespace SimWorld.Thoughts
{
    /// <summary>
    /// The memories of having seen the dead lying about (<see cref="Things.Corpse.GiveObservedThought"/>). Its
    /// own <c>[DefOf]</c> class rather than an addition to a shared one, per <c>CLAUDE.md</c>: <c>DefOfHelper</c>
    /// binds by scanning every <c>[DefOf]</c> type.
    /// </summary>
    [DefOf]
    public static class ObservationThoughtDefOf
    {
        /// <summary>A human body, still fresh, lying where anyone can see it (RimWorld: <c>ThoughtDefOf.ObservedLayingCorpse</c>).</summary>
        public static ThoughtDef ObservedLayingCorpse = null!;

        /// <summary>The same once it has started to rot (RimWorld: <c>ThoughtDefOf.ObservedLayingRottingCorpse</c>).</summary>
        public static ThoughtDef ObservedLayingRottingCorpse = null!;
    }
}
