using SimWorld.Sim;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Tuning for <see cref="StonecutterInitiative"/> (system: stonework — rock to usable material).
    /// RimWorld has nothing to port here: its player adds the bill and places the bench by hand, so there is
    /// no "how much stone does a settlement decide to cut" constant to source. There are deliberately only
    /// two figures below, and neither is a demand number — how many blocks the settlement wants is derived
    /// from the walls it has already decided it needs
    /// (<see cref="Building.StoneWallMaterials.BlocksWantedOf"/>), not invented here.
    ///
    /// <para/><b>What the civilization scale actually asks of these numbers.</b> Worth writing down, because
    /// the obvious worry — a recipe tuned for five colonists being absurd for a thousand citizens — turns out
    /// to point the other way. One mason at skill 0 applies
    /// <see cref="JobDriver_DoBill.BaseWorkPerTick"/> × 0.4 work per tick, so RimWorld's own 1,600-work
    /// stonecutting batch takes about 4,000 ticks and yields 20 blocks: at
    /// <see cref="GenDate.TicksPerDay"/> = 60,000 that is a fifteenth of a day for four walls' worth of stone.
    /// The settlement's entire wall demand — <c>ConstructionInitiativeTuning.MaxWallShelterCount</c> walls at
    /// five blocks each — is under a day of one mason's time. So the binding constraint at any settlement size
    /// is chunk supply (natural rock drops a chunk on a tenth of the cells mined out, per
    /// <c>Buildings_Natural.xml</c>), never bench throughput, and scaling the bench count with population
    /// would be a constant invented to solve a shortage that the arithmetic says does not exist. If wall
    /// demand ever grows past a cap of forty, this is the assumption to revisit first.
    /// </summary>
    public static class StonecuttingTuning
    {
        /// <summary>
        /// Self-gate cadence, matching <c>Building.ConstructionInitiativeTuning.IntervalTicks</c>: this
        /// initiative's whole job is to keep a bench and a bill in existence alongside the blueprints that one
        /// places, and two settlement-scale deciders running on different clocks would only make which of them
        /// noticed a change first depend on the tick number.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// Stonecutter's tables a settlement wants. One, and see this class's own remarks for the arithmetic
        /// that says one is enough: <see cref="WorkGiver_DoBill"/> reserves the bench it works, so this really
        /// is one mason at a time, and one mason at a time still outruns what the settlement can find to build.
        /// </summary>
        public const int TablesWanted = 1;

        /// <summary>
        /// How far from a chunk a new bench may be placed, in cells. The only figure here with no derivation
        /// behind it, and it is bounded rather than chosen: it must stay well inside
        /// <see cref="WorkGiver_DoBill.IngredientSearchRadius"/> (12), because that giver only offers a bill
        /// whose ingredients already lie within that radius of the bench — a stonecutter's table built any
        /// further from the stone is a table nobody can ever work. <c>StonecuttingTests</c> pins the relation
        /// to that radius, not this number.
        /// </summary>
        public const int BenchPlacementRadius = 5;
    }
}
