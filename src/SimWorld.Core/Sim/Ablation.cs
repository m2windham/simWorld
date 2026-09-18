using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// Switches a named piece of the simulation off so a run can be compared against one with it on, and the
    /// difference attributed to it. The measurement half of "measurable, ablatable" — the other half being
    /// <see cref="NamedRand"/>, which is what keeps the two runs comparable in the first place.
    ///
    /// <para/><b>Ablate the effect, never the selection.</b> The tempting implementation is to drop the thing
    /// from whatever list chooses it. That is wrong, and quietly: the storyteller picks incidents with a
    /// weighted roll over its candidates, so removing one changes which index that roll lands on, every later
    /// draw shifts, and the run diverges for a reason that has nothing to do with the thing being measured.
    /// You would get a large, reproducible number about nothing. So an ablated incident is still selected,
    /// still fires, still spends its refire timer and still consumes its own stream — it simply has no
    /// effect. Everything except the thing under test happens identically.
    ///
    /// <para/><b>The disabled state is not hypothetical.</b> <c>ManhunterPack</c> spent this port's whole life
    /// pointing at a worker whose body was <c>=&gt; parms.points &gt; 0f</c> — fired, reported success, did
    /// nothing. That is exactly what an ablated incident looks like, which is a useful thing to notice twice:
    /// once because it means the disabled arm is a faithful reproduction of the old behaviour, and once
    /// because an accidental permanent ablation is indistinguishable from a working feature unless somebody
    /// is measuring.
    ///
    /// <para/><b>Off by default and never read from content.</b> Nothing ships ablated. This is a harness
    /// control set by a bench run or a test, so a shipped game cannot accidentally disable a system, and a
    /// def cannot ask to be exempted from the simulation.
    /// </summary>
    public static class Ablation
    {
        private static readonly HashSet<string> disabled = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether anything is switched off at all — cheap enough to call on a hot path so the
        /// ordinary case costs one bool.</summary>
        public static bool Any => disabled.Count > 0;

        /// <summary>The names currently switched off, for a report that wants to say what it measured.</summary>
        public static IReadOnlyCollection<string> Disabled => disabled;

        /// <summary>Whether <paramref name="name"/> — an incident's defName, or any other agreed label — is
        /// switched off for this run.</summary>
        public static bool IsDisabled(string? name) =>
            name != null && disabled.Count > 0 && disabled.Contains(name);

        /// <summary>Switches <paramref name="name"/> off. Idempotent.</summary>
        public static void Disable(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            disabled.Add(name);
        }

        /// <summary>Switches everything back on. A harness that ablates must clear afterwards, exactly as it
        /// would reset any other global — see <c>tools/bench</c>'s <c>Bootstrap.ResetSim</c> for what a run
        /// that forgets one of these costs.</summary>
        public static void Clear() => disabled.Clear();
    }
}
