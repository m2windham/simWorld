using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="GraveyardInitiative"/> (translation: the settlement plays the RimWorld player who
    /// digs graves for the colony's dead and lays a dumping stockpile for everyone else's). RimWorld has
    /// nothing to port here — its player places every grave and zone by hand — so every figure below is
    /// SimWorld's own, documented at its declaration and pinned by <c>GraveyardInitiativeTests</c> as a relation
    /// (rows, clearance, one dump) rather than trusted as a bare literal, per CLAUDE.md's rule for untraceable
    /// numbers.
    /// </summary>
    public static class GraveyardTuning
    {
        /// <summary>Self-gate cadence: the rare bucket, like every other settlement initiative. A body is not
        /// an emergency measured in ticks, and a settlement with none to bury pays one modulo.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>Graves one gated pass may place, so a battle that left a dozen citizens dead does not queue
        /// a dozen blueprints in one instant. The remainder waits for the next pass.</summary>
        public const int MaxGravesPerPass = 2;

        /// <summary>How many free slots the first grave's neighbourhood (the block of slots two either side) must
        /// have for a graveyard to be begun there: a graveyard started in a one-slot nook has nowhere to grow.
        /// A cramped valley that cannot offer this many anywhere falls back to the best nook there is.</summary>
        public const int RoomToGrow = 6;

        /// <summary>Cells between one grave slot and the next in a row: a grave is one cell wide, so this
        /// leaves a one-cell lane between graves that a person can walk down.</summary>
        public const int ColumnPitch = 2;

        /// <summary>Cells between one row of slots and the next: a grave is two cells deep (<c>Grave</c> is 1x2),
        /// so this leaves a one-cell lane between rows.</summary>
        public const int RowPitch = 3;

        /// <summary>Nearest a grave may be to any bed, built or planned. "Near the settlement but not among its
        /// beds": close enough to be the settlement's, far enough that nobody sleeps beside the dead.</summary>
        public const int BedClearance = 6;

        /// <summary>Nearest the dump may be to any bed. Further than a grave: a body in the dump is a body in
        /// plain view of anyone who looks (<c>Corpse.GiveObservedThought</c>), and a grave is not.</summary>
        public const int DumpBedClearance = 10;

        /// <summary>Nearest the dump may be to the road hub — where the founders stand and the streets meet, the
        /// nearest thing a settlement has to a main walking area.</summary>
        public const int HubClearance = 8;

        /// <summary>Nearest the dump may be to the graveyard.</summary>
        public const int DumpGraveClearance = 4;

        /// <summary>Cells of the dump when it is first laid (four wide, three deep). It grows by whole rows as
        /// strangers fall, up to <see cref="MaxDumpCells"/>.</summary>
        public const int DumpWidth = 4;

        public const int DumpHeight = 3;

        /// <summary>Most cells the dump will grow to. A bound, so a settlement that is raided to death over and
        /// over does not pave its map with bodies it has nowhere better to put.</summary>
        public const int MaxDumpCells = 48;

        /// <summary>Half-width of the first square a site is searched in, round the settlement's middle; it
        /// doubles until it covers the map.</summary>
        public const int SearchRadius = 24;

        /// <summary>The dump zone's label — how the settlement recognises its own (the granary is found the same
        /// way, by <c>SettlementStockTuning.GranaryLabel</c>).</summary>
        public const string DumpLabel = "Dumping stockpile";
    }
}
