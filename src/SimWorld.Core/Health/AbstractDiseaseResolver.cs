using System;
using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;

namespace SimWorld.Health
{
    /// <summary>
    /// Resolves every immunizable hediff on a <see cref="PawnTier.Interval"/> citizen across an elapsed span,
    /// coarsely — the disease counterpart of <see cref="Director.SettlementRaidResolver"/>: real, but not
    /// individually simulated. <see cref="Pawns.Pawn_TierTracker"/> never runs a full tick for a pawn at this
    /// tier (that is the entire point of the tier), so this is not a tick loop over <paramref name="elapsedTicks"/>
    /// — it is the closed-form answer to "who wins the race between <c>severityPerDayNotImmune</c> and
    /// <c>immunityPerDaySick</c> over this many days", read straight off the same <see cref="HediffDef"/>
    /// numbers <see cref="HediffComp_Immunizable.CompPostTick"/> and <see cref="ImmunityHandler.ImmunityHandlerTick"/>
    /// already tick through one day-fraction at a time at <see cref="PawnTier.Full"/>. Nothing here invents a
    /// new rate: every slope below is a def field or an existing per-pawn stat, exactly as the Full-tier path
    /// reads them.
    ///
    /// <para/><b>What this replaces.</b> Before this class existed, <c>Pawn_TierTracker.ApplyElapsed</c>'s
    /// Interval branch ran age and needs forward but left every hediff exactly as it was — a citizen who
    /// caught a disease while the player was watching, or via <see cref="Director.IncidentWorker_Disease"/>
    /// while they were not, froze mid-illness the moment nobody was looking and stayed frozen for as long as
    /// nobody looked again. That inverted the game's own premise: looking at a settlement is what let its
    /// people die of what they had caught, and looking away made them safe from it. A citizen at this tier can
    /// now die of what they caught, or beat it, exactly as one at Full tier could — see
    /// <see cref="ResolveElapsed"/>.
    ///
    /// <para/><b>The attention lever is medical capacity, not the camera.</b> A watched citizen gets tended by
    /// whichever colonist <see cref="AI.WorkGiver_Tend"/> sends, at that colonist's own
    /// <see cref="StatDefOf.MedicalTendQuality"/>. An unwatched citizen has no job system running to send
    /// anyone, so this reads the same stat off the settlement's own roster instead —
    /// <see cref="SettlementMedicalCapacity"/> is the best tend quality any other living citizen there could
    /// offer, exactly the number <see cref="AI.JobDriver_TendPatient"/> would have used had a job actually
    /// run. A settlement with nobody to spare (a lone citizen, or none at all) gets none of that benefit and
    /// races the disease on the def's own untended numbers, the same numbers
    /// <c>HealthTests.Plague_kills_untended_but_a_tended_pawn_survives</c> already pins for Full tier. A
    /// settlement with real medical capacity is not merely luckier for being unwatched — it is genuinely
    /// better off, because the number this reads is the settlement's own, not the player's attention.
    /// </summary>
    public static class AbstractDiseaseResolver
    {
        /// <summary>
        /// Advances every immunizable hediff on <paramref name="pawn"/> by <paramref name="elapsedTicks"/> of
        /// unwatched time. Not bounded the way needs are (<c>TieringTuning.MaxNeedCatchUpTicks</c>): that bound
        /// exists because nothing can eat inside a bulk call, and a disease owes the pawn no such opportunity
        /// — it runs its course whether or not anyone is there to answer it, exactly as age does in the same
        /// method. Safe to call on a pawn with no immunizable hediffs at all (the common case): the loop below
        /// then does nothing.
        /// </summary>
        public static void ResolveElapsed(Pawn pawn, int elapsedTicks)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawn.Dead || elapsedTicks <= 0) return;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (i >= hediffs.Count) continue; // an earlier iteration of this same loop removed it
                Hediff hediff = hediffs[i];
                HediffComp_Immunizable? immComp = hediff.TryGetComp<HediffComp_Immunizable>();
                if (immComp == null) continue;

