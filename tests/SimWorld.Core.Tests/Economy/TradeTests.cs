using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
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

        // ---- CoalSupply: a settlement's access to coal, local or traded (spec §5b.2) ----

        private static WorldGrid PathableGrid(int subdivisionLevel = 2)
        {
            WorldGrid grid = WorldGrid.Generate(subdivisionLevel);
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                tile.elevation = 50f;
                tile.biome = BiomeDefOf.TemperateForest;
            }
            return grid;
        }

        [Fact]
        public void CoalSupply_grants_local_access_at_raw_market_value_when_the_settlement_has_its_own_deposit()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            const int tileId = 5;
            grid.Tiles[tileId].deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.5f));

            CoalAccess access = CoalSupply.Evaluate(grid, tileId, new List<int>());

            Assert.True(access.HasAccess);
            Assert.True(access.IsLocal);
            Assert.Equal(tileId, access.SourceTile);
            Assert.Equal(TradeUtility.BaseMarketValue(EconomyThingDefOf.Coal), access.UnitCost, 3);
        }

        [Fact]
        public void CoalSupply_has_no_access_when_neither_local_deposits_nor_any_candidate_settlement_has_coal()
        {
            WorldGrid grid = PathableGrid();
            const int home = 3;
            const int coalless = 10;

            CoalAccess access = CoalSupply.Evaluate(grid, home, new List<int> { coalless });

            Assert.False(access.HasAccess);
            Assert.False(access.IsLocal);
            Assert.Null(access.SourceTile);
            Assert.Equal(0f, access.UnitCost);
        }

        [Fact]
        public void CoalSupply_lets_a_coal_less_settlement_trade_for_coal_at_a_real_markup_over_local_supply()
        {
            WorldGrid grid = PathableGrid();
            const int home = 3;
            // At least 2 hops out so `home` isn't handed "local" access simply by being a neighbour of the supplier.
            int supplier = TileAtLeastHopsAway(grid, home, minHops: 2);
            grid.Tiles[supplier].deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.6f));

            CoalAccess traded = CoalSupply.Evaluate(grid, home, new List<int> { supplier });
            CoalAccess local = CoalSupply.Evaluate(grid, supplier, new List<int> { home });

            Assert.True(traded.HasAccess);
            Assert.False(traded.IsLocal);
            Assert.Equal(supplier, traded.SourceTile);

            Assert.True(local.HasAccess);
            Assert.True(local.IsLocal);

            Assert.True(
                traded.UnitCost > local.UnitCost,
                $"Supplying coal by trade should cost more than sitting on a local deposit ({traded.UnitCost} vs {local.UnitCost}).");
            // The floor of that markup is TradeUtility's own buy factor, before any route-distance premium.
            Assert.True(traded.UnitCost >= local.UnitCost * TradeUtility.BuyPriceFactor - 0.001f);
        }

        [Fact]
        public void CoalSupply_prefers_the_cheaper_to_reach_of_two_coal_bearing_settlements()
        {
            WorldGrid grid = PathableGrid();
            const int home = 0;

            // Every tile shares the same biome/elevation, so every edge costs the same: hop count alone
            // decides which of two candidates is cheaper to reach. BFS finds tiles at true shortest-path
            // distances 2 and 4 from `home` (neither a direct neighbour, so `home` gets no "local" access from
            // either just by being adjacent) rather than assuming anything about the grid's own numbering.
            int nearSupplier = TileAtLeastHopsAway(grid, home, minHops: 2);
            int farSupplier = TileAtLeastHopsAway(grid, home, minHops: 4);

            grid.Tiles[nearSupplier].deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.6f));
            grid.Tiles[farSupplier].deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.6f));

            CoalAccess access = CoalSupply.Evaluate(grid, home, new List<int> { farSupplier, nearSupplier });

            Assert.True(access.HasAccess);
            Assert.False(access.IsLocal);
            Assert.Equal(nearSupplier, access.SourceTile);
        }

        private static int TileAtLeastHopsAway(WorldGrid grid, int start, int minHops)
        {
            var distance = new Dictionary<int, int> { [start] = 0 };
            var frontier = new Queue<int>();
            frontier.Enqueue(start);
            int farthest = start;

            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                if (distance[current] >= minHops) return current;
                farthest = current;
                foreach (int neighbor in grid.NeighborsOf(current))
                {
                    if (distance.ContainsKey(neighbor)) continue;
                    distance[neighbor] = distance[current] + 1;
                    frontier.Enqueue(neighbor);
                }
            }
            return farthest;
        }

        // ---- StockGenerator / TraderKindDef.GenerateStock (economy.traders) ----

        [Fact]
        public void StockGenerator_SingleDef_rolls_within_its_count_range()
        {
            ThingDef def = GoodsDef(2f);
            var gen = new StockGenerator_SingleDef { thingDef = def, countRange = new IntRange(5, 10) };
            var rand = new RandomStream(101);

            for (int i = 0; i < 50; i++)
            {
                List<(ThingDef def, int count)> results = gen.Generate(rand).ToList();
                Assert.Single(results);
                Assert.Equal(def, results[0].def);
                Assert.InRange(results[0].count, 5, 10);
            }
        }

        [Fact]
        public void StockGenerator_SingleDef_yields_nothing_without_a_thingDef()
        {
            var gen = new StockGenerator_SingleDef { countRange = new IntRange(5, 10) };
            Assert.Empty(gen.Generate(new RandomStream(1)));
        }

        [Fact]
        public void StockGenerator_MultiDef_rolls_each_entry_independently_within_its_own_range()
        {
            ThingDef a = GoodsDef(1f);
            ThingDef b = GoodsDef(2f);
            var gen = new StockGenerator_MultiDef
            {
                thingDefs = new List<ThingDefCountRange>
                {
                    new ThingDefCountRange { def = a, count = new IntRange(1, 3) },
                    new ThingDefCountRange { def = b, count = new IntRange(50, 60) },
                },
            };
            var rand = new RandomStream(202);

            for (int i = 0; i < 30; i++)
            {
                List<(ThingDef def, int count)> results = gen.Generate(rand).ToList();
                Assert.Equal(2, results.Count);
                Assert.InRange(results.Single(r => r.def == a).count, 1, 3);
                Assert.InRange(results.Single(r => r.def == b).count, 50, 60);
            }
        }

        [Fact]
        public void StockGenerator_is_deterministic_for_the_same_seed()
        {
            var gen = new StockGenerator_SingleDef { thingDef = GoodsDef(3f), countRange = new IntRange(1, 1000) };

            List<int> a = gen.Generate(new RandomStream(999)).Select(r => r.count).ToList();
            List<int> b = gen.Generate(new RandomStream(999)).Select(r => r.count).ToList();

            Assert.Equal(a, b);
        }

        [Fact]
        public void TraderKindDef_GenerateStock_concatenates_every_generator()
        {
            ThingDef a = GoodsDef(1f);
            ThingDef b = GoodsDef(2f);
            var kind = new TraderKindDef
            {
                defName = "TestKind",
                stockGenerators = new List<StockGenerator>
                {
                    new StockGenerator_SingleDef { thingDef = a, countRange = new IntRange(10, 10) },
                    new StockGenerator_SingleDef { thingDef = b, countRange = new IntRange(20, 20) },
                },
            };

            List<(ThingDef def, int count)> stock = kind.GenerateStock(new RandomStream(1));

            Assert.Equal(2, stock.Count);
            Assert.Contains(stock, s => s.def == a && s.count == 10);
            Assert.Contains(stock, s => s.def == b && s.count == 20);
        }

        [Fact]
        public void TraderKindDef_GenerateStock_is_empty_with_no_generators()
        {
            var kind = new TraderKindDef { defName = "EmptyKind" };
            Assert.Empty(kind.GenerateStock(new RandomStream(1)));
        }

        [Fact]
        public void TraderKindDef_ConfigErrors_flags_generators_missing_their_defs()
        {
            var kind = new TraderKindDef
            {
                defName = "BadKind",
                stockGenerators = new List<StockGenerator> { new StockGenerator_SingleDef(), new StockGenerator_MultiDef() },
            };

            Assert.Equal(2, kind.ConfigErrors().Count());
        }

        [Fact]
        public void Content_TraderKindDefs_generate_real_priced_stock_including_silver()
        {
            foreach (string defName in new[] { "Caravan_Tribal_BulkGoods", "Caravan_Outlander_BulkGoods", "Orbital_BulkGoods" })
            {
                TraderKindDef kind = DefDatabase<TraderKindDef>.GetNamed(defName);
                List<(ThingDef def, int count)> stock = kind.GenerateStock(new RandomStream(7));

                Assert.NotEmpty(stock);
                Assert.All(stock, s => Assert.True(s.count > 0, defName + " generated a non-positive count for " + s.def.defName));
                Assert.All(stock, s => Assert.True(TradeUtility.BaseMarketValue(s.def) > 0f, defName + "'s " + s.def.defName + " has no market value"));
                Assert.Contains(stock, s => s.def == EconomyThingDefOf.Silver);
            }
        }

        // ---- Silver routing (economy.traders) ----

        [Fact]
        public void TradeSession_SetupWith_routes_silver_into_the_currency_line_not_an_ordinary_tradeable()
        {
            TraderKindDef traderKind = DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods");
            ThingDef goods = GoodsDef(5f);
            var trader = new SettlementTrader(traderKind, null);
            trader.goods.Add((goods, 40));
            trader.goods.Add((EconomyThingDefOf.Silver, 300));

            var session = new TradeSession();
            session.SetupWith(trader, null);

            Assert.Single(session.deal.tradeables);
            Assert.Equal(goods, session.deal.tradeables[0].thingDef);
            Assert.NotNull(session.deal.silverTradeable);
            Assert.Equal(EconomyThingDefOf.Silver, session.deal.silverTradeable!.thingDef);
            Assert.Equal(300, session.deal.silverTradeable.countOffered);
        }

        // ---- SettlementTradeUtility: real store movement (economy.traders) ----

        private static Settlement NewSettlement(string name, int tile = 0, Faction? faction = null) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, faction, name, 0);

        [Fact]
        public void SettlementTradeUtility_OpenSession_seeds_countInPlayer_from_the_settlements_real_stores()
        {
            Settlement buyer = NewSettlement("Buyer");
            ThingDef goods = GoodsDef(5f);
            buyer.SetStoreCount(goods, 20);
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((goods, 40));

            TradeSession session = SettlementTradeUtility.OpenSession(buyer, trader, null);

            Assert.Equal(20, session.deal.tradeables[0].countInPlayer);
            Assert.NotNull(session.deal.silverTradeable);
            Assert.Equal(1000, session.deal.silverTradeable!.countInPlayer);
        }

        [Fact]
        public void SettlementTradeUtility_OpenSession_guarantees_a_silver_line_even_when_the_trader_carries_none()
        {
            Settlement buyer = NewSettlement("Buyer");
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 500);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((GoodsDef(2f), 10)); // no Silver among this trader's own goods

            TradeSession session = SettlementTradeUtility.OpenSession(buyer, trader, null);

            Assert.NotNull(session.deal.silverTradeable);
            Assert.Equal(0, session.deal.silverTradeable!.countOffered);
            Assert.Equal(500, session.deal.silverTradeable.countInPlayer);
        }

        [Fact]
        public void SettlementTradeUtility_TryExecute_moves_real_goods_and_silver_into_the_buyers_store_ledger()
        {
            Settlement buyer = NewSettlement("Buyer");
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 1000);
            ThingDef goods = GoodsDef(10f);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((goods, 50));

            TradeSession session = SettlementTradeUtility.OpenSession(buyer, trader, null);
            session.deal.tradeables[0].countToTransfer = 5;
            float expectedCost = TradeUtility.GetPricePlayerBuy(goods) * 5;

            bool executed = SettlementTradeUtility.TryExecute(session, buyer, null, out bool actuallyTraded);

            Assert.True(executed);
            Assert.True(actuallyTraded);
            Assert.Equal(5, buyer.StoreCountOf(goods));
            Assert.Equal(1000 - (int)System.Math.Round(expectedCost), buyer.StoreCountOf(EconomyThingDefOf.Silver));
        }

        [Fact]
        public void SettlementTradeUtility_TryExecute_updates_both_ledgers_for_a_settlement_to_settlement_trade()
        {
            ThingDef goods = GoodsDef(4f);
            Settlement seller = NewSettlement("Seller", tile: 1);
            seller.SetStoreCount(goods, 50);
            seller.SetStoreCount(EconomyThingDefOf.Silver, 50);

            Settlement buyer = NewSettlement("Buyer", tile: 2);
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            ITrader sellerTrader = SettlementTradeUtility.AsTrader(seller, DefDatabase<TraderKindDef>.GetNamed("Caravan_Outlander_BulkGoods"));
            TradeSession session = SettlementTradeUtility.OpenSession(buyer, sellerTrader, null);
            session.deal.tradeables.Single(t => t.thingDef == goods).countToTransfer = 10;

            bool executed = SettlementTradeUtility.TryExecute(session, buyer, seller, out bool actuallyTraded);

            Assert.True(executed);
            Assert.True(actuallyTraded);
            Assert.Equal(10, buyer.StoreCountOf(goods));
            Assert.Equal(40, seller.StoreCountOf(goods));
            Assert.True(seller.StoreCountOf(EconomyThingDefOf.Silver) > 50, "the seller settlement should have been paid");
            Assert.True(buyer.StoreCountOf(EconomyThingDefOf.Silver) < 1000, "the buyer settlement should have paid silver");
        }

        [Fact]
        public void SettlementTradeUtility_TryExecute_refuses_and_changes_nothing_when_unaffordable()
        {
            Settlement buyer = NewSettlement("PoorBuyer");
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 1);
            ThingDef goods = GoodsDef(50f);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((goods, 10));

            TradeSession session = SettlementTradeUtility.OpenSession(buyer, trader, null);
            session.deal.tradeables[0].countToTransfer = 5;

            bool executed = SettlementTradeUtility.TryExecute(session, buyer, null, out bool actuallyTraded);

            Assert.False(executed);
            Assert.False(actuallyTraded);
            Assert.Equal(0, buyer.StoreCountOf(goods));
            Assert.Equal(1, buyer.StoreCountOf(EconomyThingDefOf.Silver));
        }

        [Fact]
        public void SettlementTradeUtility_tradeAccessGain_lowers_the_net_cost_of_a_buy()
        {
            Settlement buyer = NewSettlement("Buyer");
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 100000);
            ThingDef goods = GoodsDef(100f);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((goods, 100));

            TradeSession plain = SettlementTradeUtility.OpenSession(buyer, trader, null);
            plain.deal.tradeables[0].countToTransfer = 1;

            TradeSession discounted = SettlementTradeUtility.OpenSession(buyer, trader, null, tradeAccessGain: 0.1f);
            discounted.deal.tradeables[0].countToTransfer = 1;

            Assert.True(discounted.deal.NetPlayerSilverCost() < plain.deal.NetPlayerSilverCost());
        }

        [Fact]
        public void Scribe_round_trip_preserves_a_settlements_store_ledger_after_a_completed_trade()
        {
            ThingDef goods = DefDatabase<ThingDef>.GetNamed("WoodLog"); // a real, cross-reffable ThingDef — an ad hoc GoodsDef() would fail to reload by name.
            Settlement buyer = NewSettlement("ScribeBuyer");
            buyer.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            var trader = new SettlementTrader(DefDatabase<TraderKindDef>.GetNamed("Caravan_Tribal_BulkGoods"), null);
            trader.goods.Add((goods, 50));

            TradeSession session = SettlementTradeUtility.OpenSession(buyer, trader, null);
            session.deal.tradeables[0].countToTransfer = 5;
            Assert.True(SettlementTradeUtility.TryExecute(session, buyer, null, out bool actuallyTraded));
            Assert.True(actuallyTraded);

            int expectedGoods = buyer.StoreCountOf(goods);
            int expectedSilver = buyer.StoreCountOf(EconomyThingDefOf.Silver);
            Assert.Equal(5, expectedGoods);

            string xml = Scribe.SaveToString(buyer, "settlement");
            Settlement loaded = Scribe.Load<Settlement>(xml, "settlement", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(expectedGoods, loaded.StoreCountOf(goods));
            Assert.Equal(expectedSilver, loaded.StoreCountOf(EconomyThingDefOf.Silver));
        }

        // ---- TradeRouteUtility (economy.diplomacy: trade routes between settlements) ----

        [Fact]
        public void TradeRouteUtility_Evaluate_same_tile_is_free()
        {
            WorldGrid grid = PathableGrid();
            TradeRouteAccess access = TradeRouteUtility.Evaluate(grid, 5, 5);
            Assert.True(access.HasRoute);
            Assert.Equal(0f, access.RouteCost);
            Assert.Equal(1f, access.PriceMultiplier);
        }

        [Fact]
        public void TradeRouteUtility_Evaluate_reports_no_route_to_an_impassable_tile()
        {
            WorldGrid grid = PathableGrid();
            const int oceanTile = 1;
            grid.Tiles[oceanTile].biome = BiomeDefOf.Ocean;
            grid.Tiles[oceanTile].elevation = -10f;

            TradeRouteAccess access = TradeRouteUtility.Evaluate(grid, 0, oceanTile);

            Assert.False(access.HasRoute);
            Assert.Equal(1f, access.PriceMultiplier);
            Assert.Equal(0f, access.UnitCostFor(GoodsDef(10f)));
        }

        [Fact]
        public void TradeRouteUtility_Evaluate_prices_a_real_route_above_1_and_rises_with_distance()
        {
            WorldGrid grid = PathableGrid();
            const int home = 0;
            int near = TileAtLeastHopsAway(grid, home, minHops: 2);
            int far = TileAtLeastHopsAway(grid, home, minHops: 4);

            TradeRouteAccess nearAccess = TradeRouteUtility.Evaluate(grid, home, near);
            TradeRouteAccess farAccess = TradeRouteUtility.Evaluate(grid, home, far);

            Assert.True(nearAccess.HasRoute);
            Assert.True(farAccess.HasRoute);
            Assert.True(nearAccess.PriceMultiplier > 1f);
            Assert.True(farAccess.PriceMultiplier > nearAccess.PriceMultiplier,
                $"expected a farther route to price higher ({farAccess.PriceMultiplier} vs {nearAccess.PriceMultiplier})");
        }

        [Fact]
        public void TradeRouteUtility_UnitCostFor_prices_above_the_plain_buy_price_over_a_real_route()
        {
            WorldGrid grid = PathableGrid();
            int far = TileAtLeastHopsAway(grid, 0, minHops: 3);
            TradeRouteAccess access = TradeRouteUtility.Evaluate(grid, 0, far);
            ThingDef def = GoodsDef(10f);

            float routed = access.UnitCostFor(def);
            float plain = TradeUtility.GetPricePlayerBuy(def);

            Assert.True(routed > plain, $"expected a route premium to raise the price above the plain buy price ({routed} vs {plain})");
        }

        [Fact]
        public void TradeRouteUtility_RouteOpen_closes_between_settlements_at_war_and_reopens_at_peace()
        {
            WorldGrid grid = PathableGrid();
            Faction a = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "RouteA", "F_RouteA");
            Faction b = new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), "RouteB", "F_RouteB");
            int otherTile = TileAtLeastHopsAway(grid, 0, minHops: 2);
            Settlement s1 = NewSettlement("S1", tile: 0, faction: a);
            Settlement s2 = NewSettlement("S2", tile: otherTile, faction: b);

            Assert.True(TradeRouteUtility.RouteOpen(grid, s1, s2), "a route should be open before any war is declared");

            Assert.True(a.DeclareWar(b));
            Assert.False(TradeRouteUtility.RouteOpen(grid, s1, s2));

            Assert.True(a.MakePeace(b));
            Assert.True(TradeRouteUtility.RouteOpen(grid, s1, s2));
        }
    }
}
