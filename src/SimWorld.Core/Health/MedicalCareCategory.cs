using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Health
{
    /// <summary>
    /// How aggressively a patient may be treated (RimWorld: <c>RimWorld.MedicalCareCategory</c>), from
    /// nothing at all up through whatever the settlement can find. Ordered worst to best so a "worse than X"
    /// comparison (<see cref="MedicalCareUtility.AllowsMedicine"/>) can use the plain <c>&lt;=</c>/<c>&gt;</c>
    /// operators RimWorld's own enum relies on, byte-backed to match.
    /// <para/>
    /// <b>Deviation, and it is the whole point of this port's version:</b> RimWorld sets this per pawn
    /// (<c>Pawn_PlayerSettings.medCare</c>) — a dropdown on every colonist's health tab. This is a god-game
    /// with no such tab, so <c>docs/design/player-first.md</c>'s lever is the settlement's own standing rule
    /// instead: one value per settlement (<see cref="Map.Map.medicalCare"/>), read by every tend the same
    /// way RimWorld reads a pawn's own. See <c>MapCommands.SetMedicalCare</c> for where the player sets it.
    /// </summary>
    public enum MedicalCareCategory : byte
    {
        /// <summary>Not tended at all, by anyone (RimWorld: <c>HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer</c>
        /// refuses outright). The one setting <see cref="AI.WorkGiver_Tend"/> refuses a job over.</summary>
        NoCare,

        /// <summary>Tended, but never with medicine — every tend runs at <see cref="TendUtility.NoMedicinePotency"/>'s
        /// ceiling regardless of what the settlement has in storage (RimWorld: <c>HealthAIUtility.FindBestMedicine</c>
        /// returns null at or below this).</summary>
        NoMeds,

        /// <summary>Herbal medicine or anything weaker (never spends anything above herbal's own potency).</summary>
        HerbalOrWorse,

        /// <summary>Industrial medicine or anything weaker.</summary>
        NormalOrWorse,

        /// <summary>Whatever is on hand, however potent.</summary>
        Best
    }

    /// <summary>RimWorld: <c>RimWorld.MedicalCareUtility</c>, trimmed to the one query a settlement without a
    /// per-pawn dropdown still needs — which medicine a given care level will actually spend.</summary>
    public static class MedicalCareUtility
    {
        /// <summary>
        /// Whether <paramref name="category"/> permits spending <paramref name="medicine"/> at all (RimWorld:
        /// <c>MedicalCareCategory.AllowsMedicine</c>). <see cref="MedicalCareCategory.NoCare"/> and
        /// <see cref="MedicalCareCategory.NoMeds"/> both refuse every medicine — the two differ only in
        /// whether a doctor comes at all, which <see cref="MedicalCareCategory.NoMeds"/> still allows.
        /// </summary>
        public static bool AllowsMedicine(this MedicalCareCategory category, ThingDef medicine)
        {
            if (medicine == null) return false;
            switch (category)
            {
                case MedicalCareCategory.NoCare:
                case MedicalCareCategory.NoMeds:
                    return false;
                case MedicalCareCategory.HerbalOrWorse:
                    return medicine.GetStatValue(HealthStatDefOf.MedicalPotency)
                        <= MedicineDefOf.MedicineHerbal.GetStatValue(HealthStatDefOf.MedicalPotency);
                case MedicalCareCategory.NormalOrWorse:
                    return medicine.GetStatValue(HealthStatDefOf.MedicalPotency)
                        <= MedicineDefOf.MedicineIndustrial.GetStatValue(HealthStatDefOf.MedicalPotency);
                case MedicalCareCategory.Best:
                    return true;
                default:
                    return false;
            }
        }
    }
}
