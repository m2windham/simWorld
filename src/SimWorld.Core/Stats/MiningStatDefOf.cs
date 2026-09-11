using SimWorld.Defs;

namespace SimWorld.Stats
{
    /// <summary>
    /// The mining yield stat (RimWorld: <c>RimWorld.StatDefOf.MiningYield</c>), in its own <c>[DefOf]</c> class
    /// rather than another field on <see cref="StatDefOf"/> — <see cref="DefOfAttribute"/> binding scans every
    /// marked type, so an additive binding needs no edit to a shared file (CLAUDE.md).
    /// </summary>
    [DefOf]
    public static class MiningStatDefOf
    {
        /// <summary>
        /// How much of a mined vein the miner actually recovers, skill-need-scaled off Mining
        /// (RimWorld: <c>StatDefOf.MiningYield</c>). Read by
        /// <see cref="SimWorld.Things.MineableUtility.YieldFor"/> for anything whose def says its yield is
        /// wasteable; see <c>Stats_Mining.xml</c> for what the curve is and is not sourced from.
        /// </summary>
        public static StatDef MiningYield = null!;
    }
}
