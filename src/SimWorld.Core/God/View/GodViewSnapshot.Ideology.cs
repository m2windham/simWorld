using System.Collections.Generic;

using SimWorld.Sim;
using SimWorld.Social.Ideology;

namespace SimWorld.God.View
{
    /// <summary>
    /// A civilization's belief system, read-only — the seam <see cref="GodViewSnapshot"/>'s own doc describes
    /// for every other piece of civilization state: values, never a live <see cref="Ideo"/> or
    /// <see cref="IdeoDef"/> reference a host could hold and drive the simulation through.
    ///
    /// <para/><b>Earned, not built ahead of the machinery.</b> This exists because
    /// <c>IdeologyInGameTests.A_founders_mood_rises_measurably_more_under_an_active_Ideo_than_an_identical_founding_with_none</c>
    /// (and its sibling <c>The_belonging_thought_is_actually_active_on_a_founder_after_real_ticking</c>) prove a
    /// civilization's ideology genuinely, observably changes it through nothing but the real tick loop —
    /// exactly the "prove it is alive" gate this lane's brief set before any read side. Role-holding and
    /// ritual performance did not clear that gate (nothing in <c>src/</c> triggers either on its own — see
    /// that suite's own honest-boundary test), so neither is named here: reporting a role nobody will ever
    /// hold, or a ritual that will never fire on its own, would be a view onto nothing wearing a UI.
    /// </summary>
    public sealed partial class GodViewSnapshot
    {
        /// <summary>
        /// The civilization's current belief system, or null when it has none. Reads <see cref="Find.Ideo"/>
        /// directly rather than folding into <see cref="Capture()"/>'s constructor — <see cref="GodViewSnapshot"/>
        /// itself is out of this lane's reach (<c>GodViewSnapshot.cs</c> is not to be edited), and every other
        /// query on this seam that was added after the fact (<see cref="CitizensOf"/>, <see cref="Citizen"/>)
        /// is already its own static call for exactly that reason — a host asks for it when it wants it, the
        /// same way it asks for a citizen.
        /// </summary>
        public static IdeologySummary? CurrentIdeology()
        {
            Ideo? ideo = Find.Ideo;
            if (ideo == null) return null;

            IReadOnlyList<MemeDef> ideoMemes = ideo.def.memes;
            var memes = new List<string>(ideoMemes.Count);
            for (int i = 0; i < ideoMemes.Count; i++) memes.Add(ideoMemes[i].LabelCap);

            IReadOnlyList<PreceptDef> ideoPrecepts = ideo.Precepts;
            var precepts = new List<string>(ideoPrecepts.Count);
            for (int i = 0; i < ideoPrecepts.Count; i++) precepts.Add(ideoPrecepts[i].LabelCap);

            return new IdeologySummary(ideo.name, memes, precepts);
        }
    }

    /// <summary>One civilization's belief system, as much as a god-scale view needs of it: a name and what it
    /// holds. See <see cref="GodViewSnapshot.CurrentIdeology"/> for why nothing about roles or rituals is here.</summary>
    public sealed class IdeologySummary
    {
        internal IdeologySummary(string name, IReadOnlyList<string> memes, IReadOnlyList<string> precepts)
        {
            Name = name;
            Memes = memes;
            Precepts = precepts;
        }

        public string Name { get; }

        /// <summary>Every meme's label, in the ideoligion's own authored order.</summary>
        public IReadOnlyList<string> Memes { get; }

        /// <summary>Every precept this ideoligion holds, auto and slot-filled alike (<see cref="Ideo.Precepts"/>).</summary>
        public IReadOnlyList<string> Precepts { get; }
    }
}
