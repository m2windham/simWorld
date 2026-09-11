using System;
using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.Health
{
    /// <summary>
    /// Treating wounds and diseases (RimWorld: <c>RimWorld.TendUtility</c>). One treatment tends the most urgent
    /// hediff; if that is an injury, every tendable injury is treated in the same batch. Quality comes from the
    /// caller for now — the skills module supplies the doctor's stat later.
    /// </summary>
    public static class TendUtility
    {
        public const float MaxQualityNoMedicine = 0.7f;
        public const float MaxQualityWithMedicine = 1f;

        /// <summary>Tends the pawn once. Returns the hediffs treated (empty when nothing needed tending).</summary>
        public static List<Hediff> DoTend(Pawn patient, float quality, float maxQuality = MaxQualityWithMedicine)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));
            var toTend = new List<Hediff>();
            GetOptimalHediffsToTendWithSingleTreatment(patient, toTend);
            for (int i = 0; i < toTend.Count; i++)
            {
                toTend[i].Tended(quality, maxQuality, i);
            }
            if (toTend.Count > 0)
            {
                patient.health.Notify_HediffChanged(null);
            }
            return toTend;
        }

        public static void GetOptimalHediffsToTendWithSingleTreatment(Pawn patient, List<Hediff> outHediffs)
        {
            outHediffs.Clear();
            var tendable = new List<Hediff>();
            List<Hediff> hediffs = patient.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].TendableNow()) tendable.Add(hediffs[i]);
            }
            if (tendable.Count == 0) return;
            tendable.Sort((a, b) => TendPriority(b).CompareTo(TendPriority(a)));

            Hediff first = tendable[0];
            outHediffs.Add(first);
            if (first is Hediff_Injury || first is Hediff_MissingPart)
            {
                for (int i = 1; i < tendable.Count; i++)
                {
                    if (tendable[i] is Hediff_Injury || tendable[i] is Hediff_MissingPart) outHediffs.Add(tendable[i]);
                }
            }
        }

        /// <summary>Bleeding and life-threatening conditions first, then by severity.</summary>
        public static float TendPriority(Hediff hediff)
        {
            float priority = 0f;
            if (hediff.BleedRate > 0f) priority += 1000f + hediff.BleedRate * 100f;
            HediffStage? stage = hediff.CurStage;
            if (stage != null && stage.lifeThreatening) priority += 500f;
            if (hediff.def.lethalSeverity > 0f) priority += 100f * (hediff.Severity / hediff.def.lethalSeverity);
            priority += hediff.Severity;
            return priority;
        }

        public static bool HasAnythingToTend(Pawn patient)
        {
            List<Hediff> hediffs = patient.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].TendableNow()) return true;
            }
            return false;
        }

        /// <summary>
        /// True when this patient has a tendable hediff that is bleeding or life-threatening — RimWorld's own
        /// split between <c>DoctorTendEmergency</c> and ordinary <c>DoctorTend</c> (both content-side
        /// <c>WorkGiverDef</c>s carry a plain <c>emergency</c> flag; nothing in the def itself says what
        /// "emergency" means for a patient, so this is that definition, in the one place both of them read it
        /// from — <see cref="AI.WorkGiver_Tend"/>). <see cref="TendPriority"/> already weighs bleeding (+1000)
        /// and life-threatening (+500) far above any ordinary severity, so whichever hediff a patient's own
        /// tend would treat first is exactly one of these whenever either is present — "does any tendable
        /// hediff qualify" and "is the most urgent one this" always agree, which is what lets
        /// <see cref="AI.WorkGiver_Tend"/> use this single check to sort every patient into exactly one of its
        /// two WorkGiverDefs rather than risking the same patient double-matching both.
        /// </summary>
        public static bool NeedsEmergencyTend(Pawn patient)
        {
            List<Hediff> hediffs = patient.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!hediff.TendableNow()) continue;
                if (hediff.BleedRate > 0f) return true;
                HediffStage? stage = hediff.CurStage;
                if (stage != null && stage.lifeThreatening) return true;
            }
            return false;
        }
    }
}
