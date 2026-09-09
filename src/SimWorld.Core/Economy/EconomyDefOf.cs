using SimWorld.Defs;

namespace SimWorld.Economy
{
    /// <summary>ThingDefs the Economy module reaches for by identity rather than by generic lookup, the same pattern MapGen/Crafting/Health etc. each use for their own module-scoped DefOf classes.</summary>
    [DefOf]
    public static class EconomyThingDefOf
    {
        /// <summary>The tradeable commodity <see cref="CoalSupply"/> prices — distinct from <see cref="SimWorld.World.DepositDefOf.Coal"/>, the terrain-derived deposit that decides where it is found (spec §5b.2).</summary>
        public static ThingDef Coal = null!;

        /// <summary>
        /// The trade currency (RimWorld: <c>ThingDefOf.Silver</c>). Priced at exactly 1 market value by its
        /// own content, same as RimWorld's Silver — so a currency-for-itself line never carries the buy/sell
        /// markup real goods do; see <see cref="TradeSession.SetupWith"/>, which routes this def straight
        /// into <see cref="TradeDeal.silverTradeable"/> instead of an ordinary priced <see cref="Tradeable"/>.
        /// </summary>
        public static ThingDef Silver = null!;
    }
}
