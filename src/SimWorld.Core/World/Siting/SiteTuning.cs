namespace SimWorld.World.Siting
{
    /// <summary>
    /// Necessity-gate tuning for <see cref="SiteScorer"/> (spec §5b.2's "hard necessities... times weighted
    /// advantages"). Necessities are deliberately era-independent here — see this module's report-back for
    /// why "land that feeds the group at this era's technology" was simplified to an era-independent floor
    /// on food availability, with the era-specific *preference* between farmland, foraging and game left to
    /// <see cref="SiteWeightDef"/>'s advantage weights instead. Every number below is this port's own
    /// judgement call; RimWorld has no settlement-founding necessity gate to source them from.
    /// </summary>
    public static class SiteTuning
    {
        /// <summary>Deposit magnitude (own tile or a neighbour) at/above which fresh water counts as "in
        /// reach"; below it, necessity ramps linearly down to 0 rather than cutting off sharply.</summary>
        public const float FreshWaterNecessityFloor = 0.15f;

        /// <summary>Same ramp-floor idea as <see cref="FreshWaterNecessityFloor"/>, applied to the best of
        /// ArableSoil/Game/Timber (own tile or a neighbour): "land that feeds the band" at all, independent
        /// of which food source an era actually favours.</summary>
        public const float FoodNecessityFloor = 0.15f;

        /// <summary>Below this temperature (°C), no pre-industrial settlement is survivable unaided.</summary>
        public const float SurvivableTemperatureMinC = -30f;

        /// <summary>Above this temperature (°C), no pre-industrial settlement is survivable unaided.</summary>
        public const float SurvivableTemperatureMaxC = 45f;

        /// <summary>Width, in °C, of the ramp from 0 to full necessity inside each survivable-band edge, so
        /// the gate softens near the limit instead of stepping abruptly from "fine" to "zero".</summary>
        public const float TemperatureRampMarginC = 10f;
    }
}
