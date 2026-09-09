using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="SettlementConstructionInitiative"/> (translation: citizen-initiated construction
    /// under edicts, <c>docs/status.json</c>'s <c>building.initiative</c>). RimWorld has nothing to port here
    /// — its player places every blueprint by hand, so there is no "how much does a settlement build on its
    /// own" constant to source — every figure below is SimWorld's own, documented at its declaration and
    /// pinned by <c>ConstructionInitiativeTests</c> as a band or a trend (more citizens → more beds wanted;
    /// more stored goods → more storage wanted) rather than trusted as a bare literal, per CLAUDE.md's own
    /// rule for untraceable numbers.
    /// </summary>
    public static class ConstructionInitiativeTuning
    {
        /// <summary>
        /// Self-gate cadence for <see cref="SettlementConstructionInitiative.TickSettlement(World.Settlement, Map.Map)"/>
        /// — the "rare/long bucket, not a per-tick scan" every civilization-scale process in this codebase
        /// uses (see <c>GodTuning.GodTickIntervalTicks</c>'s own doc for the sibling reasoning). Reuses
        /// <see cref="GenTicks.TickRareInterval"/> rather than the long bucket <c>GodManager</c> uses: a
        /// settlement sitting on an unmet need should not wait a whole god-tick's worth of real time before
        /// its very first blueprint appears.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// How many blueprints one gated call to <c>TickSettlement</c> may place, summed across every need
        /// category. Keeps a settlement with a huge shortfall (a newly-founded town whose citizens include a
        /// large Statistical cohort, say) from flooding its own map with every blueprint it will ever need in
        /// a single pass; the remainder simply waits for the next gated tick — the same "grows over many
        /// ticks, not instantly" shape <c>Settlement.GrowStatisticalCohort</c> already uses for population.
        /// </summary>
        public const int MaxBlueprintsPerTick = 3;

        /// <summary>
        /// Random cells tried before giving up on placing one more unit of a given need this tick.
        /// <see cref="GenConstruct.CanPlaceBlueprintAt"/> is a handful of grid lookups, not a full-map scan,
        /// so a bounded number of misses is far cheaper than scanning every cell on a large map; a map that
        /// genuinely has no room left simply carries its shortfall forward to the next gated tick.
        /// </summary>
        public const int MaxPlacementAttempts = 40;

        /// <summary>
        /// Wall blueprints wanted per citizen once a settlement has any citizens at all — SimWorld's own
        /// figure (RimWorld has no autonomous-shelter concept to source one from): enough that a growing
        /// settlement keeps building outward, clamped by <see cref="MinWallShelterCount"/>/
        /// <see cref="MaxWallShelterCount"/> so a single very large settlement does not try to wall off its
        /// entire population's worth of perimeter in one civilization-scale pass.
        /// </summary>
        public const float WallsPerCitizen = 2f;

        /// <summary>Minimum wall target once a settlement has at least one citizen — a token starter
        /// enclosure rather than zero, even for a lone founder.</summary>
        public const int MinWallShelterCount = 4;

        /// <summary>Ceiling on the wall target regardless of population, so <see cref="WallsPerCitizen"/>
        /// scaling does not ask a large settlement to wall in its entire population in one go.</summary>
        public const int MaxWallShelterCount = 40;

        /// <summary>
        /// Stored-goods units one <c>StorageHut</c> is assumed to hold before the settlement wants another —
        /// SimWorld's own figure (<c>Settlement.Stores</c> is a bare def→count ledger with no per-item volume
        /// or quality to size a real hut against). Pinned by <c>ConstructionInitiativeTests</c> as "more
        /// stores asks for more huts," never by this literal.
        /// </summary>
        public const int GoodsPerStorageHut = 50;
    }
}
