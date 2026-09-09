using System;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// Ambient access to the civilization's current <see cref="Ideo"/> — <see cref="ThoughtWorker_UnderPrecept"/>
    /// and <see cref="PreceptWorkers.PreceptWorker_RoleHolder"/> both read <see cref="Current"/> the same way
    /// <see cref="God.ThoughtWorker_UnderEdict"/> reads <c>Find.God</c>.
    /// <para/>
    /// <b>Why this is not <c>Find.Ideo</c>.</b> The natural home for a civilization-scale belief system is
    /// beside <c>Find.God</c>/<c>Find.ResearchManager</c> on <c>Sim/Find.cs</c>, owned by
    /// <c>Sim/Game.cs</c> the way every other manager in this codebase is — but <c>Sim/**</c> is out of this
    /// pass's file ownership (another lane owns it concurrently this round). This class is the "self-gate
    /// somewhere you own in the meantime" the brief asks for: a thread-static holder, in this module's own
    /// folder, with the same per-thread shape every <c>Find</c> property already has. Wiring an
    /// <c>Ideo Ideo { get; set; }</c> slot into <c>Sim/Game.cs</c>/<c>Sim/Find.cs</c> — mirroring
    /// <c>Find.God</c> exactly — and pointing this class through it the way <c>Find</c>'s own properties check
    /// <c>CurrentGame</c> first is the follow-up this report asks for.
    /// <para/>
    /// <b>Unlike every <c>Find</c> property, this one is not auto-constructed.</b> <c>Find.God</c> lazily
    /// builds a fresh, inert <c>GodManager</c> the first time anything asks for it, so a test that never
    /// touches it sees no edicts active. <see cref="Current"/> instead defaults to <c>null</c> — "no
    /// civilization has an ideoligion yet" — because an auto-built <c>Ideo</c> would need a default
    /// <c>IdeoDef</c> this pass has no principled way to pick, and every worker here already treats a
    /// <c>null</c> Ideo as "nothing applies", so nothing is lost by leaving it unset until something actually
    /// assigns one.
    /// <para/>
    /// <b>Test isolation.</b> Nothing resets this between tests the way <c>Find.Reset()</c> resets every
    /// thread-static <c>Find</c> service (<c>ContentTestBase</c>'s own constructor call lives in
    /// <c>tests/…/Content/CoreContentFixture.cs</c>, out of this pass's file ownership) — so any test that
    /// assigns <see cref="Current"/> MUST clear it again (an <c>IDisposable.Dispose</c> override is the safe
    /// place) or risk leaking a non-null Ideo into an unrelated test reusing the same pooled xUnit thread. See
    /// <c>IdeologyTests</c> for the pattern.
    /// </summary>
    public static class IdeoManager
    {
        [ThreadStatic] private static Ideo? current;

        public static Ideo? Current
        {
            get => current;
            set => current = value;
        }

        /// <summary>Drops the thread's ambient Ideo. See this class's own "Test isolation" doc.</summary>
        public static void Reset() => current = null;
    }
}
