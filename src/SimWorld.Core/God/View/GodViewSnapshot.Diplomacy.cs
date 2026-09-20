using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The read half of the diplomacy seam — see <c>GodCommands.Diplomacy.cs</c> for the write half and
    /// <c>docs/design/player-first.md</c> §10 for why this was missing: <see cref="Factions.Faction.DeclareWar"/>
    /// and <see cref="Factions.Faction.SignTreaty"/> had zero callers, and a player asked to command a
    /// civilization's foreign policy blind — with no way to see who exists, how they feel about us, or what
    /// state each relation is in — could not make an informed choice even once a command existed.
    /// </summary>
    public sealed partial class GodViewSnapshot
    {
        private IReadOnlyList<FactionRelationView>? civilizations;

        /// <summary>
        /// Every other civilization's relation with our own: who they are, our goodwill with them, the
        /// diplomatic standing that goodwill implies, whether we are formally at war, and every treaty (active
        /// or lapsed) between us. Empty before a player civilization exists.
        ///
        /// <para/>Computed on first read and cached from then on — the same pattern
        /// <see cref="PendingLetters"/> uses and for the same reason: <see cref="GodViewSnapshot"/>'s
        /// constructor is private in <c>GodViewSnapshot.cs</c>, which this lane does not touch (another lane's
        /// own partial file lives beside this one — see CLAUDE.md, "add a file rather than edit a shared one"),
        /// so this cannot be threaded through it the way <see cref="Civilization"/> or <see cref="Losses"/> are.
        /// </summary>
        public IReadOnlyList<FactionRelationView> Civilizations =>
            civilizations ??= FactionRelationView.RelationsOf(Find.FactionManager.OfPlayer, Find.FactionManager.AllFactionsVisible);
    }
}
