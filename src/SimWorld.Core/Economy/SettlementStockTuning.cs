using SimWorld.Building;
using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// Tuning for <see cref="SettlementStockInitiative"/> — the seam between what a settlement's map holds and
    /// what its civilization owns. RimWorld has nothing to port here: its colony's stock <i>is</i> the things
    /// lying on the one map, so it never needed a second ledger and never needed a rule for moving between
    /// them. Every figure below is therefore SimWorld's own, and every one is derived from a number this
    /// codebase already committed to rather than chosen — see each declaration, and see
    /// <c>SettlementStockTests</c>, which pins each as a band or a trend rather than as the literal.
    /// </summary>
    public static class SettlementStockTuning
    {
        /// <summary>
        /// Self-gate cadence. The same <see cref="GenTicks.TickRareInterval"/> the three settlement-scale
        /// deciders this one stands beside already use (<c>Building.ConstructionInitiativeTuning.IntervalTicks</c>,
        /// <c>Building.WorksInitiativeTuning.IntervalTicks</c>, <c>Crafting.StonecuttingTuning.IntervalTicks</c>),
        /// and for the reason those two state for each other: settlement-scale deciders reading the same map
        /// on different clocks only make which of them noticed a change first depend on the tick number.
        /// <para/>
        /// It also has to divide <c>Crafting.GuildTuning.GuildIntervalTicks</c> (2,000), because a guild
        /// spends what this banks: 2,000 / 250 = 8, so the ledger is always current on the tick a guild reads
        /// it, and the goods a settlement hauled in never sit a whole guild-interval behind.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// Ceiling on how many cells one gated pass may add to a settlement's granary. The <i>size</i> of a
        /// granary is not a constant at all — it is the settlement's own backlog, one cell per stack of goods
        /// that is lying out with nowhere to go (see <see cref="SettlementStockInitiative.EnsureGranary"/>),
        /// which is derived from live state and self-limiting: once everything can be stored, it stops
        /// growing. This is only the "grows over many ticks, not instantly" bound every other settlement-scale
        /// decider here applies to itself (<c>ConstructionInitiativeTuning.MaxBlueprintsPerTick</c>,
        /// <c>Settlement.GrowStatisticalCohort</c>).
        /// <para/>
        /// Derived from <see cref="ConstructionInitiativeTuning.GoodsPerStorageHut"/>, this codebase's
        /// existing unit of "how much a settlement keeps in one place" — one pass may paint at most one
        /// storage hut's worth. Reading that figure as <i>cells</i> rather than as units is the deliberately
        /// conservative half of the derivation: a cell holds a whole stack (up to
        /// <c>ThingDef.stackLimit</c> units), so this over-provisions a pass rather than under-provisioning
        /// it, and the backlog rule bounds the total either way.
        /// </summary>
        public const int MaxGranaryCellsPerPass = ConstructionInitiativeTuning.GoodsPerStorageHut;

        /// <summary>
        /// The label the settlement's own stockpile carries, so a later pass extends the granary it already
        /// painted instead of painting a second one beside it. Banking drains <i>every</i> stockpile on the
        /// interior, labelled or not — what makes goods the settlement's stock is resting in storage, not
        /// which zone object happens to hold the cell.
        /// </summary>
        public const string GranaryLabel = "Granary";
    }
}
