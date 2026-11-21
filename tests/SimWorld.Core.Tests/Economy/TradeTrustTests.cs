using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreWorld = SimWorld.World.World;
using EconomyThingDefOf = global::SimWorld.Economy.EconomyThingDefOf;
using IncidentWorker_TraderCaravanArrival = global::SimWorld.Director.IncidentWorker_TraderCaravanArrival;
using PriceType = global::SimWorld.Economy.PriceType;
using TradeTrust = global::SimWorld.Economy.TradeTrust;
using TradeTrustTier = global::SimWorld.Economy.TradeTrustTier;
using TradeTrustTuning = global::SimWorld.Economy.TradeTrustTuning;
using TradeUtility = global::SimWorld.Economy.TradeUtility;
using TraderArrival = global::SimWorld.Economy.TraderArrival;
using TraderCaravan = global::SimWorld.Economy.TraderCaravan;
using TraderKindDef = global::SimWorld.Economy.TraderKindDef;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// Trade on reputation (Doug, 2026-11-20): only our own settlements trade; goodwill bands the price;
    /// hostile civilizations neither trade nor send caravans; trade and gifts move goodwill.
    /// Every tuned figure is read back from <see cref="TradeTrustTuning"/> rather than restated.
    /// </summary>
    public class TradeTrustTests : ContentTestBase
    {
        public TradeTrustTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Thing(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static Faction NewFaction(string factionDefName, string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed(factionDefName), name, "F_" + name);

        /// <summary>A world with one settlement owned by <paramref name="ownerIsPlayer"/>'s side: ours, or a
        /// rival's while ours exists elsewhere in the faction manager.</summary>
        private static (CoreWorld world, Settlement settlement, Faction home) PosedWorld(string seed, bool ownerIsPlayer = true)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "TrustWorld", 2, soloStart: true);
            Find.World = world;

            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && !taken.Contains(i));

            // World generation already made the player's civilization: that is "ours".
            Faction home = Find.FactionManager.OfPlayer!;

            Faction owner = home;
            if (!ownerIsPlayer)
            {
                owner = NewFaction("OutlanderCivilization", "Rival");
                world.factions.Add(owner);
                Find.FactionManager.Add(owner);
            }

            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, owner, "Hearth", 0);
            world.worldObjects.Add(settlement);
            return (world, settlement, home);
        }

        private static (TraderCaravan caravan, Faction seller) LandCaravan(
            CoreWorld world, Settlement settlement, Faction home, int goodwill, params (string defName, int count)[] stock)
        {
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");
            world.factions.Add(seller);
            Find.FactionManager.Add(seller);
            home.SetRelationDirect(seller, Faction.KindFromGoodwill(goodwill), goodwill);

            TraderCaravan? caravan = TraderArrival.Land(
                settlement, DefDatabase<TraderKindDef>.GetNamed("Caravan_Outlander_BulkGoods"), seller,
                stock.Select(e => (Thing(e.defName), e.count)).ToList(), new RandomStream(4242));
            Assert.NotNull(caravan);
            return (caravan!, seller);
        }

        // -------------------------------------------------------------------------------------------
        // Ownership (a defect, not balance).
        // -------------------------------------------------------------------------------------------

        /// <summary>A rival's settlement with a caravan at it: buying used to spend the rival's own silver.
        /// Every verb is refused, in words, and nothing moves.</summary>
        [Fact]
        public void Trading_at_a_settlement_that_is_not_ours_is_refused_and_spends_nothing()
        {
            (CoreWorld world, Settlement rivalTown, Faction home) = PosedWorld("trust-owner", ownerIsPlayer: false);
            (TraderCaravan caravan, _) = LandCaravan(world, rivalTown, home, 0, ("Steel", 50), ("Silver", 5000));
            rivalTown.SetStoreCount(EconomyThingDefOf.Silver, 5000);
            rivalTown.SetStoreCount(Thing("Cloth"), 50);

            GodCommandResult buy = GodCommands.BuyFromTrader(rivalTown.tile, "Steel", 5);
            GodCommandResult sell = GodCommands.SellToTrader(rivalTown.tile, "Cloth", 5);
            GodCommandResult gift = GodCommands.GiftToTrader(rivalTown.tile, "Silver", 100);

            foreach (GodCommandResult r in new[] { buy, sell, gift })
            {
                Assert.Equal(GodCommandOutcome.Refused, r.Outcome);
                Assert.Contains("Hearth is not one of ours", r.Reason);
            }
            Assert.Equal(5000, rivalTown.StoreCountOf(EconomyThingDefOf.Silver));
            Assert.Equal(50, rivalTown.StoreCountOf(Thing("Cloth")));
            Assert.Equal(0, rivalTown.StoreCountOf(Thing("Steel")));
            Assert.Equal(50, caravan.StockOf(Thing("Steel")));

            // And the read model does not offer it to us either.
            Assert.Empty(GodViewSnapshot.Capture().VisitingTraders);
        }

        // -------------------------------------------------------------------------------------------
        // Hostile: no trade (gifts still welcome).
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_hostile_caravan_will_not_trade_but_will_accept_a_gift()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-hostile");
            (_, Faction seller) = LandCaravan(world, town, home, -80, ("Steel", 50), ("Silver", 5000));
            town.SetStoreCount(EconomyThingDefOf.Silver, 5000);
            town.SetStoreCount(Thing("Cloth"), 50);

            GodCommandResult buy = GodCommands.BuyFromTrader(town.tile, "Steel", 5);
            GodCommandResult sell = GodCommands.SellToTrader(town.tile, "Cloth", 5);

            Assert.Equal(GodCommandOutcome.Refused, buy.Outcome);
            Assert.Equal(GodCommandOutcome.Refused, sell.Outcome);
            Assert.Contains("hostile", buy.Reason);
            Assert.Contains("goodwill -80", sell.Reason);
            Assert.Equal(5000, town.StoreCountOf(EconomyThingDefOf.Silver));
            Assert.Equal(50, town.StoreCountOf(Thing("Cloth")));

            VisitingTraderView view = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            Assert.Equal("Hostile", view.TrustTier);
            Assert.False(view.TrustAllowsTrade);
            Assert.Equal(-80, view.FactionGoodwill);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.GiftToTrader(town.tile, "Silver", 400).Outcome);
            Assert.True(home.GoodwillWith(seller) > -80, "a gift is how a soured relation is mended");
        }

        /// <summary>War is its own state here, separate from goodwill: a civilization at war refuses trade
        /// even when goodwill alone would not call it hostile.</summary>
        [Fact]
        public void A_caravan_whose_civilization_is_at_war_with_us_will_not_trade()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-war");
            (_, Faction seller) = LandCaravan(world, town, home, 0, ("Steel", 50), ("Silver", 5000));
            Assert.True(home.DeclareWar(seller));
            home.TryAffectGoodwillWith(seller, 150);
            Assert.False(home.HostileTo(seller));
            Assert.True(home.WarWith(seller));
            town.SetStoreCount(EconomyThingDefOf.Silver, 5000);

            GodCommandResult buy = GodCommands.BuyFromTrader(town.tile, "Steel", 5);

            Assert.Equal(GodCommandOutcome.Refused, buy.Outcome);
            Assert.Contains("at war", buy.Reason);
            Assert.Equal(5000, town.StoreCountOf(EconomyThingDefOf.Silver));
        }

        // -------------------------------------------------------------------------------------------
        // Tiers: one price factor, quoted and charged alike.
        // -------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(-30, TradeTrustTier.Wary)]
        [InlineData(20, TradeTrustTier.Neutral)]
        [InlineData(80, TradeTrustTier.Trusted)]
        public void Each_tiers_price_factor_is_in_the_quote_and_in_the_charge(int goodwill, TradeTrustTier tier)
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-tier-" + goodwill);
            LandCaravan(world, town, home, goodwill, ("Steel", 50), ("Silver", 100000));
            town.SetStoreCount(EconomyThingDefOf.Silver, 100000);
            town.SetStoreCount(Thing("Cloth"), 400);

            float factor = TradeTrustTuning.Tiers[(int)tier].PriceFactor;
            VisitingTraderView view = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            Assert.Equal(TradeTrustTuning.Tiers[(int)tier].Label, view.TrustTier);
            Assert.Equal(factor, view.TrustBuyPriceFactor);
            Assert.Equal(1f / factor, view.TrustSellPriceFactor);

            // Quote: TradeUtility's own expression with the factor in its own parameter (nobody negotiates
            // here, so no other gain is folded in).
            TradeLineView steel = view.Goods.Single(g => g.DefName == "Steel");
            TradeLineView cloth = view.Goods.Single(g => g.DefName == "Cloth");
            Assert.Equal(TradeUtility.GetPricePlayerBuy(Thing("Steel"), PriceType.Normal, factor), steel.UnitBuyPrice);
            Assert.Equal(TradeUtility.GetPricePlayerSell(Thing("Cloth"), PriceType.Normal, 1f / factor), cloth.UnitSellPrice);

            // Charge: exactly the quote.
            int before = town.StoreCountOf(EconomyThingDefOf.Silver);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.BuyFromTrader(town.tile, "Steel", 7).Outcome);
            int afterBuy = town.StoreCountOf(EconomyThingDefOf.Silver);
            Assert.Equal((int)System.Math.Round(steel.UnitBuyPrice * 7), before - afterBuy);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SellToTrader(town.tile, "Cloth", 300).Outcome);
            Assert.Equal((int)System.Math.Round(cloth.UnitSellPrice * 300), town.StoreCountOf(EconomyThingDefOf.Silver) - afterBuy);
        }

        /// <summary>The tiers order the way their names say: worse for the wary, better for the trusted.</summary>
        [Fact]
        public void Better_trust_buys_cheaper_and_sells_dearer()
        {
            float Buy(int goodwill, string seed)
            {
                (CoreWorld world, Settlement town, Faction home) = PosedWorld(seed);
                LandCaravan(world, town, home, goodwill, ("Steel", 50), ("Silver", 1000));
                return Assert.Single(GodViewSnapshot.Capture().VisitingTraders).Goods.Single(g => g.DefName == "Steel").UnitBuyPrice;
            }

            float wary = Buy(-30, "trust-order-a");
            Find.Reset();
            Find.TickManager = new TickManager();
            float neutral = Buy(20, "trust-order-b");
            Find.Reset();
            Find.TickManager = new TickManager();
            float trusted = Buy(80, "trust-order-c");

            Assert.True(wary > neutral, "a wary partner should charge us more");
            Assert.True(trusted < neutral, "a trusted partner should charge us less");
        }

        // -------------------------------------------------------------------------------------------
        // Trade raises goodwill, pooled per day and capped.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_completed_trade_raises_goodwill_by_its_value()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-gain");
            (_, Faction seller) = LandCaravan(world, town, home, 10, ("Steel", 500), ("Silver", 1000));
            town.SetStoreCount(EconomyThingDefOf.Silver, 100000);

            int before = town.StoreCountOf(EconomyThingDefOf.Silver);
            GodCommandResult result = GodCommands.BuyFromTrader(town.tile, "Steel", 200);
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            int paid = before - town.StoreCountOf(EconomyThingDefOf.Silver);

            int expected = System.Math.Min(TradeTrustTuning.TradeGoodwillDailyCap, (int)(paid / TradeTrustTuning.TradeValuePerGoodwill));
            Assert.True(expected > 0, "the fixture trade should be worth at least one goodwill");
            Assert.Equal(10 + expected, home.GoodwillWith(seller));
            Assert.Contains("Goodwill with Riverfolk rose by " + expected, result.Reason);
        }

        /// <summary>One-unit spam earns exactly what one big trade would, and neither earns past the daily cap;
        /// the next day the pool starts again.</summary>
        [Fact]
        public void One_unit_spam_cannot_farm_goodwill_past_the_daily_cap()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-spam");
            (_, Faction seller) = LandCaravan(world, town, home, 0, ("Steel", 5000), ("Silver", 1000000));
            town.SetStoreCount(EconomyThingDefOf.Silver, 1000000);

            int start = town.StoreCountOf(EconomyThingDefOf.Silver);
            int spent = 0;
            for (int i = 0; i < 2000; i++)
            {
                Assert.Equal(GodCommandOutcome.Done, GodCommands.BuyFromTrader(town.tile, "Steel", 1).Outcome);
                spent = start - town.StoreCountOf(EconomyThingDefOf.Silver);
                int expected = System.Math.Min(TradeTrustTuning.TradeGoodwillDailyCap, (int)(spent / TradeTrustTuning.TradeValuePerGoodwill));
                Assert.Equal(expected, home.GoodwillWith(seller));
            }
            Assert.True(spent > TradeTrustTuning.TradeValuePerGoodwill * (TradeTrustTuning.TradeGoodwillDailyCap + 1),
                "the spam should have been worth more than the cap allows");
            Assert.Equal(TradeTrustTuning.TradeGoodwillDailyCap, home.GoodwillWith(seller));

            // A new day, a new pool.
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerDay);
            Assert.Equal(1, TradeTrust.RecordTrade(home, seller, TradeTrustTuning.TradeValuePerGoodwill));
        }

        /// <summary>The pool survives a save, or a reload would reset the cap.</summary>
        [Fact]
        public void The_days_trade_pool_survives_a_save()
        {
            (CoreWorld world, _, Faction home) = PosedWorld("trust-scribe");
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");
            world.factions.Add(seller);
            home.SetRelationDirect(seller, FactionRelationKind.Neutral, 0);
            TradeTrust.RecordTrade(home, seller, 350f);

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Faction loadedHome = loaded.factions.Single(f => f.name == home.name);
            FactionRelation rel = loadedHome.Relations.Single(r => r.other?.name == "Riverfolk");
            Assert.Equal(home.RelationWith(seller)!.tradeTrustDay, rel.tradeTrustDay);
            Assert.Equal(350f, rel.tradeTrustValue);
        }

        // -------------------------------------------------------------------------------------------
        // Gifts (RimWorld: FactionGiftUtility).
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_gift_raises_goodwill_by_its_value_and_the_view_quoted_it()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-gift");
            (TraderCaravan caravan, Faction seller) = LandCaravan(world, town, home, 0, ("Steel", 10), ("Silver", 100));
            town.SetStoreCount(EconomyThingDefOf.Silver, 400);
            town.SetStoreCount(Thing("Steel"), 60);

            VisitingTraderView view = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            int quotedSilver = view.GoodwillIfAllSilverGifted;
            Assert.Equal((int)(400 / TradeTrustTuning.GiftValuePerGoodwill), quotedSilver);

            GodCommandResult result = GodCommands.GiftToTrader(town.tile, "Silver", 400);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(quotedSilver, home.GoodwillWith(seller));
            Assert.Equal(0, town.StoreCountOf(EconomyThingDefOf.Silver));
            Assert.Equal(500, caravan.StockOf(EconomyThingDefOf.Silver));

            int quotedSteel = Assert.Single(GodViewSnapshot.Capture().VisitingTraders)
                .Goods.Single(g => g.DefName == "Steel").GoodwillIfAllGifted;
            int beforeSteel = home.GoodwillWith(seller);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.GiftToTrader(town.tile, "Steel", 60).Outcome);
            Assert.Equal(beforeSteel + quotedSteel, home.GoodwillWith(seller));
            Assert.Equal(70, caravan.StockOf(Thing("Steel")));
        }

        /// <summary>RimWorld's gift curve: full effect up to 75 (ally), diminishing above it, never past 100.</summary>
        [Fact]
        public void Gifts_to_an_ally_count_for_less_and_never_pass_one_hundred()
        {
            Faction home = NewFaction("PlayerCivilization", "Home");
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");

            home.SetRelationDirect(seller, FactionRelationKind.Neutral, 0);
            int fromZero = TradeTrust.GiftGoodwillChange(home, seller, 400f);
            home.SetRelationDirect(seller, FactionRelationKind.Ally, 80);
            int fromAlly = TradeTrust.GiftGoodwillChange(home, seller, 400f);
            int huge = TradeTrust.GiftGoodwillChange(home, seller, 1_000_000f);

            Assert.Equal(10, fromZero);
            Assert.True(fromAlly < fromZero, "a gift to an ally should count for less");
            Assert.Equal(20, huge);
        }

        [Fact]
        public void Giving_what_the_settlement_does_not_hold_is_refused()
        {
            (CoreWorld world, Settlement town, Faction home) = PosedWorld("trust-gift-refuse");
            LandCaravan(world, town, home, 0, ("Steel", 10), ("Silver", 100));
            town.SetStoreCount(EconomyThingDefOf.Silver, 50);

            GodCommandResult result = GodCommands.GiftToTrader(town.tile, "Silver", 51);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("holds only 50", result.Reason);
            Assert.Equal(50, town.StoreCountOf(EconomyThingDefOf.Silver));
        }

        // -------------------------------------------------------------------------------------------
        // Drift: trust earned is trust that fades.
        // -------------------------------------------------------------------------------------------

        /// <summary>Goodwill above the faction's natural range drifts back toward it, so trust is upkeep.</summary>
        [Fact]
        public void Trust_above_natural_goodwill_drifts_back_down()
        {
            Faction home = NewFaction("PlayerCivilization", "Home");
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");
            Find.FactionManager.Add(home);
            Find.FactionManager.Add(seller);
            home.SetRelationDirect(seller, FactionRelationKind.Ally, 80);

            for (int t = Faction.GoodwillCheckInterval; t <= GenDate.TicksPerDay * 4; t += Faction.GoodwillCheckInterval)
            {
                Find.TickManager.DebugSetTicksGame(t);
                seller.FactionTick();
            }

            Assert.True(home.GoodwillWith(seller) < 80, "natural drift should pull trust back down");
        }

        // -------------------------------------------------------------------------------------------
        // Caravan arrival.
        // -------------------------------------------------------------------------------------------

        private static IncidentWorker_TraderCaravanArrival NewWorker()
        {
            var def = new global::SimWorld.Director.IncidentDef
            {
                defName = "TestTrustCaravanArrival",
                category = global::SimWorld.Director.IncidentCategoryDefOf.Misc,
                workerClass = typeof(IncidentWorker_TraderCaravanArrival),
            };
            return (IncidentWorker_TraderCaravanArrival)def.Worker;
        }

        /// <summary>RimWorld sends no caravans from hostile factions; here a civilization at war with us sends
        /// none either, even when its goodwill is not in the hostile band.</summary>
        [Fact]
        public void A_civilization_at_war_with_us_sends_no_caravans()
        {
            Find.FactionManager = new FactionManager();
            Faction home = NewFaction("PlayerCivilization", "Home");
            Faction seller = NewFaction("TribalCivilization", "Riverfolk");
            Find.FactionManager.Add(home);
            Find.FactionManager.Add(seller);
            home.SetRelationDirect(seller, FactionRelationKind.Neutral, 0);
            var parms = new global::SimWorld.Director.IncidentParms { target = new global::SimWorld.Director.CivilizationTarget() };
            Assert.True(NewWorker().CanFireNow(parms));

            home.DeclareWar(seller);
            home.TryAffectGoodwillWith(seller, 150);
            Assert.False(home.HostileTo(seller));

            Assert.False(NewWorker().CanFireNow(parms));
        }

        /// <summary>A trusted partner is entered twice in the draw for who sends the next caravan.</summary>
        [Fact]
        public void A_trusted_civilizations_caravans_come_more_often()
        {
            Find.FactionManager = new FactionManager();
            Faction home = NewFaction("PlayerCivilization", "Home");
            Faction trusted = NewFaction("TribalCivilization", "Friends");
            Faction neutral = NewFaction("OutlanderCivilization", "Strangers");
            Find.FactionManager.Add(home);
            Find.FactionManager.Add(trusted);
            Find.FactionManager.Add(neutral);
            home.SetRelationDirect(trusted, FactionRelationKind.Ally, 80);
            home.SetRelationDirect(neutral, FactionRelationKind.Neutral, 20);
            Rand.Current = new RandomStream(77);

            int fromTrusted = 0, fromNeutral = 0;
            for (int i = 0; i < 300; i++)
            {
                IncidentWorker_TraderCaravanArrival worker = NewWorker();
                Assert.True(worker.TryExecute(new global::SimWorld.Director.IncidentParms
                {
                    target = new global::SimWorld.Director.CivilizationTarget(),
                }));
                if (ReferenceEquals(worker.LastTraderFaction, trusted)) fromTrusted++;
                else if (ReferenceEquals(worker.LastTraderFaction, neutral)) fromNeutral++;
            }

            Assert.Equal(300, fromTrusted + fromNeutral);
            Assert.True(fromTrusted > fromNeutral * 1.5f, "trusted " + fromTrusted + " vs neutral " + fromNeutral);
        }
    }
}
