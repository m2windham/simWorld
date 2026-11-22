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
using TradeTrustTier = global::SimWorld.Economy.TradeTrustTier;
using TradeTrustTuning = global::SimWorld.Economy.TradeTrustTuning;
using TraderArrival = global::SimWorld.Economy.TraderArrival;
using TraderKindDef = global::SimWorld.Economy.TraderKindDef;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// REQ_006 (host): <see cref="VisitingTraderView.TrustReason"/> is the sentence
    /// <see cref="GodCommands.BuyFromTrader"/> and <see cref="GodCommands.SellToTrader"/> refuse with on trust
    /// grounds, word for word, in every tier — and "Ready to trade." exactly when they do not refuse on trust.
    /// </summary>
    public class TradeTrustReasonTests : ContentTestBase
    {
        public TradeTrustReasonTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Thing(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        [Theory]
        [InlineData(-80, false, TradeTrustTier.Hostile)]
        [InlineData(0, true, TradeTrustTier.Hostile)]
        [InlineData(-30, false, TradeTrustTier.Wary)]
        [InlineData(20, false, TradeTrustTier.Neutral)]
        [InlineData(80, false, TradeTrustTier.Trusted)]
        public void The_views_trust_reason_is_the_commands_refusal_in_every_tier(int goodwill, bool atWar, TradeTrustTier tier)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                "trust-reason-" + goodwill + "-" + atWar, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "TrustReasonWorld", 2, soloStart: true);
            Find.World = world;
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && !taken.Contains(i));
            Faction home = Find.FactionManager.OfPlayer!;
            var town = new Settlement(WorldObjectDefOf.Settlement, tile, home, "Hearth", 0);
            world.worldObjects.Add(town);

            var seller = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Riverfolk", "F_Riverfolk");
            world.factions.Add(seller);
            Find.FactionManager.Add(seller);
            home.SetRelationDirect(seller, Faction.KindFromGoodwill(goodwill), goodwill);
            if (atWar) Assert.True(home.DeclareWar(seller));
            Assert.NotNull(TraderArrival.Land(
                town, DefDatabase<TraderKindDef>.GetNamed("Caravan_Outlander_BulkGoods"), seller,
                new List<(ThingDef, int)> { (Thing("Steel"), 50), (Thing("Silver"), 100000) }, new RandomStream(4242)));
            town.SetStoreCount(EconomyThingDefOf.Silver, 100000);
            town.SetStoreCount(Thing("Cloth"), 50);

            VisitingTraderView view = Assert.Single(GodViewSnapshot.Capture().VisitingTraders);
            Assert.Equal(TradeTrustTuning.Tiers[(int)tier].Label, view.TrustTier);
            Assert.False(string.IsNullOrEmpty(view.TrustReason));

            GodCommandResult buy = GodCommands.BuyFromTrader(town.tile, "Steel", 1);
            GodCommandResult sell = GodCommands.SellToTrader(town.tile, "Cloth", 1);
            if (view.TrustAllowsTrade)
            {
                Assert.Equal("Ready to trade.", view.TrustReason);
                Assert.Equal(GodCommandOutcome.Done, buy.Outcome);
                Assert.Equal(GodCommandOutcome.Done, sell.Outcome);
            }
            else
            {
                Assert.Equal(GodCommandOutcome.Refused, buy.Outcome);
                Assert.Equal(GodCommandOutcome.Refused, sell.Outcome);
                Assert.Equal(view.TrustReason, buy.Reason);
                Assert.Equal(view.TrustReason, sell.Reason);
                Assert.Contains(atWar ? "at war" : "hostile", view.TrustReason);
            }
        }
    }
}
