using System;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>
    /// When a patient's need for a doctor is urgent (RimWorld: <c>RimWorld.HealthAIUtility</c> and
    /// <c>Verse.HealthUtility.TicksUntilDeathDueToBloodLoss</c>, both ported from the 1.0 decompile).
    /// <para/>
    /// <b>Why urgency is its own question.</b> Urgent tending is emergency work, and emergency work outranks
    /// eating, sleeping and recreation in the humanlike think tree — it is what gets a doctor out of bed
    /// (<see cref="AI.JobDriver_LayDown"/>). A rule that broad has to be narrow about who qualifies, and
    /// RimWorld's is: a patient who will bleed to death inside
    /// <see cref="UrgentTicksUntilDeathDueToBloodLoss"/>. A scratched hunter bleeding 0.2 a day does not wake
    /// the settlement; a raid casualty bleeding 2 a day does. Everything else that needs a doctor — a slow
    /// bleed, a bruise, an infection at any stage — is ordinary doctoring, done in the ordinary work order.
    /// <para/>
    /// Its own file rather than methods added to <c>HealthUtility</c> (which lives in <c>Hediff.cs</c>, a file
    /// other lanes are editing) — CLAUDE.md's "add a file rather than edit a shared one".
    /// </summary>
    public static class HealthAIUtility
    {
        /// <summary>
        /// RimWorld's own threshold (<c>HealthAIUtility.ShouldBeTendedNowByPlayerUrgent</c>:
        /// <c>TicksUntilDeathDueToBloodLoss(pawn) &lt; 45000</c>) — eighteen in-game hours, the figure the
        /// RimWorld wiki's Doctoring page gives for "patients that will die from blood loss" being tended first.
        /// </summary>
        public const int UrgentTicksUntilDeathDueToBloodLoss = 45000;

        /// <summary>
        /// Ticks until the pawn's blood loss reaches its lethal severity at the current bleed rate, or
        /// <see cref="int.MaxValue"/> when it is not bleeding (RimWorld: <c>HealthUtility.TicksUntilDeathDueToBloodLoss</c>,
        /// the number the health tab shows as "death in"). RimWorld's formula, with RimWorld's own caveat: it
        /// assumes blood loss grows one severity per day per unit of bleed rate, which is this port's accrual too
        /// (<see cref="HealthTuning.BloodLossPerBleedUnitPerInterval"/>), and ignores recovery.
        /// </summary>
        public static int TicksUntilDeathDueToBloodLoss(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            float bleedRateTotal = pawn.health.hediffSet.BleedRateTotal;
            if (bleedRateTotal < 0.0001f) return int.MaxValue;
            Hediff? bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            float severity = bloodLoss?.Severity ?? 0f;
            // RimWorld hardcodes the 1 here rather than reading BloodLoss.lethalSeverity; so does this port's content.
            return (int)((1f - severity) / bleedRateTotal * GenDate.TicksPerDay);
        }

        /// <summary>
        /// A bleed that can still be tended and will kill inside <see cref="UrgentTicksUntilDeathDueToBloodLoss"/>
        /// (RimWorld: <c>HealthAIUtility.ShouldBeTendedNowByPlayerUrgent</c>). The "needs tending at all" half
        /// is <see cref="TendUtility.HasAnythingToTend"/>'s — this port's stand-in for RimWorld's
        /// <c>HasHediffsNeedingTendByPlayer</c> — narrowed to what is actually bleeding: a pawn whose every
        /// bleeding wound is already bandaged is losing no blood worth a doctor's night.
        /// </summary>
        public static bool ShouldBeTendedNowUrgent(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!HasTendableBleeding(pawn)) return false;
            return TicksUntilDeathDueToBloodLoss(pawn) < UrgentTicksUntilDeathDueToBloodLoss;
        }

        private static bool HasTendableBleeding(Pawn pawn)
        {
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].BleedRate > 0f && hediffs[i].TendableNow()) return true;
            }
            return false;
        }
    }
}
