using System.Collections.Generic;

using SimWorld.Pawns;

namespace SimWorld.Health
{
    /// <summary>
    /// Which incident a wound belongs to — and so everything the wound goes on to cause.
    ///
    /// <para/><b>Why the wound has to carry it.</b> Most people a raid kills do not die while the raider is
    /// standing over them. They are downed, and then they bleed out, or the wound turns septic and they die of
    /// the infection a day or two later. By then there is no <see cref="DamageInfo"/> and no instigator — the
    /// death reaches <see cref="Pawn_HealthTracker.Kill"/> with <c>dinfo: null</c> and the blood loss or the
    /// infection as its culprit — so asking the killer where it came from, which is how
    /// <c>Director.StorytellerDeathEvents.SourceOf</c> credits a death on the spot, has nobody to ask. The seed
    /// that exposed this lost 25 people to raid injuries and credited none of them to a raid.
    ///
    /// <para/>So the answer is written down at the one moment it is known — the blow — and handed along the
    /// chain that follows it, on the field every hediff already has for exactly this question
    /// (<see cref="Hediff.sourceIncident"/>, which <c>Director.IncidentWorker_Disease</c> already stamps):
    /// <list type="bullet">
    /// <item>the injury, from the blow's instigator (<see cref="OfBlow"/>, at
    /// <see cref="DamageWorker_AddInjury"/>);</item>
    /// <item>the missing part a blow leaves, from the injury that destroyed it (<c>HediffSet</c>);</item>
    /// <item>a merged wound, from the larger of the two it was made from (<see cref="Hediff_Injury.TryMergeWith"/>);</item>
    /// <item>the infection a wound turns into, from that wound (<see cref="HediffComp_Infecter"/>);</item>
    /// <item>and blood loss, from whatever is bleeding into it (<see cref="AccrueBloodLoss"/>).</item>
    /// </list>
    /// A death at the end of the chain reads its culprit's stamp and is credited to the incident behind the
    /// wound, not to the proximate cause. What they <i>died of</i> is unchanged — blood loss is still an
    /// injury death and an infection still a disease death on the ledger's other axis. That axis answers a
    /// different question, and it was already right.
    ///
    /// <para/><b>It under-claims, never over-claims.</b> A wound nobody sent — a brawl, a fall, a native
    /// animal — carries null, and so does whatever it causes. Where two sources feed one hediff, the larger
    /// share wins, so a raid is never credited with a death its wound was the lesser part of.
    /// </summary>
    public static class WoundProvenance
    {
        /// <summary>The incident behind a blow: the <see cref="Pawn.spawnedByIncident"/> of the pawn that
        /// dealt it, or null for a blow nobody sent — a native, a falling roof, a fire, a trap.</summary>
        public static string? OfBlow(DamageInfo dinfo) => (dinfo?.Instigator as Pawn)?.spawnedByIncident;

        /// <summary>
        /// The provenance of whatever is bleeding hardest right now, or null when nothing is bleeding or the
        /// heaviest bleeder was sent by nobody. Ties keep the first in list order, so the answer is
        /// deterministic.
        /// </summary>
        public static string? OfBleeding(HediffSet set)
        {
            string? source = null;
            float heaviest = 0f;
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                float rate = hediffs[i].BleedRate;
                if (rate > heaviest)
                {
                    heaviest = rate;
                    source = hediffs[i].sourceIncident;
                }
            }
            return source;
        }

        /// <summary>
        /// Adds <paramref name="delta"/> blood loss — <see cref="HealthUtility.AdjustSeverity"/>'s own steps,
        /// in its own order, so no roll moves — with one addition: the blood loss is stamped with where the
        /// blood is going (<see cref="OfBleeding"/>) <b>before</b> its severity rises. The order is the point.
        /// Raising the severity is what kills: the setter runs the state check, and a pawn crossing the lethal
        /// line dies inside that assignment with this hediff as the culprit. Stamped afterwards, the death
        /// would read the previous interval's answer, or none at all.
        ///
        /// <para/>Re-stamped on every interval rather than kept from the first: blood loss has no single cause,
        /// and the question a death asks of it is what was bleeding into it when it became lethal.
        /// </summary>
        public static void AccrueBloodLoss(Pawn pawn, float delta)
        {
            string? source = OfBleeding(pawn.health.hediffSet);
            Hediff? bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                bloodLoss.sourceIncident = source;
                bloodLoss.Severity += delta;
                return;
            }
            if (delta <= 0f) return;

            Hediff made = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
            made.sourceIncident = source;
            made.Severity = delta;
            pawn.health.AddHediff(made);
        }
    }
}
