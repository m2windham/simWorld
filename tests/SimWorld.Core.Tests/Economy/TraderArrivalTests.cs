using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreWorld = SimWorld.World.World;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using EconomyThingDefOf = global::SimWorld.Economy.EconomyThingDefOf;
using TradeUtility = global::SimWorld.Economy.TradeUtility;
using TraderArrival = global::SimWorld.Economy.TraderArrival;
using TraderArrivalTuning = global::SimWorld.Economy.TraderArrivalTuning;
using TraderCaravan = global::SimWorld.Economy.TraderCaravan;
using TraderKindDef = global::SimWorld.Economy.TraderKindDef;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>A caravan that is somewhere, for a while, and can actually be traded with.</b>
    ///
    /// <para/><b>The defect these pin.</b> <c>docs/design/player-first.md</c> §10 lists
    /// <c>Economy.SettlementTradeUtility.OpenSession</c> among the mechanisms that are "built, tested, and
    /// never called from <c>src/</c>" — "a trader arrives with real stock and no session ever opens". The
    /// director genuinely generated a trader, with a real <c>TraderKindDef</c>, a real faction and real priced
    /// stock; the trader then existed on a test hook and nowhere else. A caravan arrived, carrying goods, and
    /// nothing in the game could ever buy or sell any of it.
    ///
    /// <para/><b>What these assert, in the order the missing machinery had to be built.</b> That an incident
    /// firing through the director's own queue in a real tick loop puts a caravan on a real settlement's tile;
    /// that the god view can then see it, its stock, its deadline and its prices; that a command buying from
    /// it moves the settlement's real store ledger and the caravan's real stock; that it leaves when its stay
    /// is over and cannot be traded with afterwards; that a trader who is present survives a save; that every
    /// refusal is an impossibility rather than a judgement — and, twice over, that a trade which will plainly
    /// hurt is carried out exactly as asked.
    /// </summary>
    public class TraderArrivalTests : ContentTestBase
    {
        public TraderArrivalTests(CoreContentFixture content) : base(content)
        {
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static ThingDef Thing(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static TraderKindDef Kind(string defName) => DefDatabase<TraderKindDef>.GetNamed(defName);

        private static Faction NewFaction(string factionDefName, string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed(factionDefName), name, "F_" + name);

        /// <summary>
        /// A world this thread is running with one settlement on it and nobody living in it. Small on purpose
        /// (subdivision 2): every test below is about arrival, presence and the ledger, none of which reads
        /// terrain, and a settlement with no citizens negotiates at zero price gain — so the prices these
        /// assert against are <c>TradeUtility</c>'s own, with nothing else folded in.
        /// </summary>
        private static (CoreWorld world, Settlement settlement) PosedWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "TradeWorld", 2, soloStart: true);
            Find.World = world;

            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && !taken.Contains(i));

            Faction home = NewFaction("PlayerCivilization", "Home");
            world.factions.Add(home);

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, home, "Hearth", 0);
            world.worldObjects.Add(settlement);
            return (world, settlement);
        }

        /// <summary>Lands a caravan with an exactly-stated stock, so every count a refusal quotes below is a
        /// fixture rather than a roll.</summary>
        private static TraderCaravan LandCaravan(
            CoreWorld world, Settlement settlement, string kindDefName, params (string defName, int count)[] stock)
        {
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");
            world.factions.Add(seller);

            List<(ThingDef def, int count)> goods = stock
                .Select(entry => (Thing(entry.defName), entry.count))
                .ToList();

            TraderCaravan? caravan = TraderArrival.Land(
                settlement, Kind(kindDefName), seller, goods, new RandomStream(4242));
            Assert.NotNull(caravan);
            return caravan!;
        }

        /// <summary>A caravan is still one of the world's objects. Written as a predicate rather than
        /// Assert.Contains so the element type stays the list's own (WorldObject) rather than being inferred
        /// from the argument.</summary>
        private static void AssertHolds(CoreWorld world, TraderCaravan caravan) =>
            Assert.Contains(world.worldObjects, o => ReferenceEquals(o, caravan));

        /// <summary>Runs the world's own tick loop for real — the same <c>TickManager.PreTickers</c> slot
        /// <c>Sim.Game.WireTickHooks</c> puts <c>World.WorldTick</c> in — rather than setting the clock.</summary>
        private static void TickWorld(CoreWorld world, int ticks)
        {
            TickManager tm = Find.TickManager;
            tm.PreTickers.Add(_ => world.WorldTick());
            for (int i = 0; i < ticks; i++) tm.DoSingleTick();
        }

        // -------------------------------------------------------------------------------------------
        // End to end: the incident fires in a real tick loop and the ledger moves.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole path, with nothing called by hand along it: a founded civilization, the shipped
        /// <c>TraderCaravanArrival</c> incident put on the director's own queue, and then only
        /// <see cref="TickManager.DoSingleTick"/>. The storyteller fires it, the worker lands a caravan on a
        /// real settlement's tile, the god view reports it with its stock and its prices, one command buys
        /// from it, and <see cref="Settlement.Stores"/> — the civilization's actual ledger — changes.
        ///
        /// <para/>Queued rather than waited for: <see cref="IncidentQueue"/> is the director's own "this will
        /// happen at tick T" path and is what a storyteller comp schedules through, so this is the real firing
        /// mechanism at a tick the test can name instead of a random one it would have to wait for.
        /// </summary>
        [Fact]
        public void An_incident_firing_in_the_tick_loop_lands_a_trader_the_god_view_can_see_and_buy_from()
        {
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, "trade-arrival",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();

            // Somebody to trade with. A solo start puts nobody but the player on the map, so the trading
            // civilization is added exactly as Director/TraderCaravanArrivalTests adds one.
            Find.FactionManager.Add(NewFaction("TribalCivilization", "Riverfolk"));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 5000);

            IncidentDef arrival = DefDatabase<IncidentDef>.GetNamed("TraderCaravanArrival");
            Find.Storyteller.incidentQueue.Add(
                new FiringIncident(arrival, null, new IncidentParms { target = game.CivilizationTarget }),
                Find.TickManager.TicksGame + 10);

            for (int i = 0; i < 2 * Storyteller.IncidentCycleLengthTicks; i++) game.TickManager.DoSingleTick();

            // 1. A trader is here, and the read model says so.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            VisitingTraderView trader = Assert.Single(snapshot.VisitingTraders);
            Assert.Equal(settlement.tile, trader.SettlementTile);
            Assert.Equal(settlement.name, trader.SettlementName);
            Assert.True(trader.TicksUntilDeparture > 0, "a caravan that has just arrived is still leaving later");

            // 2. This is their stock, priced.
            TradeLineView line = trader.Goods.First(g =>
                g.CountTraderHas > 0 && g.UnitBuyPrice > 0f && g.UnitBuyPrice <= trader.SettlementSilver);
            ThingDef bought = Thing(line.DefName);

            int goodsBefore = settlement.StoreCountOf(bought);
            int silverBefore = settlement.StoreCountOf(EconomyThingDefOf.Silver);

            // 3. A command buys some of it.
            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, line.DefName, 1);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(goodsBefore + 1, settlement.StoreCountOf(bought));
            Assert.True(
                settlement.StoreCountOf(EconomyThingDefOf.Silver) < silverBefore,
                "the settlement bought a good and paid nothing for it");

            // 4. And the caravan has one fewer to sell — a visit is an occasion, not an infinite shop.
            VisitingTraderView after = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            Assert.Equal(
                line.CountTraderHas - 1,
                after.Goods.First(g => g.DefName == line.DefName).CountTraderHas);
        }

        /// <summary>The other direction, over the same seam: goods leave the ledger and silver arrives.</summary>
        [Fact]
        public void Selling_moves_goods_out_of_the_ledger_and_silver_into_it()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-sell");
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods",
                ("WoodLog", 10), ("Silver", 2000));
            settlement.SetStoreCount(Thing("Cloth"), 50);

            GodCommandResult result = GodCommands.SellToTrader(settlement.tile, "Cloth", 20);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(30, settlement.StoreCountOf(Thing("Cloth")));
            Assert.True(settlement.StoreCountOf(EconomyThingDefOf.Silver) > 0, "the settlement was not paid");

            // A caravan that did not arrive carrying Cloth is now carrying the Cloth it bought — and is
            // carrying less silver for it.
            Assert.Equal(20, caravan.StockOf(Thing("Cloth")));
            Assert.True(caravan.StockOf(EconomyThingDefOf.Silver) < 2000, "the caravan paid with silver it still has");
        }

        // -------------------------------------------------------------------------------------------
        // Departure.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// A caravan is a deadline, not a shop. It leaves on its own while the world ticks, the view stops
        /// listing it, and a command aimed at it afterwards is refused in words rather than silently doing
        /// nothing.
        /// </summary>
        [Fact]
        public void A_trader_leaves_when_its_stay_is_over_and_trading_afterwards_is_refused()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-departure");
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods",
                ("WoodLog", 40), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 5000);

            Assert.NotNull(TraderArrival.At(settlement.tile));
            Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 1).Outcome);

            TickWorld(world, caravan.DepartureTick - Find.TickManager.TicksGame);

            Assert.Null(TraderArrival.At(settlement.tile));
            Assert.Empty(GodViewSnapshot.Capture().VisitingTraders);

            GodCommandResult afterwards = GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 1);
            Assert.Equal(GodCommandOutcome.Refused, afterwards.Outcome);
            Assert.Contains("No trade caravan", afterwards.Reason);
        }

        /// <summary>
        /// The stay is a real span rather than a constant this test could be reading back to itself: pinned as
        /// a band and an ordering (CLAUDE.md — assert behaviour over literals for anything tuned), and pinned
        /// as ending, which is the part that matters to a player.
        /// </summary>
        [Fact]
        public void A_stay_lasts_a_rolled_span_and_ends()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-stay");
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 5));

            int stay = caravan.DepartureTick - caravan.ArrivalTick;
            Assert.InRange(stay, TraderArrivalTuning.StayDurationTicks.min, TraderArrivalTuning.StayDurationTicks.max);

            Assert.True(caravan.PresentAt(caravan.ArrivalTick));
            Assert.True(caravan.PresentAt(caravan.DepartureTick - 1));
            Assert.False(caravan.PresentAt(caravan.DepartureTick));
            Assert.False(caravan.PresentAt(caravan.DepartureTick + GenDate.TicksPerYear));
        }

        /// <summary>A departed caravan does not accumulate in the world's object list: the next write path
        /// sweeps it. See <c>Economy.TraderArrival.PruneDeparted</c> for why the sweep cannot live in a world
        /// tick.</summary>
        [Fact]
        public void A_departed_caravan_is_swept_out_of_the_world_by_the_next_write()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-sweep");
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 5));
            AssertHolds(world, caravan);

            Find.TickManager.DebugSetTicksGame(caravan.DepartureTick);
            AssertHolds(world, caravan); // nothing has looked at the trade seam yet

            GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 1);

            Assert.DoesNotContain(world.worldObjects, o => ReferenceEquals(o, caravan));
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// A trader who is standing at a settlement when the game is saved is standing there when it is
        /// loaded, with the same stock, the same deadline and the same faction — and can still be traded with.
        /// The caravan rides in <c>World.worldObjects</c>, which is already deep-saved polymorphically, so
        /// this is the real save path rather than a round-trip of the object on its own.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_preserves_a_present_trader_its_stock_and_its_deadline()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-scribe");
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Outlander_BulkGoods",
                ("Steel", 45), ("Cloth", 12), ("Silver", 700));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 3000);

            List<(string def, int count)> before = caravan.Goods.Select(g => (g.def.defName, g.count)).ToList();

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Find.World = loaded;
            TraderCaravan? reloaded = TraderArrival.At(settlement.tile);

            Assert.NotNull(reloaded);
            Assert.NotSame(caravan, reloaded);
            Assert.Equal(caravan.TraderKind.defName, reloaded!.TraderKind.defName);
            Assert.Equal(caravan.ArrivalTick, reloaded.ArrivalTick);
            Assert.Equal(caravan.DepartureTick, reloaded.DepartureTick);
            Assert.Equal(caravan.Faction?.name, reloaded.Faction?.name);
            Assert.Equal(before, reloaded.Goods.Select(g => (g.def.defName, g.count)).ToList());

            // And the trade seam works against the loaded world, not merely the loaded object.
            Settlement loadedSettlement = loaded.worldObjects.OfType<Settlement>().Single(s => s.tile == settlement.tile);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.BuyFromTrader(settlement.tile, "Steel", 5).Outcome);
            Assert.Equal(5, loadedSettlement.StoreCountOf(Thing("Steel")));
            Assert.Equal(40, reloaded.StockOf(Thing("Steel")));
        }

        // -------------------------------------------------------------------------------------------
        // Refusals — one per reason, and every one of them an impossibility.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Trading_at_a_tile_with_no_settlement_reports_an_unknown_settlement()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-tile");
            var occupied = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int emptyTile = Enumerable.Range(0, world.grid.TilesCount).First(i => !occupied.Contains(i));

            GodCommandResult result = GodCommands.BuyFromTrader(emptyTile, "WoodLog", 1);

            Assert.Equal(GodCommandOutcome.UnknownSettlement, result.Outcome);
        }

        [Fact]
        public void Trading_where_no_caravan_stands_is_refused()
        {
            (_, Settlement settlement) = PosedWorld("trade-refuse-absent");
            settlement.SetStoreCount(Thing("WoodLog"), 100);
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            GodCommandResult buy = GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 1);
            GodCommandResult sell = GodCommands.SellToTrader(settlement.tile, "WoodLog", 1);

            Assert.Equal(GodCommandOutcome.Refused, buy.Outcome);
            Assert.Equal(GodCommandOutcome.Refused, sell.Outcome);
            Assert.Contains("No trade caravan", buy.Reason);
            Assert.Contains("No trade caravan", sell.Reason);
        }

        [Fact]
        public void Trading_a_good_no_content_names_is_refused()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-unknown");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 400));

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "NotAThingInAnyContent", 1);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("NotAThingInAnyContent", result.Reason);
        }

        [Fact]
        public void Buying_more_than_the_caravan_carries_is_refused_and_moves_nothing()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-stock");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 100000);

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 11);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Equal(0, settlement.StoreCountOf(Thing("WoodLog")));
            Assert.Equal(100000, settlement.StoreCountOf(EconomyThingDefOf.Silver));
        }

        /// <summary>A good the caravan did not bring at all is the same refusal, reached the other way: the
        /// caravan carries none of it rather than too little of it.</summary>
        [Fact]
        public void Buying_a_good_the_caravan_did_not_bring_is_refused()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-nostock");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 100000);

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "Steel", 1);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("carries no", result.Reason);
        }

        [Fact]
        public void Selling_what_the_settlement_does_not_hold_is_refused_and_moves_nothing()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-holdings");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 4000));
            settlement.SetStoreCount(Thing("Cloth"), 5);

            GodCommandResult none = GodCommands.SellToTrader(settlement.tile, "Steel", 1);
            GodCommandResult tooMany = GodCommands.SellToTrader(settlement.tile, "Cloth", 6);

            Assert.Equal(GodCommandOutcome.Refused, none.Outcome);
            Assert.Equal(GodCommandOutcome.Refused, tooMany.Outcome);
            Assert.Equal(5, settlement.StoreCountOf(Thing("Cloth")));
            Assert.Equal(0, settlement.StoreCountOf(EconomyThingDefOf.Silver));
        }

        [Fact]
        public void Buying_what_the_settlement_cannot_pay_for_is_refused_and_moves_nothing()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-purse");
            LandCaravan(world, settlement, "Caravan_Outlander_BulkGoods", ("Steel", 100), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 1);

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "Steel", 100);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("cannot afford", result.Reason);
            Assert.Equal(0, settlement.StoreCountOf(Thing("Steel")));
            Assert.Equal(1, settlement.StoreCountOf(EconomyThingDefOf.Silver));
        }

        /// <summary>
        /// The refusal on the trader's side of the purse. A caravan that cannot pay is not a judgement about
        /// the sale — it is a fact about the caravan, and without this check the settlement would be paid in
        /// silver that never existed while the caravan's purse went quietly negative.
        /// </summary>
        [Fact]
        public void Selling_more_than_the_caravan_can_pay_for_is_refused_and_moves_nothing()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-traderpurse");
            const int purse = 200;
            TraderCaravan caravan = LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods",
                ("WoodLog", 5), ("Silver", purse));

            // Enough wood that the caravan plainly cannot cover it, sized off the price the deal itself uses.
            float perUnit = TradeUtility.GetPricePlayerSell(Thing("WoodLog"));
            Assert.True(perUnit > 0f, "WoodLog has no market value to sell at");
            int tooMuch = (int)(purse / perUnit) + 50;
            settlement.SetStoreCount(Thing("WoodLog"), tooMuch);

            GodCommandResult result = GodCommands.SellToTrader(settlement.tile, "WoodLog", tooMuch);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("silver", result.Reason);
            Assert.Equal(tooMuch, settlement.StoreCountOf(Thing("WoodLog")));
            Assert.Equal(0, settlement.StoreCountOf(EconomyThingDefOf.Silver));
            Assert.Equal(purse, caravan.StockOf(EconomyThingDefOf.Silver));
        }

        /// <summary>Silver is the currency line, not a good — <c>TradeSession.SetupWith</c> routes it away
        /// from the tradeables list on purpose, so there is nothing to put a transfer on.</summary>
        [Fact]
        public void Silver_cannot_itself_be_traded_for()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-refuse-silver");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "Silver", 10);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Equal(1000, settlement.StoreCountOf(EconomyThingDefOf.Silver));
        }

        [Fact]
        public void Asking_for_nothing_changes_nothing_and_says_so()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-nochange");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 1000);

            Assert.Equal(GodCommandOutcome.NoChange, GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 0).Outcome);
            Assert.Equal(1000, settlement.StoreCountOf(EconomyThingDefOf.Silver));
        }

        // -------------------------------------------------------------------------------------------
        // Ruinous, and allowed. docs/design/player-first.md §5.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Selling every scrap of the settlement's food, at a price that is a loss by construction — the sell
        /// side is <c>TradeUtility.SellPriceFactor</c> of market value — because a caravan is here now and
        /// winter is not. The simulation carries it out and the settlement is left with nothing to eat. That
        /// is the decision, and a game that refused it would have removed the thing the player came for.
        /// </summary>
        [Fact]
        public void Selling_the_settlements_entire_food_store_at_a_loss_is_allowed()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-ruinous-food");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10), ("Silver", 100000));

            ThingDef food = Thing("Pemmican");
            settlement.SetStoreCount(food, 400);

            float marketValue = TradeUtility.BaseMarketValue(food);
            float received = TradeUtility.GetPricePlayerSell(food);
            Assert.True(received < marketValue, "the sell side is meant to be a loss against market value");

            GodCommandResult result = GodCommands.SellToTrader(settlement.tile, food.defName, 400);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(0, settlement.StoreCountOf(food));
            Assert.True(
                settlement.StoreCountOf(EconomyThingDefOf.Silver) < (int)(marketValue * 400),
                "the settlement sold its whole larder and was not even paid market value for it");
        }

        /// <summary>
        /// The mirror: spending the treasury down to almost nothing on goods with no use to the settlement,
        /// at the buy-side markup. Allowed, and the settlement is left unable to buy the medicine it will
        /// want next quarter.
        /// </summary>
        [Fact]
        public void Spending_the_whole_treasury_on_goods_the_settlement_does_not_need_is_allowed()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-ruinous-treasury");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 60), ("Silver", 400));

            ThingDef wood = Thing("WoodLog");
            int treasury = (int)System.Math.Ceiling(TradeUtility.GetPricePlayerBuy(wood) * 60);
            settlement.SetStoreCount(EconomyThingDefOf.Silver, treasury);

            GodCommandResult result = GodCommands.BuyFromTrader(settlement.tile, "WoodLog", 60);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(60, settlement.StoreCountOf(wood));
            Assert.True(
                settlement.StoreCountOf(EconomyThingDefOf.Silver) <= 1,
                "the settlement spent its treasury on firewood and the command let it");
        }

        // -------------------------------------------------------------------------------------------
        // Determinism (CLAUDE.md: determinism is a feature).
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Two runs of the same firing from the same seed land the same trader kind, the same stock in the
        /// same order, and the same departure tick. Driven through the director's own queue and a real tick
        /// loop rather than by calling the worker, so the draws the incident itself makes — the faction, the
        /// kind, every stock roll, and the stay — are all inside what is being compared.
        /// </summary>
        [Fact]
        public void Two_identically_seeded_firings_land_the_same_trader_with_the_same_stock_and_deadline()
        {
            (string kind, int departure, List<(string def, int count)> stock) first = FireOneArrival(seed: 8712);
            (string kind, int departure, List<(string def, int count)> stock) second = FireOneArrival(seed: 8712);
            (string kind, int departure, List<(string def, int count)> stock) other = FireOneArrival(seed: 991);

            Assert.Equal(first.kind, second.kind);
            Assert.Equal(first.departure, second.departure);
            Assert.Equal(first.stock, second.stock);

            // And the seed is load-bearing rather than the whole thing being constant: a different stream
            // produces a different caravan. Counts alone could coincide, so compare the whole table.
            Assert.True(
                other.kind != first.kind || !other.stock.SequenceEqual(first.stock) || other.departure != first.departure,
                "a different seed produced a byte-identical caravan, so nothing here is actually rolled");
        }

        /// <summary>One posed civilization, one queued <c>TraderCaravanArrival</c>, one real tick loop; what
        /// landed, flattened to values that can be compared between runs.</summary>
        private static (string kind, int departure, List<(string def, int count)> stock) FireOneArrival(int seed)
        {
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);

            (CoreWorld world, Settlement settlement) = PosedWorld("determinism");

            Find.FactionManager = new FactionManager();
            Find.FactionManager.Add(NewFaction("PlayerCivilization", "DetHome"));
            Find.FactionManager.Add(NewFaction("TribalCivilization", "DetRiverfolk"));

            // No target is registered with this storyteller, so its comps consider nobody and only the queued
            // incident below ever fires — the tick loop is real, the noise is not there to drown it.
            var storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, DifficultyDefOf.Medium);
            Find.Storyteller = storyteller;

            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            storyteller.incidentQueue.Add(
                new FiringIncident(
                    DefDatabase<IncidentDef>.GetNamed("TraderCaravanArrival"), null,
                    new IncidentParms { target = target }),
                10);

            TickManager tm = Find.TickManager;
            tm.PreTickers.Add(_ => world.WorldTick());
            tm.PostTickers.Add(_ => storyteller.StorytellerTick());
            for (int i = 0; i < Storyteller.IncidentCycleLengthTicks; i++) tm.DoSingleTick();

            TraderCaravan? caravan = TraderArrival.At(settlement.tile);
            Assert.NotNull(caravan);
            return (
                caravan!.TraderKind.defName,
                caravan.DepartureTick,
                caravan.Goods.Select(g => (g.def.defName, g.count)).ToList());
        }

        // -------------------------------------------------------------------------------------------
        // The read model itself.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The table the player reads is the table they are charged from. The quote comes off the same session
        /// the command opens (<c>Economy.VisitingTraderTrade.OpenSessionWith</c>), so a buy costs exactly
        /// <see cref="TradeLineView.UnitBuyPrice"/> per unit — this pins that rather than trusting it.
        /// </summary>
        [Fact]
        public void The_quoted_price_is_the_price_charged()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-quote");
            LandCaravan(world, settlement, "Caravan_Outlander_BulkGoods", ("Steel", 50), ("Silver", 400));
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 10000);

            VisitingTraderView trader = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            TradeLineView steel = trader.Goods.Single(g => g.DefName == "Steel");

            Assert.Equal(50, steel.CountTraderHas);
            Assert.True(steel.UnitBuyPrice > steel.UnitSellPrice, "a trader lives on the gap between the two");

            const int bought = 7;
            int before = settlement.StoreCountOf(EconomyThingDefOf.Silver);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.BuyFromTrader(settlement.tile, "Steel", bought).Outcome);

            int paid = before - settlement.StoreCountOf(EconomyThingDefOf.Silver);
            Assert.Equal((int)System.Math.Round(steel.UnitBuyPrice * bought), paid);
        }

        /// <summary>
        /// "This is what they want" is the same table read down the other column: a good the settlement holds
        /// and the caravan did not bring still gets a row, with a sell price on it, because that is the row a
        /// player decides against.
        /// </summary>
        [Fact]
        public void The_view_lists_what_the_settlement_could_sell_as_well_as_what_the_trader_brought()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-wants");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 30), ("Silver", 400));
            settlement.SetStoreCount(Thing("Steel"), 25);

            VisitingTraderView trader = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);

            TradeLineView brought = trader.Goods.Single(g => g.DefName == "WoodLog");
            Assert.Equal(30, brought.CountTraderHas);
            Assert.Equal(0, brought.CountSettlementHas);

            TradeLineView wanted = trader.Goods.Single(g => g.DefName == "Steel");
            Assert.Equal(0, wanted.CountTraderHas);
            Assert.Equal(25, wanted.CountSettlementHas);
            Assert.True(wanted.UnitSellPrice > 0f, "a good the settlement can sell needs a price to sell it at");

            Assert.Equal(400, trader.TraderSilver);
            Assert.Equal(0, trader.SettlementSilver);
        }

        /// <summary>A settlement's best talker is who negotiates, and a better one moves the price the right
        /// way — RimWorld prices a deal off the negotiator's Social skill and a god picks no pawn, so the
        /// settlement fields its best. Asserted as an ordering, never as a number.</summary>
        [Fact]
        public void The_settlements_best_talker_negotiates_and_a_better_one_improves_the_price()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-negotiator");
            LandCaravan(world, settlement, "Caravan_Outlander_BulkGoods", ("Steel", 50), ("Silver", 400));

            float withoutAnyone = Assert.Single(GodViewSnapshot.Capture().VisitingTraders)
                .Goods.Single(g => g.DefName == "Steel").UnitBuyPrice;

            global::SimWorld.Pawns.Pawn quiet = NewHuman("Quiet");
            quiet.skills.GetSkill(global::SimWorld.Work.SkillDefOf.Social)!.Level = 0;
            settlement.AddCitizen(quiet);

            global::SimWorld.Pawns.Pawn silverTongue = NewHuman("SilverTongue");
            silverTongue.skills.GetSkill(global::SimWorld.Work.SkillDefOf.Social)!.Level = 20;
            settlement.AddCitizen(silverTongue);

            VisitingTraderView withTalker = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);

            Assert.Equal(silverTongue.Label, withTalker.NegotiatorName);
            Assert.True(withTalker.NegotiatorPriceGain > 0f);
            Assert.True(
                withTalker.Goods.Single(g => g.DefName == "Steel").UnitBuyPrice < withoutAnyone,
                "a skilled negotiator should have brought the buy price down");
        }

        /// <summary>At most one caravan stands at a settlement, because the tile is the handle both commands
        /// take and two traders on one tile would make it ambiguous.</summary>
        [Fact]
        public void A_second_caravan_does_not_land_on_a_settlement_that_already_has_one()
        {
            (CoreWorld world, Settlement settlement) = PosedWorld("trade-occupied");
            LandCaravan(world, settlement, "Caravan_Tribal_BulkGoods", ("WoodLog", 10));

            TraderCaravan? second = TraderArrival.Land(
                settlement, Kind("Caravan_Outlander_BulkGoods"), null,
                new List<(ThingDef, int)> { (Thing("Steel"), 10) },
                new RandomStream(7));

            Assert.Null(second);
            Assert.Single(world.worldObjects.OfType<TraderCaravan>());
        }
    }
}
