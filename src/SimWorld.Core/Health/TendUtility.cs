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
    }
}
