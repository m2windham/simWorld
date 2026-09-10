using SimWorld.Sim;

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
        /// How often <see cref="Settlement.Tick"/> reconciles <see cref="Settlement.Citizens"/> against
        /// <see cref="Settlement.InteriorMap"/> — spawning a newly-Full citizen who is not yet on the map,
        /// despawning one who no longer qualifies, and dropping anyone who has died (spec §11.2/§11.3's
        /// physical-presence seam). The same "rare bucket" cadence <see cref="Building.SettlementConstructionInitiative"/>
        /// already self-gates on, not a bespoke interval — frequent enough that a newborn or a migrant shows
        /// up on the map without a perceptible delay, cheap enough that scanning a settlement's own (small,
        /// spec §11.3) <see cref="Settlement.Citizens"/> list every 250 ticks costs nothing. Entering a
        /// settlement (<see cref="Settlement.EnterMap"/>) also runs this immediately, unconditionally, so a
        /// freshly-opened settlement never waits out this interval to show its founders.
        /// </summary>
        public const int CitizenMapSyncIntervalTicks = GenTicks.TickRareInterval;

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

        /// <summary>
        /// Statistical starting population for a settlement placed at world generation for a civilization
        /// that is not the game's own opening moment — an NPC civilization, or (until the two-stage
        /// region-then-site UI exists) a placeholder for the player's own extra starting settlements. These
        /// are backstory the player never watched happen, the same "history begins there, it was not lived
        /// through" reasoning <c>Research.ResearchManager.SetProjectFinishedForSetup</c> already applies to a
        /// scenario's starting era — so they get a plausible already-established size via
        /// <see cref="SettlementFounder.FoundColony"/> rather than a freshly-rolled founding band. SimWorld's
        /// own judgement call: RimWorld's <c>Settlement</c> carries no population at all to source a number
        /// from, and nothing yet models a civilization's real history well enough to compute one. Deliberately
        /// well above <see cref="FoundingBandRange"/> — small enough that a whole starting civilization is not
        /// implausibly vast, large enough to read as "already living there" rather than "just arrived".
        /// </summary>
        public static readonly IntRange EstablishedColonyPopulationRange = new IntRange(80, 400);
    }
}
