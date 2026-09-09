using System.Linq;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>economy.traders: IncidentWorker_TraderCaravanArrival actually generating a real, priced trader, end to end.</summary>
    public class TraderCaravanArrivalTests : ContentTestBase
    {
        public TraderCaravanArrivalTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");
        private static FactionDef RoughDef => DefDatabase<FactionDef>.GetNamed("RoughOutlanders");
        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        private static global::SimWorld.Director.IncidentWorker_TraderCaravanArrival NewWorker()
        {
            var def = new global::SimWorld.Director.IncidentDef
            {
                defName = "TestTraderCaravanArrival",
                category = global::SimWorld.Director.IncidentCategoryDefOf.Misc,
                workerClass = typeof(global::SimWorld.Director.IncidentWorker_TraderCaravanArrival),
            };
            return (global::SimWorld.Director.IncidentWorker_TraderCaravanArrival)def.Worker;
        }

        // ---- content ----

        [Fact]
        public void TraderCaravanArrival_content_uses_the_real_worker()
        {
            Assert.Empty(Content.Result.Errors);
            global::SimWorld.Director.IncidentDef def = DefDatabase<global::SimWorld.Director.IncidentDef>.GetNamed("TraderCaravanArrival");
            Assert.Same(typeof(global::SimWorld.Director.IncidentWorker_TraderCaravanArrival), def.workerClass);
        }

        // ---- CanFireNow / faction+kind resolution ----

        [Fact]
        public void CanFireNow_false_without_any_faction_that_can_trade()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Find.FactionManager.Add(player);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            Assert.False(NewWorker().CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_false_when_the_only_non_hostile_faction_has_no_traderKinds()
        {
            Faction player = NewFaction(PlayerDef, "Player2");
            var noTraders = new FactionDef { defName = "NoTraders", isPlayer = false };
            Faction stranger = NewFaction(noTraders, "Stranger");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(stranger);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            Assert.False(NewWorker().CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_true_once_a_non_hostile_trading_faction_exists()
        {
            Faction player = NewFaction(PlayerDef, "Player3");
            Faction tribal = NewFaction(TribalDef, "Tribal3");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            Assert.True(NewWorker().CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_false_when_the_only_candidate_faction_is_hostile()
        {
            Faction player = NewFaction(PlayerDef, "Player4");
            Faction tribal = NewFaction(TribalDef, "Tribal4");
            tribal.SetRelationDirect(player, FactionRelationKind.Hostile, -100);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            Assert.False(NewWorker().CanFireNow(parms));
        }

        // ---- TryExecute: real trader, real stock, real prices ----

        [Fact]
        public void TryExecute_generates_a_real_trader_with_priced_stock_attributed_to_its_faction()
        {
            Faction player = NewFaction(PlayerDef, "Player5");
            Faction tribal = NewFaction(TribalDef, "Tribal5");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            Rand.Current = new RandomStream(55);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            global::SimWorld.Director.IncidentWorker_TraderCaravanArrival worker = NewWorker();
            bool result = worker.TryExecute(parms);

            Assert.True(result);
            Assert.Same(tribal, worker.LastTraderFaction);
            Assert.Same(tribal, parms.faction);
            Assert.Contains(worker.LastTraderKind, tribal.def.caravanTraderKinds!);
            Assert.NotNull(worker.LastTrader);
            Assert.Same(tribal, worker.LastTrader!.Faction);
            Assert.Same(worker.LastTraderKind, worker.LastTrader.TraderKind);

            var goods = worker.LastTrader.Goods.ToList();
            Assert.NotEmpty(goods);
            Assert.All(goods, g => Assert.True(g.count > 0));
            Assert.All(goods, g => Assert.True(TradeUtility.BaseMarketValue(g.def) > 0f));
            Assert.Contains(goods, g => g.def == EconomyThingDefOf.Silver);
        }

        [Fact]
        public void TryExecute_honors_a_forced_faction_in_parms()
        {
            Faction player = NewFaction(PlayerDef, "Player6");
            Faction tribal = NewFaction(TribalDef, "Tribal6");
            Faction outlander = NewFaction(OutlanderDef, "Outlander6");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            Find.FactionManager.Add(outlander);
            Rand.Current = new RandomStream(9);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, faction = outlander };

            global::SimWorld.Director.IncidentWorker_TraderCaravanArrival worker = NewWorker();
            Assert.True(worker.TryExecute(parms));

            Assert.Same(outlander, worker.LastTraderFaction);
        }

        [Fact]
        public void TryExecute_fails_when_forced_onto_a_faction_that_cannot_trade()
        {
            Faction player = NewFaction(PlayerDef, "Player7");
            var noTraders = new FactionDef { defName = "NoTraders2" };
            Faction stranger = NewFaction(noTraders, "Stranger2");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(stranger);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, faction = stranger };

            Assert.False(NewWorker().TryExecute(parms));
        }

        [Fact]
        public void TryExecute_never_picks_a_hostile_faction_when_not_forced()
        {
            Faction player = NewFaction(PlayerDef, "Player8");
            Faction tribal = NewFaction(TribalDef, "Tribal8");
            Faction outlander = NewFaction(OutlanderDef, "Outlander8");
            tribal.SetRelationDirect(player, FactionRelationKind.Hostile, -100);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            Find.FactionManager.Add(outlander);
            Rand.Current = new RandomStream(3);
            var target = new global::SimWorld.Director.CivilizationTarget();

            for (int i = 0; i < 20; i++)
            {
                var parms = new global::SimWorld.Director.IncidentParms { target = target };
                global::SimWorld.Director.IncidentWorker_TraderCaravanArrival worker = NewWorker();
                Assert.True(worker.TryExecute(parms));
                Assert.Same(outlander, worker.LastTraderFaction);
            }
        }

        // ---- a generated trader opens a real session against a real settlement ----

        [Fact]
        public void A_generated_traders_stock_can_open_a_real_session_against_a_settlement()
        {
            Faction player = NewFaction(PlayerDef, "Player9");
            Faction tribal = NewFaction(TribalDef, "Tribal9");
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(tribal);
            Rand.Current = new RandomStream(21);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target };

            global::SimWorld.Director.IncidentWorker_TraderCaravanArrival worker = NewWorker();
            Assert.True(worker.TryExecute(parms));

            var settlement = new global::SimWorld.World.Settlement(
                global::SimWorld.World.WorldObjectDefOf.Settlement, 0, player, "Capital", 0);
            settlement.SetStoreCount(EconomyThingDefOf.Silver, 100000);

            TradeSession session = SettlementTradeUtility.OpenSession(settlement, worker.LastTrader!, null);
            Assert.NotEmpty(session.deal.tradeables);
            Assert.NotNull(session.deal.silverTradeable);

            Tradeable line = session.deal.tradeables[0];
            line.countToTransfer = 1;
            bool executed = SettlementTradeUtility.TryExecute(session, settlement, null, out bool actuallyTraded);

            Assert.True(executed);
            Assert.True(actuallyTraded);
            Assert.Equal(1, settlement.StoreCountOf(line.thingDef));
        }
    }
}
