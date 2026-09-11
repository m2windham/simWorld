using System.Collections.Generic;

using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Quests
{
    /// <summary>
    /// Which civilization a quest is transacting with, and where its goods live.
    ///
    /// <para/>Extracted from <see cref="QuestRewardSink"/> when tribute arrived, because value now moves in
    /// both directions and both directions must agree about whose ledger they touch. A reward that lands in
    /// the seat's stores and a tribute taken from somewhere else would be two different civilizations wearing
    /// one name.
    /// </summary>
    public static class QuestCivilization
    {
        /// <summary>
        /// The civilization a quest is offered to: the running <see cref="Game"/>'s target, else the one
        /// registered with the storyteller. That second route is not a test affordance — adaptation, threat
        /// points and refire spacing all already read "the civilization" off the storyteller's own target
        /// list, and a quest is the same question asked by a different system.
        /// </summary>
        public static CivilizationTarget? Current
        {
            get
            {
                Game? game = Find.CurrentGame;
                if (game != null) return game.CivilizationTarget;

                IReadOnlyList<IIncidentTarget> targets = Find.Storyteller.AllIncidentTargets;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is CivilizationTarget civ) return civ;
                }
                return null;
            }
        }

        /// <summary>
        /// Where a civilization's goods sit: <see cref="CivilizationTarget.Seat"/>, the oldest settlement with
        /// ties broken by tile. Null when the civilization holds no settlements at all.
        ///
        /// <para/>The seat rather than the settlement the player is watching, and rather than a
        /// population-weighted pick, for the two reasons <see cref="QuestRewardSink"/>'s own doc gives at
        /// length: a civilization that only changes where the camera points is a stage set, and a
        /// population-weighted pick would need a roll — which, inside
        /// <see cref="QuestManager.QuestManagerTick"/>, would shift every subsequent roll in the game by the
        /// accident of when a delay happened to elapse.
        /// </summary>
        public static Settlement? TreasuryOf(CivilizationTarget? civilization) => civilization?.Seat;
    }
}
