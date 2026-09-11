namespace SimWorld.Defs
{
    /// <summary>
    /// Mining half of <see cref="ThingDef"/> (RimWorld: the <c>mineable*</c> block of
    /// <c>Verse.BuildingProperties</c>, which this port keeps directly on <see cref="ThingDef"/> —
    /// <c>mineable</c>, <c>mineableThing</c> and <c>mineableYield</c> already live on
    /// <c>ThingDef.Things.cs</c> beside the rest of the map-occupancy fields).
    /// <para/>
    /// Its own partial rather than three more lines on that file, per CLAUDE.md: a new file cannot conflict
    /// with the lanes editing the shared one.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>
        /// Chance that mining this out drops <c>mineableThing</c> at all (RimWorld:
        /// <c>BuildingProperties.mineableDropChance</c>). An ore vein leaves this at 1 — a vein always pays —
        /// while plain rock sets it low, which is how RimWorld gets "a tunnel through the mountain leaves a
        /// scattering of chunks behind, not one per cell". <b>RimWorld's own number for rock could not be
        /// sourced here</b>; the shipped value is this port's own and the tests pin the band (some rock cells
        /// drop, not all of them), never the literal.
        /// </summary>
        public float mineableDropChance = 1f;

        /// <summary>
        /// Whether an unskilled miner wastes part of the yield (RimWorld:
        /// <c>BuildingProperties.mineableYieldWasteable</c>). True on ore, where the miner's
        /// <c>MiningYield</c> stat scales the amount; false on rock, where one chunk is one chunk however good
        /// the miner is. <b>RimWorld's own default for this flag could not be sourced here</b> — this port
        /// defaults it on, so a vein that says nothing is wasteable, and rock turns it off explicitly.
        /// </summary>
        public bool mineableYieldWasteable = true;

        /// <summary>
        /// Relative weight for being chosen when map generation places a vein inside natural rock (RimWorld:
        /// <c>BuildingProperties.mineableScatterCommonality</c>, read by its <c>GenStep_ScatterLumpsMineable</c>).
        /// Zero — the default — means map generation never places this one, which is the right answer for
        /// plain rock: rock is placed as the mountain itself, not scattered into it. Read by
        /// <see cref="SimWorld.Things.MineableUtility.RandomVeinDef"/>.
        /// <para/>
        /// RimWorld's companion <c>mineableScatterLumpSizeRange</c> is deliberately not ported: this port's
        /// <c>GenStep_RocksAndMountains</c> places single-cell veins rather than lumps, so a size range would
        /// be content nothing reads — exactly the kind of seam the wiring audit exists to catch.
        /// </summary>
        public float mineableScatterCommonality;
    }
}
