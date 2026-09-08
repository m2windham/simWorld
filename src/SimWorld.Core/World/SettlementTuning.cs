namespace SimWorld.World
{
    /// <summary>
    /// Tuning for settlement founding and growth (spec §5b.3, §5b.5, §11.3/§11.5). RimWorld has no settlement-
    /// as-entity concept to source any of this from — a RimWorld <c>Settlement</c> is a tile, a faction and a
    /// trader comp, never a population.
    /// </summary>
    public static class SettlementTuning
    {
        /// <summary>
        /// The founding band's size range (spec §5b.3): "twenty to forty people in several households — the
        /// archaeological range for a neolithic founding group, and the smallest number at which demography
        /// works unaided."
        /// </summary>
        public static readonly IntRange FoundingBandRange = new IntRange(20, 40);

        /// <summary>
        /// Founders' ages are spread across this adult band rather than all starting at one age, so the band
        /// immediately has more than one generation's worth of marriage/fertility eligibility rather than
        /// every founder ageing out of it in lockstep. This port's own judgement call — the archaeological
        /// record fixes the band's size (§5b.3), not individual ages within it.
        /// </summary>
        public static readonly FloatRange FounderAgeRange = new FloatRange(18f, 40f);

        /// <summary>
        /// Net annual growth applied directly to a settlement's Statistical-tier population count (spec
        /// §11.3). A Statistical citizen deliberately has no live <c>Pawn</c> object (see
        /// <see cref="Settlement"/>'s own doc on why), so <c>FamilyManager.DemographyTick</c> — which runs
        /// marriages, births and deaths against real <c>Pawn</c> objects — has nothing to operate on for this
        /// slice of the population. This is the closed-form equivalent §11.1 requires for anything the
        /// abstract clock advances ("every process it advances needs a closed-form or sampled equivalent that
        /// agrees with ticking"): not an independently invented growth curve, but the actual net outcome
        /// §11.5 measured from the real per-pawn mechanism itself (a 120-year regression run: "demography's
        /// own growth — 4.5% a year, doubling every ~16 years"), applied here as the aggregate result that
        /// mechanism would have produced were it run per-citizen at a scale the Statistical tier exists
        /// specifically to avoid running per-citizen at all.
        /// </summary>
        public const float StatisticalNetGrowthPerYear = 0.045f;
    }
}
