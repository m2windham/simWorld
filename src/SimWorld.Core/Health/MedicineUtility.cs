using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Health
{
    /// <summary>
    /// Finds a doctor the medicine to carry to a tend (RimWorld: the medicine-search half of
    /// <c>HealthAIUtility.FindBestMedicine</c>, trimmed to what this port's map actually offers — no
    /// per-pawn <c>playerSettings.medCare</c> to read, since that lever is the settlement's own
    /// <see cref="Map.Map.medicalCare"/> here (see <see cref="MedicalCareCategory"/>'s own doc); no
    /// <c>Medicine.GetMedicineCountToFullyHeal</c> — <see cref="AI.JobDriver_TendPatient"/> always spends
    /// exactly one unit per tend (see that class's own doc for why a flat amount, not RimWorld's variable
    /// one, is this port's deliberate simplification).
    /// </summary>
    public static class MedicineUtility
    {
        /// <summary>
        /// The most potent medicine <paramref name="healer"/> can reach and reserve that
        /// <paramref name="patient"/>'s settlement's own <see cref="MedicalCareCategory"/> still allows —
        /// null when the care level forbids medicine outright (<see cref="MedicalCareCategory.NoCare"/>/
        /// <see cref="MedicalCareCategory.NoMeds"/>) or nothing reachable qualifies, in which case the tend
        /// still goes ahead medicine-less (RimWorld: <c>HealthAIUtility.FindBestMedicine</c> returning null
        /// is not a reason to refuse the job — see <see cref="AI.WorkGiver_Tend.JobOnThing"/>).
        /// </summary>
        public static Thing? FindBestMedicine(Pawn healer, Pawn patient)
        {
            if (healer == null || patient == null) return null;
            Map.Map? map = healer.Map;
            if (map == null || patient.Map != map) return null;

            MedicalCareCategory care = map.medicalCare;
            if (care <= MedicalCareCategory.NoMeds) return null;

            Thing? best = null;
            float bestPotency = -1f;
            System.Collections.Generic.IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing candidate = items[i];
                if (!candidate.Spawned || !candidate.def.IsMedicine) continue;
                if (!care.AllowsMedicine(candidate.def)) continue;
                if (!map.reservationManager.CanReserve(healer, candidate)) continue;
                if (!Reachability.CanReach(healer, candidate, PathEndMode.ClosestTouch)) continue;

                float potency = candidate.def.GetStatValue(HealthStatDefOf.MedicalPotency);
                if (potency <= bestPotency) continue;
                bestPotency = potency;
                best = candidate;
            }
            return best;
        }
    }
}