                ResolveOne(pawn, hediff, immComp, elapsedTicks);
                if (pawn.Dead) return; // ResolveOne can kill; nothing left on this pawn to resolve further
            }
        }

        /// <summary>
        /// The race for one hediff. At most two linear phases — not-yet-immune, then immune — because immunity
        /// only ever climbs toward 1 across a span where the pawn is sick throughout, so it crosses that line
        /// at most once. Each phase is settled in one step (no per-tick or per-day iteration): severity and
        /// immunity are both piecewise-linear in time given a fixed rate, so the phase's whole duration can be
        /// computed directly rather than walked.
        /// </summary>
        private static void ResolveOne(Pawn pawn, Hediff hediff, HediffComp_Immunizable immComp, int elapsedTicks)
        {
            HediffCompProperties_Immunizable props = immComp.Props;
            HediffCompProperties_TendDuration? tendProps = hediff.def.CompProps<HediffCompProperties_TendDuration>();

            // Tending applies throughout, in both phases — exactly as HediffComp_TendDuration.CompPostTick
            // stacks its own severityAdjustment onto HediffComp_Immunizable's unconditionally, whatever the
            // immune state. Capped the same way JobDriver_TendPatient caps a real tend: this port ships no
            // medicine item yet, so no tend anywhere in it can exceed TendUtility.MaxQualityNoMedicine.
            float tendQuality = tendProps != null
                ? Math.Min(SettlementMedicalCapacity(pawn), TendUtility.MaxQualityNoMedicine)
                : 0f;
            float tendRate = tendProps != null ? tendProps.severityPerDayTended * tendQuality : 0f;

            ImmunityRecord record = pawn.health.immunity.EnsureRecord(hediff.def);
            float severity = hediff.Severity;
            float immunity = record.immunity;
            float lethal = hediff.def.lethalSeverity;

            // ImmunityRecord.ImmunityChangePerTick's own factor (Pawn.ImmunityGainSpeed), applied here exactly
            // as it is applied there for the sick case — this is that pawn's own gain speed, not a new one.
            float immunityGainSpeed = pawn.ImmunityGainSpeed;

            float remainingDays = elapsedTicks / (float)GenDate.TicksPerDay;

            for (int phase = 0; phase < 2 && remainingDays > 0f; phase++)
            {
                bool immune = immunity >= 1f;
                float severityPerDay = (immune ? props.severityPerDayImmune : props.severityPerDayNotImmune) + tendRate;
                float immunityPerDay = immune ? 0f : props.immunityPerDaySick * immunityGainSpeed;

                float daysToImmune = !immune && immunityPerDay > 0f
                    ? (1f - immunity) / immunityPerDay
                    : float.PositiveInfinity;
                float step = daysToImmune < remainingDays ? daysToImmune : remainingDays;
                if (step < 0f) step = 0f;

                severity += severityPerDay * step;
                if (!immune) immunity = GenMath.Clamp01(immunity + immunityPerDay * step);
                remainingDays -= step;
                record.immunity = immunity;

                if (lethal > 0f && severity >= lethal)
                {
                    // The same funnel a Full-tier tick would have reached: the Severity setter dirties caches
                    // and calls CheckForStateChange, which sees Hediff.CauseDeathNow() and kills through
                    // Pawn_HealthTracker.Kill with this exact hediff as exactCulprit — so Hediff.sourceIncident
                    // survives to StorytellerDeathEvents.SourceOf exactly as it would from a real tick.
                    hediff.Severity = lethal;
                    return;
                }
                if (severity <= 0f)
                {
                    // HealthTick's own removal loop never runs at this tier, so nothing else will notice a
                    // hediff has run itself out — do here what that loop would have done.
                    pawn.health.RemoveHediff(hediff);
                    return;
                }
            }

            hediff.Severity = severity;
        }

        /// <summary>
        /// The best tend quality any other living citizen of <paramref name="patient"/>'s own settlement could
        /// offer — 0 when the settlement cannot be found (no <c>World.World</c>, or nobody's roster contains
        /// this pawn) or has nobody else to give. Reads <see cref="StatDefOf.MedicalTendQuality"/>, the same
        /// skill-need-scaled stat <see cref="AI.JobDriver_TendPatient"/> already reads off a real doctor — no
        /// new notion of "medical capacity" is invented here, only an existing one asked of a roster instead
        /// of a job.
        ///
        /// <para/>Settlement has no reverse pointer from a citizen back to it (nothing needed one before this),
        /// so this walks every settlement's roster once — the same O(settlements) + O(citizens) shape
        /// <see cref="God.AttentionManager.Reconcile"/> already runs on its own periodic cadence, and this runs
        /// only for a pawn that is actually sick with something immunizable, at most once per its own Interval
        /// coarse tick (~<see cref="GenTicks.TickLongInterval"/> ticks apart).
        /// </summary>
        private static float SettlementMedicalCapacity(Pawn patient)
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return 0f;

            SimWorld.World.Settlement? home = null;
            foreach (SimWorld.World.Settlement settlement in world.Settlements)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                bool found = false;
                for (int i = 0; i < citizens.Count; i++)
                {
                    if (!ReferenceEquals(citizens[i], patient)) continue;
                    found = true;
                    break;
                }
                if (!found) continue;
                home = settlement;
                break;
            }
            if (home == null) return 0f;

            float best = 0f;
            IReadOnlyList<Pawn> roster = home.Citizens;
            for (int i = 0; i < roster.Count; i++)
            {
                Pawn caregiver = roster[i];
                // Never its own patient, matching WorkGiver_Tend's own rule — see that class's doc.
                if (caregiver == null || caregiver.Dead || ReferenceEquals(caregiver, patient)) continue;
                if (!caregiver.RaceProps.Humanlike) continue;

                float quality = caregiver.GetStatValue(StatDefOf.MedicalTendQuality);
                if (quality > best) best = quality;
            }
            return best;
        }
    }
}
