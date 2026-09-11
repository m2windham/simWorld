using System.Collections.Generic;

using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// A death reaching the narrator: the letter the player gets, and the adaptation the storyteller pays.
    /// The death-side twin of <see cref="StorytellerPawnEvents"/>, which does the same job for a downing;
    /// its own file for the usual reason (<c>CLAUDE.md</c>), and split from it because the two ask different
    /// questions of the same pawn.
    ///
    /// <para/><b>What could not be told apart before.</b> <see cref="DamageDef.externalViolence"/> is how
    /// RimWorld separates a killing from a death, and no line of this port read it — so a citizen shot in
    /// the street, a citizen who starved, and a citizen who died of old age in her sleep were one
    /// undifferentiated event. Two consequences turned on that difference and neither could be written:
    ///
    /// <list type="bullet">
    /// <item>The storyteller eases off a civilization that is losing people <i>to threats</i>. That is the
    /// whole meaning of <see cref="StoryWatcher_Adaptation"/>: quiet time builds slack, and casualties
    /// spend it. A death from age is not a casualty, and charging one would make a long-lived population
    /// read as a besieged one — which is exactly why the raid lane raised
    /// <see cref="StoryWatcher_Adaptation.Notify_ColonistDied"/> from <c>SettlementRaidResolver</c> and
    /// deliberately not from <c>FamilyManager.HandleDeath</c>. That call site was the workaround for a
    /// missing classifier. This is the classifier, so every violent death now counts, including the ones
    /// that happen on a map where no raid resolver is watching.</item>
    /// <item>The letter says what happened, which needs <see cref="DamageDef.deathMessage"/> — content that
    /// has shipped since the damage module landed and whose recorded reason for being unread ("host-facing
    /// text for a death letter the core does not compose") expired the moment the core had a letter stack.
    /// Six systems raise letters through it; a death raised none.</item>
    /// </list>
    ///
    /// <para/><b>Double-charging is what the two gates are for.</b> A raid settled abstractly by
    /// <c>SettlementRaidResolver</c> kills through <c>FamilyManager.HandleDeath</c>, which has no
    /// <see cref="DamageInfo"/> to hand <c>Kill</c> — so those deaths read as non-violent here and are
    /// charged once, by the resolver, exactly as they were before. A death with real damage behind it is
    /// charged once, here. No death is charged twice, and the test suite pins it.
    /// </summary>
    public static class StorytellerDeathEvents
    {
        /// <summary>
        /// Raised from <c>Pawn_HealthTracker.Kill</c> for every death in the port, however it happened.
        /// Returns whether the storyteller's adaptation was actually charged — a test wants to tell "not one
        /// of ours" and "not a violent death" from "counted".
        /// </summary>
        public static bool Notify_PawnDied(Pawn pawn, DamageInfo? dinfo, Hediff? culprit)
        {
            if (pawn == null) return false;

            bool violent = dinfo != null && dinfo.Def.externalViolence;

            SendDeathLetter(pawn, dinfo, culprit);

            if (!violent) return false;
            if (!StorytellerPawnEvents.IsCivilizationMember(pawn, allowDead: true)) return false;

            Find.Storyteller.adaptation.Notify_ColonistDied();
            return true;
        }

        /// <summary>
        /// One letter per death of somebody the player has a stake in
        /// (<see cref="PawnUtility.ShouldSendNotificationAbout"/>: their own people and the prisoners they
        /// hold). RimWorld's <c>Pawn_HealthTracker.NotifyPlayerOfKilled</c> composes the same three-way
        /// message and in the same order of preference — the damage's own
        /// <see cref="DamageDef.deathMessage"/> first, then the hediff that finished them, then a bare
        /// statement of the fact.
        /// </summary>
        private static void SendDeathLetter(Pawn pawn, DamageInfo? dinfo, Hediff? culprit)
        {
            if (!PawnUtility.ShouldSendNotificationAbout(pawn)) return;

            string text;
            if (dinfo != null && !string.IsNullOrEmpty(dinfo.Def.deathMessage))
            {
                text = PawnUtility.FormatWithPawn(dinfo.Def.deathMessage, pawn);
            }
            else if (culprit != null)
            {
                text = pawn.Label + " has died of " + culprit.Label + ".";
            }
            else
            {
                text = pawn.Label + " has died.";
            }

            Find.LetterStack.ReceiveLetter(
                "Death: " + pawn.Label,
                text,
                DeathLetterDefOf.Death,
                new List<string> { pawn.GetUniqueLoadID() });
        }
    }
}
