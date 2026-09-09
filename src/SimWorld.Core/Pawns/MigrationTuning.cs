namespace SimWorld.Pawns
{
    /// <summary>
    /// Migration/emigration tuning (system: demography, tracker item <c>demography.migration</c>). RimWorld has
    /// no civilization-scale immigration mechanic to source these from — the closest kin is the birth formula
    /// this module already carries over the *shape* of (<see cref="FamilyManager.ComputeBirthChance"/>: base ×
    /// (0.5 + quality) × a situational factor), not any RimWorld literal. Every constant here is SimWorld's own,
    /// documented at its declaration and pinned by <c>DemographyTests</c> as a band or a trend over a simulated
    /// span, never a bare literal.
    /// </summary>
    public static class MigrationTuning
    {
        /// <summary>
        /// How often migration is considered — the same cadence <see cref="DemographyTuning.DemographyIntervalTicks"/>
        /// already uses (once per in-game year), reused rather than duplicated so the two rare-tick population
        /// concerns move in lockstep instead of drifting out of phase over a long run.
        /// </summary>
        public const int MigrationIntervalTicks = DemographyTuning.DemographyIntervalTicks;

        /// <summary>
        /// Chance, per settlement per interval, that one new household arrives and founds itself
        /// (<see cref="FamilyManager.FoundHousehold"/>) when conditions are exactly neutral (quality 0.5 — see
        /// <see cref="MigrationManager.SettlementQuality"/>). Chosen the same order of magnitude as
        /// <see cref="DemographyTuning.BaseBirthChancePerInterval"/> (0.35) so immigration reads as a second,
        /// meaningful channel of growth alongside births rather than either dwarfing or being lost next to it —
        /// not sourced, since nothing SimWorld ported has an immigration rate to source it from.
        /// </summary>
        public const float ArrivalBaseChancePerInterval = 0.25f;

        /// <summary>
        /// Extra multiplier on the arrival chance per <see cref="Research.EraDef.order"/> step the civilization
        /// has reached (<see cref="MigrationManager.ArrivalChance"/>) — a more advanced settlement has more to
        /// offer an arriving migrant. Deliberately small: across a dozen eras this roughly doubles the base
        /// chance rather than dominating the mood/food term, since era is a slow background trend and quality of
        /// life is the thing migration is actually meant to track.
        /// </summary>
        public const float EraArrivalBonusPerOrder = 0.04f;

        /// <summary>
        /// Net annual growth rate applied to a settlement's <c>StatisticalPopulation</c> from migration alone
        /// (<see cref="MigrationManager.GrowStatisticalCohortByMigration"/>), at maximum quality — scaled down by
        /// <see cref="MigrationManager.SettlementQuality"/> the same way <see cref="ArrivalChance"/> is, and
        /// floored at zero rather than made negative (<c>World.Settlement.StatisticalPopulation</c> exposes no
        /// public way to shrink itself — see that overload's own doc for the limitation this floor stands in
        /// for). Same order of magnitude as <c>World.SettlementTuning.StatisticalNetGrowthPerYear</c>
        /// (births-minus-deaths for that same cohort) so migration neither swamps nor is lost next to natural
        /// growth.
        /// </summary>
        public const float StatisticalNetMigrationRatePerYearAtMaxQuality = 0.02f;

        /// <summary>
        /// A family's mood/food quality average (see <see cref="MigrationManager.SettlementQuality"/>) below
        /// which it is eligible to consider leaving. Stricter than <see cref="DemographyTuning.FoodSecurityThreshold"/>
        /// (0.5, which merely discourages a birth) since departure is a much bigger step than a smaller chance
        /// of conceiving — a household should be in real, sustained distress before it uproots, not merely
        /// below-average.
        /// </summary>
        public const float DepartureQualityThreshold = 0.3f;

        /// <summary>
        /// Chance, per eligible (distressed) family per interval, that it actually departs. Kept well under 1 so
        /// a settlement that dips below the distress threshold does not empty out in a single year — real
        /// households give a bad situation more than one season before giving up on a place.
        /// </summary>
        public const float BaseDepartureChancePerInterval = 0.2f;

        /// <summary>Youngest age a generated migrant can arrive at — old enough to found a household immediately
        /// (<see cref="DemographyTuning.MinMarriageAgeYears"/>), never a migrant minor arriving alone.</summary>
        public const float MinMigrantAgeYears = DemographyTuning.MinMarriageAgeYears;

        /// <summary>
        /// Oldest age a generated migrant can arrive at. Skews migration toward young adults — historically the
        /// demographic most likely to relocate — without excluding an older founder outright; not sourced (no
        /// RimWorld or Epoch precedent models migrant age), chosen as roughly a generation above the minimum.
        /// </summary>
        public const float MaxMigrantAgeYears = 35f;
    }
}
