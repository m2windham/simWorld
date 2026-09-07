using SimWorld.Defs;

namespace SimWorld.Economy
{
    [DefOf]
    public static class EconomyDefOf
    {
        /// <summary>The stat <see cref="TradeUtility.BaseMarketValue"/> reads from a ThingDef's <c>statBases</c>.</summary>
        public static StatDef MarketValue = null!;
    }
}
