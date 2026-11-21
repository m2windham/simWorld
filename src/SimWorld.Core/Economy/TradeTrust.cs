using System;

using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// Trade on reputation (Doug's decision, 2026-11-20): what the goodwill between a settlement's civilization
    /// and a caravan's says about trading with them, and how trading and gifting move it.
    ///
    /// <para/><b>Every reader goes through here.</b> The price a session charges
    /// (<see cref="VisitingTraderTrade.OpenSessionWith"/>), the refusal the command surface gives, the caravan
    /// draw (<c>Director.IncidentWorker_TraderCaravanArrival</c>) and the god view all ask <see cref="TierBetween"/>
    /// and read the same row of <see cref="TradeTrustTuning.Tiers"/>, so none of them can disagree about which
    /// band a relation is in. The numbers themselves live in <see cref="TradeTrustTuning"/> and nowhere else.
    /// </summary>
    public static class TradeTrust
    {
        /// <summary>
        /// The band <paramref name="ours"/> and <paramref name="theirs"/> stand in. A caravan of no civilization,
        /// a settlement of none, or the two being the same civilization is <see cref="TradeTrustTier.Neutral"/>:
        /// there is no relation to read, and RimWorld's own factionless traders trade at plain prices.
        /// </summary>
        public static TradeTrustTier TierBetween(Faction? ours, Faction? theirs)
        {
            if (ours == null || theirs == null || ReferenceEquals(ours, theirs)) return TradeTrustTier.Neutral;
            if (ours.WarWith(theirs) || ours.HostileTo(theirs)) return TradeTrustTier.Hostile;

            int goodwill = ours.GoodwillWith(theirs);
            if (goodwill < TradeTrustTuning.WaryBelowGoodwill) return TradeTrustTier.Wary;
            if (goodwill < TradeTrustTuning.TrustedFromGoodwill) return TradeTrustTier.Neutral;
            return TradeTrustTier.Trusted;
        }

        public static TradeTrustTierRow Row(TradeTrustTier tier) => TradeTrustTuning.Tiers[(int)tier];

        /// <summary>The buy-side multiplier a session applies in <paramref name="tier"/>.</summary>
        public static float BuyPriceFactor(TradeTrustTier tier) => Row(tier).PriceFactor;

        /// <summary>The sell-side multiplier: the reciprocal of <see cref="BuyPriceFactor"/>, so a better tier
        /// both buys cheaper and sells dearer by the same proportion.</summary>
        public static float SellPriceFactor(TradeTrustTier tier) => 1f / Row(tier).PriceFactor;

        // ---- trade ----

        /// <summary>
        /// Goodwill a completed trade worth <paramref name="silverValue"/> would earn with
        /// <paramref name="theirs"/> right now, without recording it. The same arithmetic
        /// <see cref="RecordTrade"/> applies. Zero for a pair with no relation to move.
        /// </summary>
        public static int TradeGoodwillGain(Faction? ours, Faction? theirs, float silverValue)
        {
            if (!CanMoveGoodwill(ours, theirs) || silverValue <= 0f) return 0;
            FactionRelation rel = ours!.GetOrAddRelation(theirs!);
            return GainOnTopOf(PooledToday(rel), silverValue);
        }

        /// <summary>
        /// Books a completed trade worth <paramref name="silverValue"/> against today's pool and raises goodwill
        /// by what it earned (simWorld's own; RimWorld: trading moves no goodwill). Returns the goodwill gained.
        /// </summary>
        public static int RecordTrade(Faction? ours, Faction? theirs, float silverValue)
        {
            if (!CanMoveGoodwill(ours, theirs) || silverValue <= 0f) return 0;

            FactionRelation rel = ours!.GetOrAddRelation(theirs!);
            float pooled = PooledToday(rel);
            int gain = GainOnTopOf(pooled, silverValue);

            rel.tradeTrustDay = Find.TickManager.TicksGame / TradeTrustTuning.TradeDayTicks;
            rel.tradeTrustValue = pooled + silverValue;

            if (gain <= 0) return 0;
            int before = ours.GoodwillWith(theirs!);
            ours.TryAffectGoodwillWith(theirs!, gain, canSendMessage: false, canSendHostilityLetter: false, reason: "Trade");
            return ours.GoodwillWith(theirs!) - before;
        }

        private static float PooledToday(FactionRelation rel)
        {
            int today = Find.TickManager.TicksGame / TradeTrustTuning.TradeDayTicks;
            return rel.tradeTrustDay == today ? rel.tradeTrustValue : 0f;
        }

        private static int GainOnTopOf(float pooled, float added)
        {
            int cap = TradeTrustTuning.TradeGoodwillDailyCap;
            int before = Math.Min(cap, (int)(pooled / TradeTrustTuning.TradeValuePerGoodwill));
            int after = Math.Min(cap, (int)((pooled + added) / TradeTrustTuning.TradeValuePerGoodwill));
            return after - before;
        }

        // ---- gifts ----

        /// <summary>
        /// Goodwill a gift of market value <paramref name="giftValue"/> earns with <paramref name="theirs"/>
        /// (RimWorld: <c>FactionGiftUtility.GetGoodwillChange</c> — value / 40, then bent by
        /// <see cref="TradeTrustTuning.GiftGoodwillFactorCurve"/> in 0.25-point steps; recalled, not read here).
        /// <para/>One deliberate difference: RimWorld caps the result at 200 and leaves the [-100, 100] clamp to
        /// <c>TryAffectGoodwillWith</c>; this stops at the goodwill actually left to gain, so the number the god
        /// view quotes is the number that lands. The goodwill that results is identical either way.
        /// </summary>
        public static int GiftGoodwillChange(Faction? ours, Faction? theirs, float giftValue)
        {
            if (!CanMoveGoodwill(ours, theirs) || giftValue <= 0f) return 0;

            float current = ours!.GoodwillWith(theirs!);
            float room = 100f - current;
            float remaining = giftValue / TradeTrustTuning.GiftValuePerGoodwill;
            float gained = 0f;
            float step = TradeTrustTuning.GiftGoodwillStep;
            while (remaining >= step && gained < room)
            {
                gained += step * TradeTrustTuning.GiftGoodwillFactorCurve.Evaluate(Math.Min(current + gained, 100f));
                remaining -= step;
            }
            if (gained < room)
            {
                gained += remaining * TradeTrustTuning.GiftGoodwillFactorCurve.Evaluate(Math.Min(current + gained, 100f));
            }
            return (int)Math.Min(gained, room);
        }

        /// <summary>Applies <see cref="GiftGoodwillChange"/>. Returns the goodwill gained.</summary>
        public static int RecordGift(Faction? ours, Faction? theirs, float giftValue)
        {
            int gain = GiftGoodwillChange(ours, theirs, giftValue);
            if (gain <= 0) return 0;
            int before = ours!.GoodwillWith(theirs!);
            ours.TryAffectGoodwillWith(theirs!, gain, canSendMessage: false, canSendHostilityLetter: false, reason: "Gift");
            return ours.GoodwillWith(theirs!) - before;
        }

        /// <summary>Two different civilizations neither of which is a permanent enemy — the pairs
        /// <see cref="Faction.TryAffectGoodwillWith"/> will actually move.</summary>
        private static bool CanMoveGoodwill(Faction? ours, Faction? theirs) =>
            ours != null && theirs != null && !ReferenceEquals(ours, theirs)
            && !ours.def.permanentEnemy && !theirs.def.permanentEnemy;
    }
}
