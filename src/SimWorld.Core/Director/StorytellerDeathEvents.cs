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
    /// <para/><b>Adaptation is charged for every death of ours, whatever it was.</b> That is RimWorld's rule,
    /// and it is simpler than the one this port had. <c>Verse.Pawn.Kill</c> raises
    /// <c>Find.Storyteller.Notify_PawnEvent(this, AdaptationEvent.Died)</c> with no <c>DamageInfo</c> at all,
    /// and <c>RimWorld.StoryWatcher_Adaptation.Notify_PawnEvent</c> filters only on who the pawn is
    /// (humanlike, <c>IsColonist</c>, not a prisoner) before charging
    /// <c>adaptDaysLossFromColonistLostByPostPopulation</c>. The one violence test in that method,
    /// <c>dinfo.Value.Def.ExternalViolenceFor(p)</c>, sits on the <c>Downed</c> branch and nowhere else.
    /// Read in two decompiles, of 1.0 (josh-m/RW-Decompile, where the call is inline in <c>Kill</c>) and of
    /// 1.6 (Dyyrlysh/RimworldDecompile, where it moved to <c>DoKillSideEffects</c>), and the same in both.
    ///
    /// <para/>This port used to charge only a death whose <see cref="DamageInfo"/> declared
    /// <see cref="DamageDef.externalViolence"/>, on the argument that a death from age is not a casualty. It
    /// was never recorded as a translation, and it cost more than it bought: most people a raid kills are
    /// not killed by the blow. They are downed, and they bleed out or die of the infected wound later, with
    /// <c>dinfo: null</c>, so a violence gate read them as natural deaths and the storyteller never paid for
    /// them. In the populated runs adaptation rose on the day six people bled out
    /// (<c>docs/perf/storyteller-populated</c>). RimWorld never had that hole because it never asks.
    ///
    /// <para/><b>One charging site.</b> A raid settled abstractly by <c>SettlementRaidResolver</c> kills
    /// through <c>FamilyManager.HandleDeath</c>, which reaches <c>Kill</c> and so reaches here like any other
    /// death. The resolver used to charge its own dead because the violence gate here turned them away; with
    /// the gate gone it does not, so no death is charged twice. The test suite pins that.
    ///
    /// <para/><b>The letter</b> says what happened, which needs <see cref="DamageDef.deathMessage"/>, content
    /// that had shipped since the damage module landed and that nothing read until the core had a letter
    /// stack.
    /// </summary>
    public static class StorytellerDeathEvents
    {
        /// <summary>
        /// Raised from <c>Pawn_HealthTracker.Kill</c> for every death in the port, however it happened.
        /// Returns whether the storyteller's adaptation was charged, which is exactly whether the pawn was one
        /// of ours. A test uses it to tell "not one of ours" from "counted".
        /// </summary>
        public static bool Notify_PawnDied(Pawn pawn, DamageInfo? dinfo, Hediff? culprit)
        {
            if (pawn == null) return false;

            bool ours = StorytellerPawnEvents.IsCivilizationMember(pawn, allowDead: true);

            SendDeathLetter(pawn, dinfo, culprit);

            // The ledger takes every death of ours, whatever killed them, and takes it here because this is
            // the only place every death in the port passes through. Starvation and disease are exactly the
            // two causes that had no sink at all before (see DeathLedger and DeathCauseClassifier). Counted
            // once per body, because Pawn_HealthTracker.Kill returns early for a pawn already dead.
            if (ours)
            {
                Find.Storyteller.deaths.Record(pawn.health.CauseOfDeath);

                // Second axis: not what they died of, but what killed them. An on-map violent death answers
                // this from its instigator; a raid or pack resolved abstractly has no instigator to read at
                // all, so the worker that resolved it attributes its own toll from the outcome it already
                // computes instead (see IncidentWorker_ManhunterPack). A disease death, and a wound that kills
                // later — bleeding out, or the infection it turned into — sit between the two: they reach here
                // through this exact funnel, but dinfo is null (nothing struck the pawn just now), so there is
                // no instigator to ask. SourceOf's second argument is that case's answer: the culprit hediff's
                // own provenance, stamped by the incident that caused it or carried down the wound chain from
                // the blow (Health.WoundProvenance).
                Find.Storyteller.deaths.RecordAttributed(SourceOf(dinfo, culprit));
            }

            // And the settlement mourns them. Here rather than in PawnDiedThoughtsUtility because that one
            // answers "who saw this", which an abstractly-resolved death has no answer to — no DamageInfo,
            // no map, no spawned witnesses. Being told is not being there, and a settlement that loses
            // people while nobody is watching has to be able to feel it. See Thoughts.BereavementUtility.
            if (ours) SimWorld.Thoughts.BereavementUtility.Notify_CitizenDied(pawn);

            if (!ours) return false;

            // RimWorld's AdaptationEvent.Died: every colonist death, with no question asked about the cause
            // (see the class doc for where that rule is read). A citizen who bleeds out a day after a raid, or
            // dies of the infected wound, is charged here exactly as one shot dead on the spot is.
            Find.Storyteller.adaptation.Notify_ColonistDied();
            return true;
        }

        /// <summary>
        /// What killed them, as opposed to what they died of: the <c>defName</c> of the incident that put the
        /// killer — or the condition, or the wound — in the world, or null when nobody is to blame in that
        /// sense: a death by age or hunger, or a killer, illness or wound that was always here.
        ///
        /// <para/>Attribution is by <i>provenance</i> rather than by timing. An ambient "what was happening at
        /// the time" window would have been cheaper and would have been wrong: a citizen who starves while a
        /// threat is on the map did not die of the threat. Asking the corpse's killer where it came from
        /// cannot make that mistake, and neither can asking the hediff that finished them the same question.
        /// It also cuts the other way, and that is the case that was missed: a citizen downed in a raid who
        /// bleeds out the next day died long after the threat left, and was still killed by it.
        ///
        /// <para/><b>The instigator is asked first, and the hediff when there is no instigator to ask.</b>
        /// A death on the spot carries a <see cref="DamageInfo"/>, so its instigator's own
        /// <see cref="Pawns.Pawn.spawnedByIncident"/> answers it; the culprit there is the wound that blow just
        /// made, stamped from that same instigator, so the two cannot disagree. A disease death, a bleed-out and
        /// an infected wound carry no <see cref="DamageInfo"/> at all — <see cref="Pawn_HealthTracker.Kill"/> was
        /// called with <c>dinfo: null</c> — so they fall through to <paramref name="culprit"/>'s own
        /// <see cref="Hediff.sourceIncident"/>: the incident that started the illness, or, for blood loss and
        /// infection, the one behind the wound that caused them (<see cref="Health.WoundProvenance"/>). Either
        /// source, once read, is exactly what <see cref="DeathLedger.RecordAttributed"/> is handed.
        /// </summary>
        internal static string? SourceOf(DamageInfo? dinfo, Hediff? culprit) =>
            (dinfo?.Instigator is Pawn killer ? killer.spawnedByIncident : null) ?? culprit?.sourceIncident;

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
