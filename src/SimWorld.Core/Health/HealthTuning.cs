namespace SimWorld.Health
{
    /// <summary>Health constants (RimWorld: <c>Verse.HealthTuning</c>). Content-independent knobs live here.</summary>
    public static class HealthTuning
    {
        /// <summary>Consciousness below this cannot stay awake: the pawn is downed.</summary>
        public const float ConsciousnessAwakeThreshold = 0.3f;

        /// <summary>A capacity at or below this counts as absent.</summary>
        public const float MinCapableLevel = 0.0001f;

        /// <summary>Fraction of pain that is subtracted from consciousness (100% pain = −50%).</summary>
        public const float PainConsciousnessFactor = 0.5f;

        public const int HealInterval = 600;
        /// <summary>60,000 ticks / <see cref="HealInterval"/>.</summary>
        public const int HealIntervalsPerDay = 100;
        public const float BaseHealPerDay = 8f;
        public const float LyingDownHealBonusPerDay = 4f;
        /// <summary>Extra healing per day for a tended wound, × tend quality.</summary>
        public const float TendedHealPerDay = 22f;

        public const int BleedInterval = 60;
        public const float MinBleedRateToBleed = 0.1f;
        /// <summary>Blood-loss severity gained per bleed-rate unit per <see cref="BleedInterval"/> (rate 1 ⇒ 1.0/day).</summary>
        public const float BloodLossPerBleedUnitPerInterval = 0.001f;

        /// <summary>Total injury severity at which any flesh pawn dies regardless of parts.</summary>
        public const float LethalDamageThreshold = 150f;

        public const float BecomePermanentBaseChance = 0.02f;

        /// <summary>Delay before an untended wound rolls for infection.</summary>
        public static readonly IntRange InfectionDelayRange = new IntRange(15000, 45000);

        /// <summary>Malnutrition severity per 150-tick need interval while starving (0.113/day).</summary>
        public const float MalnutritionSeverityPerInterval = 0.113f / 400f;

        /// <summary>Random spread applied to a tend's quality.</summary>
        public const float TendQualityVariance = 0.25f;
    }
}
