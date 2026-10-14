using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="SettlementWealth"/>: what one real settlement is worth to the storyteller, on a settlement
    /// founded the ordinary way. The map half is <see cref="WealthWatcherTests"/>' concern; this is the
    /// seam between a map and the settlement that owns it — the goods that are in the ledger rather than on the
    /// ground, the people the tiering system has taken off the map, the buildings a citizen finished.
    /// </summary>
    public class SettlementWealthTests : ContentTestBase
    {
        public SettlementWealthTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        private sealed class Town
        {
            public CoreWorld World = null!;
            public Settlement Settlement = null!;
            public Faction Player = null!;
            public CoreMap? Map;
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static float Value(Thing thing) => thing.GetStatValue(StatDefOf.MarketValue);

        private static Town Found(string seed, int band = 20, bool enter = true)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 2, soloStart: true);
            Faction player = world.factions.First();
            Find.FactionManager.Add(player);
            Assert.Same(player, Find.FactionManager.OfPlayer);
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, player, band, new RandomStream(4141), "Wealthton");
            return new Town { World = world, Settlement = settlement, Player = player, Map = enter ? settlement.EnterMap(world) : null };
        }

        /// <summary>Builds <paramref name="defName"/> the way a settlement does: blueprint, frame, a citizen finishing it.</summary>
        private static Thing BuildThrough(Town town, string defName)
        {
            CoreMap map = town.Map!;
            IntVec3 cell = map.AllCells.First(c => GenGrid.Standable(c, map) && GenGrid.GetThingList(c, map).Count == 0 && !map.roofGrid.Roofed(c));
            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_" + defName));
            GenSpawn.Spawn(blueprint, cell, map);
            SimWorld.Building.Frame frame = ((SimWorld.Building.Blueprint)blueprint).ReplaceWithFrame();
            foreach (ThingDefCountClass cost in Def(defName).costList!) frame.AddMaterial(cost.thingDef, cost.count);
            return frame.CompleteConstruction(town.Settlement.Citizens[0]);
        }

        // ---- a settlement with a map ----

        [Fact]
        public void A_settlement_with_a_map_is_its_map_plus_its_ledger_plus_its_people_off_the_map()
        {
            Town town = Found("wealth-watched");
            Settlement settlement = town.Settlement;
            ThingDef logs = Def("WoodLog");
            settlement.AddStore(logs, 100);
            town.Map!.wealthWatcher.ForceRecount();

            WealthBreakdown wealth = SettlementWealth.Of(settlement);
            WealthWatcher watcher = town.Map.wealthWatcher;

            Assert.Equal(watcher.WealthItems + 100 * global::SimWorld.Economy.TradeUtility.BaseMarketValue(logs), wealth.Items, 3);
            Assert.Equal(watcher.WealthBuildings, wealth.Buildings, 3);
            Assert.Equal(watcher.WealthPawns, wealth.Pawns, 3);
            Assert.Equal(wealth.Items + 0.5f * wealth.Buildings + wealth.Pawns, wealth.ForStoryteller, 3);

            // Everyone is standing on the map, so the watcher alone has them all.
            Assert.All(settlement.Citizens, c => Assert.True(c.Spawned));
            Assert.True(wealth.Pawns > 0f);
        }

        [Fact]
        public void The_civilization_reports_what_its_settlements_are_worth()
        {
            Town town = Found("wealth-target");
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { town.Settlement });
            town.Map!.wealthWatcher.ForceRecount();

            Assert.Equal(SettlementWealth.Of(town.Settlement).ForStoryteller, target.PlayerWealthForStoryteller, 3);
            Assert.True(target.PlayerWealthForStoryteller > 0f);
        }

        [Fact]
        public void A_citizen_taken_off_the_map_is_still_the_settlements()
        {
            Town town = Found("wealth-offmap");
            Settlement settlement = town.Settlement;
            town.Map!.wealthWatcher.ForceRecount();
            float before = SettlementWealth.Of(settlement).Pawns;

            Pawn leaver = settlement.Citizens[0];
            leaver.tier.Notify_AttentionChanged(false);
            settlement.SyncCitizenSpawns();
            Assert.False(leaver.Spawned, "fixture: the citizen must really be off the map");
            town.Map.wealthWatcher.ForceRecount();

            Assert.Equal(before, SettlementWealth.Of(settlement).Pawns, 2);
        }

        [Fact]
        public void Banking_a_stack_moves_wealth_between_the_two_halves_and_never_changes_it()
        {
            Town town = Found("wealth-bank");
            CoreMap map = town.Map!;
            ThingDef logs = Def("WoodLog");
            IntVec3 cell = map.AllCells.First(c => GenGrid.Standable(c, map) && GenGrid.GetThingList(c, map).Count == 0);
            Thing stack = ThingMaker.MakeThing(logs);
            stack.stackCount = 60;
            GenSpawn.Spawn(stack, cell, map);
            map.wealthWatcher.ForceRecount();
            float onTheGround = SettlementWealth.Of(town.Settlement).ForStoryteller;

            // Exactly what SettlementStockInitiative does: the map-side stack is destroyed in the same breath
            // the ledger is credited.
            stack.Destroy();
            town.Settlement.AddStore(logs, 60);
            map.wealthWatcher.ForceRecount();

            Assert.Equal(onTheGround, SettlementWealth.Of(town.Settlement).ForStoryteller, 3);
        }

        // ---- buildings ----

        [Fact]
        public void What_a_citizen_builds_is_the_settlements_and_what_map_generation_left_is_not()
        {
            Town town = Found("wealth-built");
            CoreMap map = town.Map!;
            map.wealthWatcher.ForceRecount();

            // Whatever the generator put on the map — ruined walls, today — belongs to nobody.
            IReadOnlyList<Thing> standing = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            Assert.All(standing.OfType<SimWorld.Building.Building>(), b => Assert.Null(b.faction));
            Assert.Equal(0f, map.wealthWatcher.WealthBuildings, 3);

            Thing wall = BuildThrough(town, "Wall");
            Assert.Same(town.Player, ((SimWorld.Building.Building)wall).faction);

            map.wealthWatcher.ForceRecount();
            Assert.Equal(Value(wall), map.wealthWatcher.WealthBuildings, 3);
            Assert.Equal(Value(wall), SettlementWealth.Of(town.Settlement).Buildings, 3);
        }

        // ---- save and load ----

        [Fact]
        public void A_building_keeps_its_owner_through_a_save_and_the_wealth_reads_the_same_after()
        {
            Town town = Found("wealth-scribe");
            BuildThrough(town, "Wall");
            BuildThrough(town, "Door");
            town.Settlement.AddStore(Def("WoodLog"), 25);
            town.Map!.wealthWatcher.ForceRecount();
            WealthBreakdown before = SettlementWealth.Of(town.Settlement);
            Assert.True(before.Buildings > 0f);

            string xml = Scribe.SaveToString(town.World, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Find.FactionManager = new FactionManager();
            foreach (Faction f in loaded.factions) Find.FactionManager.Add(f);
            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Wealthton");
            Assert.NotNull(reloaded.InteriorMap);

            var owned = reloaded.InteriorMap!.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .OfType<SimWorld.Building.Building>().Where(b => b.faction != null).ToList();
            Assert.Equal(2, owned.Count);
            Assert.All(owned, b => Assert.Same(Find.FactionManager.OfPlayer, b.faction));

            // Nothing was saved from the count; the first read after the load counts the map afresh.
            WealthBreakdown after = SettlementWealth.Of(reloaded);
            Assert.Equal(before.Items, after.Items, 2);
            Assert.Equal(before.Buildings, after.Buildings, 2);
            Assert.Equal(before.Pawns, after.Pawns, 2);
        }
    }
}
