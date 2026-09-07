using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Economy
{
    public class TradeTests : ContentTestBase
    {
        public TradeTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef GoodsDef(float marketValue) => new ThingDef
        {
            defName = "TestGoods",
            statBases = new List<StatModifier> { new StatModifier(StatDefOf.MarketValue, marketValue) },
        };

        // ---- MarketValue / BaseMarketValue ----

        [Fact]
        public void BaseMarketValue_reads_the_statBases_entry()
        {
            ThingDef def = GoodsDef(25f);
            Assert.Equal(25f, TradeUtility.BaseMarketValue(def));
        }

        [Fact]
        public void BaseMarketValue_is_zero_when_the_def_sets_none()
        {
            var def = new ThingDef { defName = "NoValue" };
            Assert.Equal(0f, TradeUtility.BaseMarketValue(def));
        }

        // ---- price types ----

        [Theory]
        [InlineData(PriceType.VeryCheap, 0.4f)]
        [InlineData(PriceType.Cheap, 0.7f)]
        [InlineData(PriceType.Normal, 1f)]
        [InlineData(PriceType.Expensive, 2f)]
        [InlineData(PriceType.Exorbitant, 5f)]
        public void PriceTypeUtility_multipliers_match_the_documented_table(PriceType type, float expected)
        {
            Assert.Equal(expected, PriceTypeUtility.PriceMultiplier(type));
        }

        // ---- buy/sell pricing ----

        [Fact]
        public void Buy_price_is_above_market_value_and_scales_with_price_type()
        {
            ThingDef def = GoodsDef(10f);
            float normal = TradeUtility.GetPricePlayerBuy(def);
            float expensive = TradeUtility.GetPricePlayerBuy(def, PriceType.Expensive);

            Assert.Equal(10f * TradeUtility.BuyPriceFactor, normal, 3);
            Assert.True(normal > 10f, "Buying should cost more than raw market value.");
            Assert.Equal(normal * 2f, expensive, 3);
        }

        [Fact]
        public void Sell_price_is_below_market_value_and_scales_with_price_type()
        {
            ThingDef def = GoodsDef(10f);
            float normal = TradeUtility.GetPricePlayerSell(def);
            float cheap = TradeUtility.GetPricePlayerSell(def, PriceType.Cheap);

            Assert.Equal(10f * TradeUtility.SellPriceFactor, normal, 3);
            Assert.True(normal < 10f, "Selling should net less than raw market value.");
            Assert.Equal(normal * 0.7f, cheap, 3);
        }

        [Fact]
        public void Negotiator_gain_lowers_buy_price_and_raises_sell_price()
        {
            ThingDef def = GoodsDef(100f);
            float buyNoGain = TradeUtility.GetPricePlayerBuy(def);
            float buyWithGain = TradeUtility.GetPricePlayerBuy(def, priceGain_Negotiator: 0.1f);
            float sellNoGain = TradeUtility.GetPricePlayerSell(def);
            float sellWithGain = TradeUtility.GetPricePlayerSell(def, priceGain_Negotiator: 0.1f);

            Assert.True(buyWithGain < buyNoGain);
            Assert.True(sellWithGain > sellNoGain);
        }

        [Fact]
        public void Buy_price_is_floored_at_half_market_value_however_large_the_gains()
        {
            ThingDef def = GoodsDef(50f);
            float price = TradeUtility.GetPricePlayerBuy(def, priceGain_Negotiator: 5f, priceGain_Settlement: 5f);
            Assert.Equal(50f * TradeUtility.MinBuyPriceFraction, price, 3);
        }

        [Fact]
        public void Negotiator_price_gain_curve_matches_documented_points()
        {
            Assert.Equal(0f, TradeUtility.NegotiatorPriceGainFromSocialSkill(0));
            Assert.Equal(0.05f, TradeUtility.NegotiatorPriceGainFromSocialSkill(5), 3);
            Assert.Equal(0.10f, TradeUtility.NegotiatorPriceGainFromSocialSkill(10), 3);
            Assert.Equal(0.20f, TradeUtility.NegotiatorPriceGainFromSocialSkill(20), 3);
        }

        // ---- TradeDeal ----

        [Fact]
        public void TradeDeal_executes_when_the_player_can_afford_it()
        {
            ThingDef silver = GoodsDef(1f);
            ThingDef goods = GoodsDef(10f);

            var deal = new TradeDeal();
            var silverLine = new Tradeable(silver, countOffered: 0, countInPlayer: 1000);
            var goodsLine = new Tradeable(goods, countOffered: 20, countInPlayer: 0) { countToTransfer = 5 };
            deal.silverTradeable = silverLine;
            deal.AddTradeable(goodsLine);

            float expectedCost = TradeUtility.GetPricePlayerBuy(goods) * 5;
            Assert.Equal(expectedCost, deal.NetPlayerSilverCost(), 2);

            bool result = deal.TryExecute(out bool actuallyTraded);

            Assert.True(result);
            Assert.True(actuallyTraded);
            Assert.Equal(5, goodsLine.countInPlayer);
            Assert.Equal(15, goodsLine.countOffered);
            Assert.Equal(0, goodsLine.countToTransfer);
            Assert.Equal(1000 - (int)System.Math.Round(expectedCost), silverLine.countInPlayer);
        }

        [Fact]
        public void TradeDeal_refuses_when_the_player_cannot_afford_it()
        {
            ThingDef silver = GoodsDef(1f);
            ThingDef goods = GoodsDef(10f);

            var deal = new TradeDeal();
            var silverLine = new Tradeable(silver, countOffered: 0, countInPlayer: 2);
            var goodsLine = new Tradeable(goods, countOffered: 20, countInPlayer: 0) { countToTransfer = 5 };
            deal.silverTradeable = silverLine;
            deal.AddTradeable(goodsLine);

            bool result = deal.TryExecute(out bool actuallyTraded);

            Assert.False(result);
            Assert.False(actuallyTraded);
            // Nothing moved.
            Assert.Equal(0, goodsLine.countInPlayer);
            Assert.Equal(20, goodsLine.countOffered);
            Assert.Equal(5, goodsLine.countToTransfer);
            Assert.Equal(2, silverLine.countInPlayer);
        }

        [Fact]
        public void TradeDeal_counts_move_both_ways_for_a_mixed_buy_and_sell()
        {
            ThingDef silver = GoodsDef(1f);
            ThingDef bought = GoodsDef(10f);
            ThingDef sold = GoodsDef(4f);

            var deal = new TradeDeal();
            var silverLine = new Tradeable(silver, countOffered: 0, countInPlayer: 1000);
            var buyLine = new Tradeable(bought, countOffered: 10, countInPlayer: 0) { countToTransfer = 3 };
            var sellLine = new Tradeable(sold, countOffered: 0, countInPlayer: 6) { countToTransfer = -4 };
            deal.silverTradeable = silverLine;
            deal.AddTradeable(buyLine);
            deal.AddTradeable(sellLine);

            deal.TryExecute(out bool actuallyTraded);

            Assert.True(actuallyTraded);
            Assert.Equal(3, buyLine.countInPlayer);
            Assert.Equal(7, buyLine.countOffered);
            Assert.Equal(2, sellLine.countInPlayer);
            Assert.Equal(4, sellLine.countOffered);
        }

        [Fact]
        public void TradeDeal_reset_zeroes_pending_transfers_without_moving_counts()
        {
            var deal = new TradeDeal();
            var line = new Tradeable(GoodsDef(5f), 10, 0) { countToTransfer = 3 };
            deal.AddTradeable(line);

            deal.Reset();

            Assert.Equal(0, line.countToTransfer);
            Assert.Equal(10, line.countOffered);
            Assert.Equal(0, line.countInPlayer);
        }

        // ---- ITrader / SettlementTrader / TradeSession ----

        [Fact]
        public void TradeSession_seeds_tradeables_from_the_traders_goods_and_negotiator_social_skill()
        {
            TraderKindDef traderKind = DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods");
            ThingDef goodsA = GoodsDef(5f);
            var trader = new SettlementTrader(traderKind, null);
            trader.goods.Add((goodsA, 40));

            Pawn negotiator = NewHuman("Negotiator");
            int socialLevel = negotiator.skills.GetSkill(SkillDefOf.Social)!.Level;

            var session = new TradeSession();
            session.SetupWith(trader, negotiator);

            Assert.Same(trader, session.trader);
            Assert.Single(session.deal.tradeables);
            Assert.Equal(goodsA, session.deal.tradeables[0].thingDef);
            Assert.Equal(40, session.deal.tradeables[0].countOffered);
            Assert.Equal(TradeUtility.NegotiatorPriceGainFromSocialSkill(socialLevel), session.deal.negotiatorGain);
        }

        [Fact]
        public void SettlementTrader_exposes_its_kind_faction_and_goods()
        {
            TraderKindDef traderKind = DefDatabase<TraderKindDef>.GetNamed("Orbital_BulkGoods");
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), "Sellers", "F_Sellers");
            var trader = new SettlementTrader(traderKind, faction);
            trader.goods.Add((GoodsDef(3f), 12));

            Assert.Same(traderKind, trader.TraderKind);
            Assert.Same(faction, trader.Faction);
            Assert.Single(trader.Goods);
        }
    }
}
