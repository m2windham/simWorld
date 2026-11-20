using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// How much a civilization trusts a trading partner, read straight off the one goodwill number the two
    /// already share (<see cref="Factions.Faction.GoodwillWith"/>), sorted into four bands.
    ///
    /// <para/><b>Not a second number.</b> Trust is faction goodwill: the same value diplomacy reads, the same
    /// value <see cref="Factions.Faction.FactionTick"/>'s natural drift pulls toward
    /// <see cref="Factions.FactionDef.naturalColonyGoodwill"/>, and the same value a war declaration drops to the
    /// floor. This enum only names the bands <see cref="TradeTrustTuning.Tiers"/> cuts it into.
    /// </summary>
    public enum TradeTrustTier
    {
        /// <summary>At war, or goodwill in RimWorld's Hostile band (≤ -75). No trade, and no caravans.</summary>
        Hostile,

        /// <summary>Not hostile, but goodwill below zero. Trades at a worse price.</summary>
        Wary,

        /// <summary>Goodwill 0 to 74. Prices exactly as <see cref="TradeUtility"/> sets them.</summary>
        Neutral,

        /// <summary>Goodwill at or above 75, RimWorld's Ally threshold. Better prices, and their caravans come more often.</summary>
        Trusted,
    }

    /// <summary>One row of <see cref="TradeTrustTuning.Tiers"/>.</summary>
    public readonly struct TradeTrustTierRow
    {
        public TradeTrustTierRow(TradeTrustTier tier, string label, bool allowsTrade, float priceFactor, int arrivalWeight)
        {
            Tier = tier;
            Label = label;
            AllowsTrade = allowsTrade;
            PriceFactor = priceFactor;
            ArrivalWeight = arrivalWeight;
        }

        public TradeTrustTier Tier { get; }

        /// <summary>What the god view shows. Also the tier's handle across the seam, which carries no enums.</summary>
        public string Label { get; }

        /// <summary>False refuses buying and selling outright (gifts are still accepted — see
        /// <see cref="TradeTrust.GiftGoodwillChange"/>).</summary>
        public bool AllowsTrade { get; }

        /// <summary>Multiplies the buy price and divides the sell price — both through
        /// <see cref="TradeUtility"/>'s own <c>priceFactorBuy</c>/<c>priceFactorSell</c> parameters, so it rides
        /// the one price expression rather than sitting beside it. Above 1 is worse for us, below 1 better.</summary>
        public float PriceFactor { get; }

        /// <summary>How many times this faction is entered in the draw for who sends the next trade caravan.
        /// Zero keeps them out of it.</summary>
        public int ArrivalWeight { get; }
    }

    /// <summary>
    /// The trust table: every number the trade-trust system turns on, in one place.
    ///
    /// <para/><b>Doug's decision (2026-11-20): trade runs on reputation.</b> RimWorld has no goodwill-based
    /// prices at all — <see cref="TradeUtility"/>'s own doc already says so — and its only goodwill effect on
    /// trade is that a hostile faction will neither trade nor send caravans. So every price and arrival number
    /// below is simWorld's own, and each one says what RimWorld does in its place. The gift numbers are the
    /// exception: they are RimWorld's <c>FactionGiftUtility</c>, recalled (not read from decompiled source in
    /// this environment — no RimWorld assembly is available here), and marked so.
    /// </summary>
    public static class TradeTrustTuning
    {
        /// <summary>Goodwill below this is <see cref="TradeTrustTier.Wary"/>. simWorld's own; RimWorld: no such
        /// band — any non-hostile faction trades at the same price.</summary>
        public const int WaryBelowGoodwill = 0;

        /// <summary>Goodwill at or above this is <see cref="TradeTrustTier.Trusted"/>. simWorld's own band, put on
        /// RimWorld's own Ally threshold (<c>Faction.KindFromGoodwill</c>, 75) so "trusted" and "ally" agree;
        /// RimWorld: being an ally changes no price.</summary>
        public const int TrustedFromGoodwill = 75;

        /// <summary>
        /// The tier table, in <see cref="TradeTrustTier"/> order.
        /// <list type="table">
        /// <item><term>Hostile</term><description>no trade, arrival weight 0. RimWorld: hostile factions
        /// (goodwill ≤ -75) will not trade and are never picked to send a trader caravan
        /// (<c>IncidentWorker_TraderCaravanArrival</c> requires a non-hostile faction). simWorld adds "at war"
        /// to it, since war is a separate state here (<c>Factions.WarState</c>). The price factor is never
        /// charged and is 1 only so the row is complete.</description></item>
        /// <item><term>Wary</term><description>price factor 1.15 (buy ×1.15, sell ÷1.15), weight 1.
        /// simWorld's own; RimWorld: factor 1.</description></item>
        /// <item><term>Neutral</term><description>factor 1, weight 1. Matches RimWorld.</description></item>
        /// <item><term>Trusted</term><description>factor 0.9 (buy ×0.9, sell ÷0.9), weight 2. simWorld's own;
        /// RimWorld: factor 1, and every eligible faction is equally likely to send the next caravan.</description></item>
        /// </list>
        /// </summary>
        public static readonly TradeTrustTierRow[] Tiers =
        {
            new TradeTrustTierRow(TradeTrustTier.Hostile, "Hostile", allowsTrade: false, priceFactor: 1f, arrivalWeight: 0),
            new TradeTrustTierRow(TradeTrustTier.Wary, "Wary", allowsTrade: true, priceFactor: 1.15f, arrivalWeight: 1),
            new TradeTrustTierRow(TradeTrustTier.Neutral, "Neutral", allowsTrade: true, priceFactor: 1f, arrivalWeight: 1),
            new TradeTrustTierRow(TradeTrustTier.Trusted, "Trusted", allowsTrade: true, priceFactor: 0.9f, arrivalWeight: 2),
        };

        /// <summary>Silver of completed trade per goodwill point. simWorld's own; RimWorld: trading raises no
        /// goodwill at all. Five times dearer than a gift (<see cref="GiftValuePerGoodwill"/>), because a trade
        /// is paid for and a gift is not.</summary>
        public const float TradeValuePerGoodwill = 200f;

        /// <summary>Most goodwill trade can earn with one faction in one in-game day, however it is split up —
        /// the value is pooled across the day before it is divided, so a hundred one-unit trades earn exactly
        /// what one hundred-unit trade does, and neither earns past this. simWorld's own (RimWorld: n/a). Set
        /// against the natural drift of 0.5/day (<c>Factions.xml</c> <c>goodwillDailyFall</c>): steady trade
        /// can out-run it, but only steadily.</summary>
        public const int TradeGoodwillDailyCap = 2;

        /// <summary>The day a trade counts toward, for <see cref="TradeGoodwillDailyCap"/>.</summary>
        public const int TradeDayTicks = GenDate.TicksPerDay;

        /// <summary>Market value of a gift per goodwill point. RimWorld: <c>FactionGiftUtility</c>'s
        /// <c>GetBaseGoodwillChange</c> divides the gift's value by 40 (recalled, not read here).</summary>
        public const float GiftValuePerGoodwill = 40f;

        /// <summary>
        /// How much of a gift's base goodwill lands, by the goodwill it is landing on. RimWorld:
        /// <c>FactionGiftUtility.GiftGoodwillFactorRelationsCurve</c>, (-100,1),(75,1),(100,0.25) — full effect
        /// until allied, then diminishing — applied in 0.25-goodwill steps by <c>PostProcessedGoodwillChange</c>
        /// (recalled, not read here).
        /// </summary>
        public static readonly SimpleCurve GiftGoodwillFactorCurve = new SimpleCurve(new[]
        {
            new CurvePoint(-100f, 1f),
            new CurvePoint(75f, 1f),
            new CurvePoint(100f, 0.25f),
        });

        /// <summary>The step <see cref="GiftGoodwillFactorCurve"/> is applied in. RimWorld's own 0.25 (recalled).</summary>
        public const float GiftGoodwillStep = 0.25f;
    }
}
