using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The write half of the diplomacy seam — see <c>GodViewSnapshot.Diplomacy.cs</c> for the read half.
    ///
    /// <para/>Before this, nothing in <c>src/</c> ever called <see cref="Faction.DeclareWar"/> or
    /// <see cref="Faction.SignTreaty"/> — not even AI (<c>docs/design/player-first.md</c> §10). Every command
    /// here goes through <see cref="Factions.DiplomacyActions"/> rather than <see cref="Faction"/> directly, the
    /// same single seam <see cref="Factions.DiplomacyAI"/> (the world's own half) uses, so a war started by the
    /// player and one started by a civilization's own judgement are bookkept identically.
    ///
    /// <para/><b>Refusals, per <c>docs/design/player-first.md</c> §5: only the impossible.</b> No such
    /// civilization is impossible. Already at war, or not at war when asked to make peace, is not impossible —
    /// it is already true, so it is <see cref="GodCommandOutcome.NoChange"/>, the same treatment
    /// <see cref="RescindEdict"/> gives an edict nobody issued. A treaty the relation's own rules forbid
    /// (a non-aggression pact blocking a declaration, a permanent enemy blocking peace or any treaty at all) is
    /// impossible, and is read straight off <see cref="Faction"/>'s own predicates
    /// (<see cref="Faction.WarWith"/>, <see cref="Faction.HasNonAggressionPactWith"/>) rather than re-derived —
    /// <see cref="Faction.DeclareWar"/>/<see cref="Faction.MakePeace"/>/<see cref="Faction.SignTreaty"/> check
    /// exactly these same conditions internally, so nothing here can drift from what they actually enforce.
    ///
    /// <para/><b>What is never refused: an unwise war.</b> Declaring on a civilization far stronger than our own
    /// is the player's call and exactly the sort of decision this game exists to let them make and regret — see
    /// <c>DiplomacyCommandsTests</c>' own suicidal-war cases. Nothing here reads relative strength at all.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Declares war on the civilization named by <paramref name="civilizationId"/> (a
        /// <see cref="FactionRelationView.CivilizationId"/> off the snapshot). Refuses only for an unknown
        /// civilization or a non-aggression pact currently forbidding it; a no-op (not a failure) if already at
        /// war. Never refused for being unwise, however outmatched the player's own civilization is.
        /// </summary>
        public static GodCommandResult DeclareWar(string civilizationId)
        {
            Faction? player = Find.FactionManager.OfPlayer;
            if (player == null) return GodCommandResult.Refused("No player civilization exists yet.");

            Faction? target = ResolveCivilization(civilizationId);
            if (target == null) return GodCommandResult.Refused("No civilization named '" + civilizationId + "'.");
            if (ReferenceEquals(target, player)) return GodCommandResult.Refused("A civilization cannot declare war on itself.");

            if (DiplomacyActions.DeclareWar(player, target, "Declared by the player"))
            {
                return GodCommandResult.Done("War declared on " + target.name + ".");
            }

            // Faction.DeclareWar's only two refusal conditions, read back rather than re-derived — whichever one
            // still holds after the call is the reason it refused.
            if (player.WarWith(target)) return GodCommandResult.NoChange("Already at war with " + target.name + ".");
            return GodCommandResult.Refused(
                "A non-aggression pact with " + target.name + " forbids declaring war while it holds.");
        }

        /// <summary>
        /// Makes peace with the civilization named by <paramref name="civilizationId"/>. Refuses only for an
        /// unknown civilization or a permanent enemy (which can never leave war); a no-op if not currently at
        /// war with them.
        /// </summary>
        public static GodCommandResult MakePeace(string civilizationId)
        {
            Faction? player = Find.FactionManager.OfPlayer;
            if (player == null) return GodCommandResult.Refused("No player civilization exists yet.");

            Faction? target = ResolveCivilization(civilizationId);
            if (target == null) return GodCommandResult.Refused("No civilization named '" + civilizationId + "'.");
            if (ReferenceEquals(target, player)) return GodCommandResult.Refused("A civilization cannot make peace with itself.");

            if (DiplomacyActions.MakePeace(player, target, "Negotiated by the player"))
            {
                return GodCommandResult.Done("Peace made with " + target.name + ".");
            }

            // Faction.MakePeace's only two refusal conditions, read back the same way DeclareWar's are above.
            if (!player.WarWith(target)) return GodCommandResult.NoChange("Not at war with " + target.name + ".");
            return GodCommandResult.Refused(target.name + " is a permanent enemy — no peace is possible.");
        }

        /// <summary>
        /// Signs the treaty named by <paramref name="treatyDefName"/> (a <see cref="TreatyView.DefName"/> off
        /// the snapshot, or any loaded <see cref="TreatyDef"/>) with the civilization named by
        /// <paramref name="civilizationId"/>. Refuses only for an unknown civilization, an unknown treaty, or a
        /// permanent enemy — <see cref="Faction.SignTreaty"/>'s one and only refusal condition. Signing a
        /// non-aggression treaty while at war ends that war outright, exactly as <see cref="Faction.SignTreaty"/>
        /// itself already does; signing one already held simply adds another instance (renewing it), which is
        /// <see cref="Faction.SignTreaty"/>'s own behaviour and is never second-guessed here.
        /// </summary>
        public static GodCommandResult SignTreaty(string civilizationId, string treatyDefName)
        {
            Faction? player = Find.FactionManager.OfPlayer;
            if (player == null) return GodCommandResult.Refused("No player civilization exists yet.");

            Faction? target = ResolveCivilization(civilizationId);
            if (target == null) return GodCommandResult.Refused("No civilization named '" + civilizationId + "'.");

            TreatyDef? def = ResolveTreaty(treatyDefName);
            if (def == null) return GodCommandResult.Refused("No treaty named '" + treatyDefName + "'.");

            string? refusal = TreatyRefusal(player, target, def);
            if (refusal != null) return GodCommandResult.Refused(refusal);

            if (DiplomacyActions.SignTreaty(player, target, def, "Signed by the player"))
            {
                return GodCommandResult.Done(def.LabelCap + " signed with " + target.name + ".");
            }

            // Unreachable while TreatyRefusal mirrors Faction.SignTreaty; DiplomacyCommandsTests pins the two to
            // agree, so a new rule there that this does not know about fails a test rather than landing here.
            return GodCommandResult.Refused(target.name + " refused the treaty.");
        }

        /// <summary>
        /// Why <paramref name="player"/> cannot sign any treaty with <paramref name="target"/> right now, or null
        /// when it can — <see cref="Faction.SignTreaty"/>'s refusal conditions (the same faction on both sides, or
        /// a permanent enemy on either), in words. The one place both <see cref="SignTreaty"/> and
        /// <see cref="FactionRelationView.TreatyReason"/> read the answer from, so the Diplomacy screen can never
        /// offer a treaty the command then refuses, nor hide one it would sign. Treaty-independent because
        /// <see cref="Faction.SignTreaty"/> is: no rule there looks at which treaty it is.
        /// </summary>
        internal static string? TreatyRefusal(Faction player, Faction target)
        {
            if (ReferenceEquals(target, player)) return "A civilization cannot sign a treaty with itself.";
            if (target.def.permanentEnemy) return target.name + " is a permanent enemy — no treaty is possible.";
            if (player.def.permanentEnemy) return player.name + " is a permanent enemy of every civilization — no treaty is possible.";
            return null;
        }

        /// <summary>
        /// Why <paramref name="player"/> cannot sign <paramref name="treaty"/> with <paramref name="target"/>
        /// right now, or null when it can: every treaty-independent refusal of
        /// <see cref="TreatyRefusal(Faction, Faction)"/>, then the one rule in <see cref="Faction.SignTreaty"/>
        /// that does look at which treaty it is — while at war, only a treaty carrying
        /// <see cref="TreatyDef.nonAggression"/> may be signed, because only that kind ends the war. What
        /// <see cref="SignTreaty"/> checks and what <see cref="FactionRelationView.TreatyOffers"/> shows, per treaty.
        /// </summary>
        internal static string? TreatyRefusal(Faction player, Faction target, TreatyDef treaty)
        {
            string? refusal = TreatyRefusal(player, target);
            if (refusal != null) return refusal;
            if (!treaty.nonAggression && player.WarWith(target))
            {
                return "We are at war with " + target.name + " — only a pact of non-aggression can end it, and "
                    + treaty.LabelCap + " is not one.";
            }
            return null;
        }

        private static Faction? ResolveCivilization(string civilizationId)
        {
            if (string.IsNullOrEmpty(civilizationId)) return null;
            IReadOnlyList<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].loadID == civilizationId) return all[i];
            }
            return null;
        }

        private static TreatyDef? ResolveTreaty(string defName) =>
            string.IsNullOrEmpty(defName) ? null : DefDatabase<TreatyDef>.GetNamedSilentFail(defName);
    }
}
