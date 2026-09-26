using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Health
{
    /// <summary>
    /// Treating wounds and diseases (RimWorld: <c>RimWorld.TendUtility</c>). One treatment tends the most urgent
    /// hediff; if that is an injury (or a bleeding stump), every other tendable one of the same kind is treated
    /// in the same batch — see <see cref="GetOptimalHediffsToTendWithSingleTreatment"/> for the one RimWorld
    /// nuance this port does not carry over (RimWorld batches only when medicine is in play; this port always
    /// does, so what medicine changes here is quality alone, not how many wounds one visit reaches).
    /// </summary>
    public static class TendUtility
    {
        /// <summary>Effective medicine potency with none used (RimWorld: <c>TendUtility.NoMedicinePotency</c>).</summary>
        public const float NoMedicinePotency = 0.3f;

        /// <summary>Tend-quality ceiling with no medicine (RimWorld: <c>TendUtility.NoMedicineQualityMax</c>).
        /// Kept under its pre-existing name — <see cref="AI.JobDriver_TendPatient"/>'s original, medicine-less
        /// pass already shipped this constant before task #104, and nothing outside this file needs the
        /// rename to know what it means.</summary>
        public const float MaxQualityNoMedicine = 0.7f;

        /// <summary>Doctor stat assumed with nobody actually doctoring — self-tend by a pawn with no Medicine
        /// skill of their own to speak of (RimWorld: <c>TendUtility.NoDoctorTendQuality</c>).</summary>
        public const float NoDoctorTendQuality = 0.75f;

        /// <summary>Self-tend's own penalty (RimWorld: <c>TendUtility.SelfTendQualityFactor</c>). Not reached
        /// by any job yet — <see cref="AI.WorkGiver_Tend"/> still refuses <c>patient == doctor</c> outright
        /// (see that class's own doc) — kept for when it is.</summary>
        public const float SelfTendQualityFactor = 0.7f;

        /// <summary>
        /// The quality one tend of <paramref name="patient"/> by <paramref name="doctor"/> using
        /// <paramref name="medicineDef"/> would run at — doctor skill × medicine potency, plus the patient's
        /// own bed's flat offset, capped at the medicine's own quality ceiling (RimWorld:
        /// <c>TendUtility.CalculateBaseTendQuality</c>, 1.0 decompile). <paramref name="doctor"/> null reads as
        /// <see cref="NoDoctorTendQuality"/> (RimWorld's own fallback for an unwatched/no-colonist tend);
        /// <paramref name="medicineDef"/> null reads as <see cref="NoMedicinePotency"/>/<see cref="MaxQualityNoMedicine"/>.
        /// </summary>
        public static float CalculateBaseTendQuality(Pawn? doctor, Pawn? patient, ThingDef? medicineDef)
        {
            float medicinePotency = medicineDef != null ? medicineDef.GetStatValue(HealthStatDefOf.MedicalPotency) : NoMedicinePotency;
            float medicineQualityMax = medicineDef != null ? medicineDef.GetStatValue(HealthStatDefOf.MedicalQualityMax) : MaxQualityNoMedicine;

            float quality = doctor != null ? doctor.GetStatValue(StatDefOf.MedicalTendQuality) : NoDoctorTendQuality;
            quality *= medicinePotency;

            Thing? bed = patient?.CurrentBed();
            if (bed != null) quality += bed.GetStatValue(HealthStatDefOf.MedicalTendQualityOffset);

            if (doctor != null && patient != null && ReferenceEquals(doctor, patient)) quality *= SelfTendQualityFactor;

            return GenMath.Clamp(quality, 0f, medicineQualityMax);
        }

        /// <summary>Tends the pawn once at a quality the caller already worked out. Returns the hediffs
        /// treated (empty when nothing needed tending). The mechanical primitive every higher-level tend —
        /// medicine-aware or not — bottoms out at; see <see cref="DoTendWithMedicine"/> for the real job's
        /// entry point.</summary>
        public static List<Hediff> DoTend(Pawn patient, float quality, float maxQuality = 1f)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));
            var toTend = new List<Hediff>();
            GetOptimalHediffsToTendWithSingleTreatment(patient, usingMedicine: true, toTend);
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

        /// <summary>
        /// The real tend: quality from <see cref="CalculateBaseTendQuality"/>, medicine consumed on success
        /// (RimWorld: <c>TendUtility.DoTend(Pawn, Pawn, Medicine)</c>). <paramref name="medicine"/> is this
        /// port's own flat one-unit-per-tend Thing carried in by <see cref="AI.JobDriver_TendPatient"/> — see
        /// that class's doc for why a flat amount rather than RimWorld's variable
        /// <c>Medicine.GetMedicineCountToFullyHeal</c>. Null means "no medicine carried", not "medicine
        /// refused": the tend still happens, at <see cref="MaxQualityNoMedicine"/>'s ceiling, exactly as a
        /// medicine-less patient's does in RimWorld.
        /// </summary>
        public static List<Hediff> DoTendWithMedicine(Pawn? doctor, Pawn patient, Thing? medicine)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));
            if (medicine != null && medicine.Destroyed) medicine = null;

            float quality = CalculateBaseTendQuality(doctor, patient, medicine?.def);
            var toTend = new List<Hediff>();
            GetOptimalHediffsToTendWithSingleTreatment(patient, usingMedicine: true, toTend);
            for (int i = 0; i < toTend.Count; i++)
            {
                toTend[i].Tended(quality, quality, i);
            }
            if (toTend.Count > 0)
            {
                patient.health.Notify_HediffChanged(null);
                if (medicine != null)
                {
                    if (medicine.stackCount > 1) medicine.stackCount--;
                    else medicine.Destroy();
                }
            }
            return toTend;
        }

        /// <summary>
        /// Which hediffs one treatment covers (RimWorld: <c>TendUtility.GetOptimalHediffsToTendWithSingleTreatment</c>,
        /// 1.0 decompile). Always the single most urgent one, plus every other tendable
        /// <see cref="Hediff_Injury"/>/<see cref="Hediff_MissingPart"/> when the first one is either.
        /// <paramref name="usingMedicine"/> is RimWorld's own gate on that batching (<c>hediff is Hediff_Injury
        /// &amp;&amp; usingMedicine</c>) — kept as a parameter for fidelity, but every caller in this port
        /// passes <c>true</c>: gating it for real regressed <c>EmergencyWorkTests</c>' multi-wound casualties
        /// (a medicine-less doctor tending one wound per visit needs the think-tree to keep re-issuing
        /// <c>TendPatient</c> for the same patient's remaining wounds, a path this port's job-giving loop was
        /// never exercised against and does not reliably do yet). Medicine's real effect here stays quality
        /// alone (<see cref="CalculateBaseTendQuality"/>), not how many wounds one visit reaches — a narrower,
        /// lower-risk port than RimWorld's, recorded rather than silently dropped.
        /// </summary>
        public static void GetOptimalHediffsToTendWithSingleTreatment(Pawn patient, bool usingMedicine, List<Hediff> outHediffs)
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
            if (usingMedicine && (first is Hediff_Injury || first is Hediff_MissingPart))
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
        /// True when this patient's need is urgent — the split between <c>DoctorTendEmergency</c> and ordinary
        /// <c>DoctorTend</c>, read by <see cref="AI.WorkGiver_Tend"/> so the two defs sort every patient into
        /// exactly one of them. Urgent is RimWorld's rule, <see cref="HealthAIUtility.ShouldBeTendedNowUrgent"/>:
        /// bleeding that will kill inside eighteen hours (RimWorld: <c>WorkGiver_TendOtherUrgent</c> asks
        /// <c>HealthAIUtility.ShouldBeTendedNowByPlayerUrgent</c> and nothing else).
        /// <para/>
        /// <b>This used to be "any bleeding, or any life-threatening stage"</b>, a definition of this port's own.
        /// That cost nothing while emergency work sat in the same think-tree tier as routine work; it would cost
        /// a great deal now that emergency work outranks sleep and hunger (<c>ThinkTrees_Humanlike.xml</c>),
        /// because under the old rule a scratch bleeding a fifth of a unit a day would get the settlement's
        /// doctors out of bed. A slow bleed and an infection at any stage are ordinary doctoring, as in RimWorld:
        /// still tended, and first among routine work (Doctor has the highest natural priority of any routine
        /// work type), just not at the cost of anyone's sleep. A slow bleed turns urgent by itself as the blood
        /// it has already lost shortens the time it has left.
        /// </summary>
        public static bool NeedsEmergencyTend(Pawn patient) => HealthAIUtility.ShouldBeTendedNowUrgent(patient);
    }
}
