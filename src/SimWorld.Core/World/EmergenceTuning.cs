using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// Tuning for <see cref="EmergenceManager"/> (spec §5b.4/§5b.5, tracker items <c>settlements.emergence</c>
    /// and <c>worldgen.civscale</c>). RimWorld has nothing to source any of this from — a RimWorld game never
    /// starts alone and never watches a new faction come into being — so every number here is SimWorld's own
    /// judgement call. Per the project's own rule for unsourced numbers: the *behaviour* these numbers produce
    /// (a band of civilizations over a simulated span, never zero forever, never a crowded planet either) is
    /// what <c>EmergenceTests</c> pins; the literals below are free to retune without breaking that contract.
    /// </summary>
    public static class EmergenceTuning
    {
        /// <summary>
        /// How often <see cref="EmergenceManager.Tick"/> re-evaluates emergence and expansion: once per
        /// in-game year, the same cadence <see cref="Settlement.GrowthTick"/> and
        /// <c>Pawns.FamilyManager.DemographyTick</c> already gate themselves to — population-scale events do
        /// not need to be re-checked faster than population itself actually changes.
        /// </summary>
        public const int CheckIntervalTicks = GenDate.TicksPerYear;

        /// <summary>
        /// Mean years between a brand new rival civilization emerging, at <c>OverallPopulation.Normal</c>
        /// (<see cref="Gen.WorldGenStep_Factions.PopulationMultiplier"/> scales it for other settings — the
        /// "population scaling" half of <c>worldgen.civscale</c>). Chosen so a solo start stays plausibly
        /// alone for its first few decades — the spec's own "truer sticks-and-stones arc" — while a
        /// centuries-long civilization-scale game reliably sees several rivals rise over its run; pinned by
        /// <c>EmergenceTests</c>' simulated-span band, never by this literal.
        /// </summary>
        public const float NewCivilizationMTBYears = 60f;

        /// <summary>
        /// A settlement below this total population has nobody to spare for a new settlement — it is still
        /// close to its own founding-band size (<see cref="SettlementTuning.FoundingBandRange"/>) and has not
        /// yet grown into "more than one settlement's worth" of people. SimWorld's own judgement call; pinned
        /// by a test that a settlement under threshold never expands regardless of the MTB roll.
        /// </summary>
        public const int ExpansionPopulationThreshold = 100;

        /// <summary>
        /// Mean years between an eligible (above-threshold) settlement spinning off a new one. Deliberately
        /// faster than <see cref="NewCivilizationMTBYears"/>: growing into a second settlement is an ordinary
        /// demographic event for an established civilization, not the rarer, world-scale event a whole new
        /// rival civilization is.
        /// </summary>
        public const float ExpansionMTBYears = 50f;

        /// <summary>
        /// A civilization stops expanding once it holds this many settlements — reuses
        /// <see cref="Gen.WorldGenStep_Factions.SettlementsPerFactionRange"/>'s own upper bound (already
        /// authored for "how many settlements one faction instance gets") rather than inventing a second
        /// ceiling for the same idea.
        /// </summary>
        public static int MaxSettlementsPerFaction => Gen.WorldGenStep_Factions.SettlementsPerFactionRange.max;
    }
}
