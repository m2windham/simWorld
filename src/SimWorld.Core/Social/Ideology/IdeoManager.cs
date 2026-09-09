using System;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// Ambient access to the civilization's current <see cref="Ideo"/> — <see cref="ThoughtWorker_UnderPrecept"/>
    /// and <see cref="PreceptWorkers.PreceptWorker_RoleHolder"/> both read <see cref="Current"/> the same way
    /// <see cref="God.ThoughtWorker_UnderEdict"/> reads <c>Find.God</c>.
    /// <para/>
    /// <b>This is now a facade over <c>Find.Ideo</c>.</b> It was built as a thread-static holder of its own
    /// because <c>Sim/**</c> was owned by another lane while this module was written; the slot it asked for
    /// exists now — <c>Sim/Game.Ideo</c>, surfaced as <c>Find.Ideo</c>, resolving through the current game
    /// exactly the way <c>Find.God</c> does — and this class delegates to it. Kept rather than deleted so the
    /// call sites here read at the same level of abstraction as the rest of the module, and because
    /// <see cref="Reset"/> says what it means where a bare <c>Find.Ideo = null</c> would not.
    /// <para/>
    /// <b>Unlike every <c>Find</c> property, this one is not auto-constructed.</b> <c>Find.God</c> lazily
    /// builds a fresh, inert <c>GodManager</c> the first time anything asks for it, so a test that never
    /// touches it sees no edicts active. <see cref="Current"/> instead defaults to <c>null</c> — "no
    /// civilization has an ideoligion yet" — because an auto-built <c>Ideo</c> would need a default
    /// <c>IdeoDef</c> this pass has no principled way to pick, and every worker here already treats a
    /// <c>null</c> Ideo as "nothing applies", so nothing is lost by leaving it unset until something actually
    /// assigns one.
    /// <para/>
    /// <b>Test isolation.</b> <c>Find.Reset()</c> now clears it along with every other thread-static service,
    /// so a test no longer has to remember to. <c>IdeologyTests</c> still clears it explicitly, which is
    /// harmless and keeps that suite honest about what it sets.
    /// </summary>
    public static class IdeoManager
    {
        public static Ideo? Current
        {
            get => Sim.Find.Ideo;
            set => Sim.Find.Ideo = value;
        }

        /// <summary>Drops the ambient Ideo. See this class's own "Test isolation" doc.</summary>
        public static void Reset() => Sim.Find.Ideo = null;
    }
}
