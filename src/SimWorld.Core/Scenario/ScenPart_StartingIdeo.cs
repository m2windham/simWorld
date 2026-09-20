using System.Collections.Generic;

using SimWorld.Sim;
using SimWorld.Social.Ideology;

namespace SimWorld.Scenario
{
    /// <summary>
    /// Assigns the civilization's starting belief system (RimWorld: the ideoligion-classic-mode random
    /// assignment a new game makes when the player does not build one by hand — this port has no interactive
    /// ideoligion-creation screen, so a scenario simply names the candidates and one is drawn).
    ///
    /// <para/><b>Why this file exists at all.</b> <c>Ideo</c>, <c>IdeoDef</c>, every <c>PreceptDef</c> and
    /// <c>MemeDef</c> in this codebase were ported, content-authored and unit-tested (see
    /// <c>Social/Ideology/**</c> and <c>IdeologyTests</c>) — and <c>new Ideo(</c> appeared nowhere in
    /// <c>src/</c> until this class. <see cref="Social.Ideology.IdeoManager"/> and <see cref="Sim.Game.Ideo"/>
    /// have carried a setter facade since the module that needed <c>Find.Ideo</c> to exist was built beside
    /// them; nothing ever called it. This is the seam <c>docs/design/player-first.md</c> §10's own audit
    /// names: "a step further back" than a mechanism with no caller — a mechanism with nothing to point at.
    /// A <see cref="ScenPart"/> is where this port already starts every other piece of civilization state
    /// (the player's faction, the starting era, the starting research) — see <see cref="Sim.Game.NewGame"/>'s
    /// own call to <see cref="Scenario.PostGameStart"/> — so it is the natural, and only, place to start this
    /// one too.
    ///
    /// <para/><b>Reaches <see cref="Sim.Find.Ideo"/> directly rather than through <see cref="IScenarioContext"/>.</b>
    /// Every other <c>ScenPart_Starting*</c> in this file's sibling (<c>ScenParts.cs</c>) writes through the
    /// context object precisely because that context is <c>Scenario/</c>'s own shared seam, built for
    /// systems this pass does not own (research, letters, starting pawns). Ideo is a system this pass *does*
    /// own, and it already has an ambient, thread-static home — <see cref="Sim.Find.Ideo"/>, which resolves
    /// through whichever <see cref="Sim.Game"/> is current. <see cref="Sim.Game.NewGame"/> sets
    /// <see cref="Sim.Find.CurrentGame"/> to the new game well before it calls
    /// <see cref="Scenario.PostGameStart"/> (see that method's own body), so <c>Find.Ideo = ...</c> here
    /// reaches exactly the right game — the same indirection <see cref="ScenPart_StartingEra"/> relies on
    /// implicitly by writing through <c>ctx.ResearchManager</c>, which is itself just <c>Find.ResearchManager</c>
    /// under a different name. Adding an <c>Ideo</c> slot to <see cref="IScenarioContext"/> would mean editing
    /// a file this lane does not need to touch, for a service that already has its own ambient home.
    /// </summary>
    public sealed class ScenPart_StartingIdeo : ScenPart
    {
        /// <summary>Every ideoligion this scenario is willing to start the civilization with. One is drawn
        /// through the seeded <see cref="Rand"/> stream at <see cref="PostGameStart"/> time — see that
        /// method's own doc for why every draw goes through it, even when there is only one candidate.</summary>
        public List<IdeoDef> options = new List<IdeoDef>();

        public override string Summary(Scenario scen)
        {
            if (options.Count == 0) return "";
            if (options.Count == 1) return "Your civilization follows " + options[0].LabelCap + ".";
            return "Your civilization's belief is drawn from " + options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ideoligions.";
        }

        /// <summary>
        /// Draws one <see cref="IdeoDef"/> from <see cref="options"/> and assigns it to <see cref="Find.Ideo"/>.
        ///
        /// <para/><b>Always draws, even with one candidate.</b> A scenario with a single named ideoligion could
        /// skip the roll and assign it directly, but that would make the RNG stream's position depend on how
        /// many candidates a scenario happens to name — the same "a tuned range that collapses still draws"
        /// discipline <see cref="RandomStream.Range(IntRange)"/> already documents for a degenerate range.
        /// Determinism here means "the same seed always draws the same ideoligion", not "the same seed always
        /// consumes the same number of draws only when the scenario is ambiguous" — the weaker property would
        /// still pass a same-seed/same-result test today and then silently break the moment a scenario's
        /// candidate list changed from one entry to two.
        ///
        /// <para/>A no-op when <see cref="options"/> is empty (<see cref="ConfigErrors"/> flags that as
        /// authored content, not something to guard against at runtime by inventing a default belief nobody
        /// chose — the same reasoning <see cref="Sim.Game.Ideo"/>'s own doc gives for staying null rather than
        /// auto-constructing).
        /// </summary>
        public override void PostGameStart(IScenarioContext ctx)
        {
            if (options.Count == 0) return;
            IdeoDef chosen = Rand.Element(options);
            Find.Ideo = new Ideo(chosen);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (options.Count == 0) yield return "options must name at least one IdeoDef.";
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null) yield return "options[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "] is null.";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            List<IdeoDef>? o = options;
            Scribe_Collections.Look(ref o, "options", LookMode.Def);
            options = o ?? new List<IdeoDef>();
        }
    }
}
