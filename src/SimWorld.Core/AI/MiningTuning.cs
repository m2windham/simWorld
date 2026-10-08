using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// The two numbers behind <see cref="MiningInitiative"/> (system: mining). RimWorld has nothing to source
    /// them from: its player marks every cell by hand, so "how much rock does a settlement decide to dig"
    /// has no constant to port. Both figures are therefore this port's own design numbers, and are marked as
    /// such here and pinned by the relations below — never by the literals — in
    /// <c>MiningInitiativeTests</c>.
    /// </summary>
    public static class MiningTuning
    {
        /// <summary>
        /// Self-gate cadence, the same <see cref="GenTicks.TickRareInterval"/> the construction, stonecutting
        /// and works initiatives use, for the reason they give: deciders that act on the same map should
        /// not run on different clocks, or which of them notices a change first depends on the tick number.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// <b>Design number.</b> The most cells a settlement will have marked for digging at once, the
        /// player's marks counted with its own. It is the whole of the guarantee that a settlement never
        /// digs out a mountain: however much it wants, it can only ever be part-way through this many
        /// cells, and it marks more only as these are mined out.
        ///
        /// <para/>Chosen against the maps this port generates, not against a feeling. Measured on two
        /// generated interiors (seeds <c>777</c> and <c>roof-a</c>, 200×200, about 13,000 rock cells each), the
        /// shortest dig from open ground to an ore vein is 1 to 15 cells, and a stone face of a hundred cells
        /// is a handful of percent of one. Twenty-four holds the longest dig seen and leaves room for a second
        /// working beside it, and is under 0.2% of the rock on those maps. <c>MiningInitiativeTests</c> pins
        /// the relations (it holds every vein's dig on generated maps; it is a sliver of a mountain), so a
        /// retune that breaks either fails there instead of silently starving ore or inviting a quarry the
        /// size of the map.
        /// </summary>
        public const int MaxOutstandingDesignations = 24;

        /// <summary>
        /// <b>Design number.</b> How much of each ore the settlement wants in hand over and above whatever its
        /// building sites are waiting for. A settlement keeps a stock of the ores it can spend and of the
        /// trade currency, because they are scarce (one vein is 20 to 60 units and a whole map has a dozen)
        /// and a build that needs steel should not have to wait for a citizen to dig a tunnel first.
        ///
        /// <para/>Pinned as a relation: it covers the dearest single build any ore is spent on in content
        /// (75 steel today), so the reserve can always afford one of whatever it is a reserve for. The
        /// settlement stops digging an ore once it has this much, counting the books
        /// (<c>Settlement.Stores</c>) as well as what lies on the ground.
        /// </summary>
        public const int OreReserve = 100;
    }
}
