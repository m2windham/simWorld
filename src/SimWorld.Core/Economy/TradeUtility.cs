using System;
using SimWorld.Defs;
using SimWorld.Stats;

namespace SimWorld.Economy
{
    /// <summary>
    /// Pricing math (RimWorld: <c>RimWorld.TradeUtility</c> / <c>Tradeable.GetPriceFor</c>). RimWorld's own
    /// constants are private and version-dependent; this reproduces their shape — the player buys at a
    /// markup and sells at a discount off market value, both bent further by the good's <see cref="PriceType"/>
    /// and by negotiator/settlement price-gain factors — as a documented approximation rather than exact
    /// decompiled values. No faction-based price modifiers exist (RimWorld does not have any either).
    /// </summary>
    public static class TradeUtility
    {
        /// <summary>Approximation of RimWorld's buy-side markup (the player pays above market value).</summary>
        public const float BuyPriceFactor = 1.4f;

        /// <summary>Approximation of RimWorld's sell-side discount (the player receives below market value).</summary>
        public const float SellPriceFactor = 0.6f;

        /// <summary>However low negotiator/settlement gains push it, buying never drops below this fraction of market value.</summary>
        public const float MinBuyPriceFraction = 0.5f;

        /// <summary><c>ThingDef.statBases[MarketValue]</c>, or 0 when the def sets none (the stat's own <c>defaultBaseValue</c> is 0).</summary>
        public static float BaseMarketValue(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return def.GetStatValueAbstract(StatDefOf.MarketValue);
        }

        /// <summary>
        /// Price the player pays to buy one unit of <paramref name="def"/> (RimWorld: <c>Tradeable.GetPriceFor(TradeAction.PlayerBuys)</c>).
        /// <paramref name="priceGain_Negotiator"/>/<paramref name="priceGain_Settlement"/> are combined and capped at 100% off; the
        /// result is floored at <see cref="MinBuyPriceFraction"/> of market value so no combination of gains lets a good go for free.
        /// </summary>
        public static float GetPricePlayerBuy(
            ThingDef def,
            PriceType priceType = PriceType.Normal,
            float priceFactorBuy = 1f,
            float priceGain_Negotiator = 0f,
            float priceGain_Settlement = 0f)
        {
            float marketValue = BaseMarketValue(def);
            float price = marketValue * BuyPriceFactor * PriceTypeUtility.PriceMultiplier(priceType) * priceFactorBuy;
            float discount = GenMath.Clamp(priceGain_Negotiator + priceGain_Settlement, 0f, 1f);
            price *= 1f - discount;
            return Math.Max(price, marketValue * MinBuyPriceFraction);
        }

        /// <summary>
        /// Price the player receives to sell one unit of <paramref name="def"/> (RimWorld: <c>Tradeable.GetPriceFor(TradeAction.PlayerSells)</c>).
        /// </summary>
        public static float GetPricePlayerSell(
            ThingDef def,
            PriceType priceType = PriceType.Normal,
            float priceFactorSell = 1f,
            float priceGain_Negotiator = 0f,
            float priceGain_Settlement = 0f)
        {
            float marketValue = BaseMarketValue(def);
            float bonus = GenMath.Clamp(priceGain_Negotiator + priceGain_Settlement, 0f, 1f);
            return marketValue * SellPriceFactor * PriceTypeUtility.PriceMultiplier(priceType) * (1f + bonus) * priceFactorSell;
        }

        /// <summary>
        /// Approximation of RimWorld's negotiator Social-skill price improvement curve
        /// (<c>SkillDefOf.Social.EffectOn(StatDefOf.TradePriceImprovement)</c>): (0,0),(5,0.05),(10,0.10),(15,0.15),(20,0.20).
        /// </summary>
        public static readonly SimpleCurve NegotiatorGainFromSocialSkillCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(5f, 0.05f),
            new CurvePoint(10f, 0.10f),
            new CurvePoint(15f, 0.15f),
            new CurvePoint(20f, 0.20f),
        });

        public static float NegotiatorPriceGainFromSocialSkill(int socialSkillLevel) => NegotiatorGainFromSocialSkillCurve.Evaluate(socialSkillLevel);
    }
}
