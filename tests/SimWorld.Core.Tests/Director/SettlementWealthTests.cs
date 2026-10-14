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

        // ---- a settlement nobody has opened ----

        private static void SpawnOwned(Town town, string defName, int count)
        {
            CoreMap map = town.Map!;
            int placed = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (placed >= count) break;
                if (!GenGrid.Standable(c, map) || GenGrid.GetThingList(c, map).Count != 0) continue;
                var building = (SimWorld.Building.Building)ThingMaker.MakeThing(Def(defName));
                building.faction = town.Player;
                GenSpawn.Spawn(building, c, map);
                placed++;
            }
            Assert.Equal(count, placed);
        }

        private static void Stock(Settlement settlement)
        {
            settlement.AddStore(Def("WoodLog"), 80);
            settlement.AddStore(Def("Steel"), 30);
            settlement.SetStructureCount(Def("Wall"), 12);
            settlement.SetStructureCount(Def("WallGranite"), 6);
            settlement.SetStructureCount(Def("Door"), 3);
        }

        [Fact]
        public void An_unopened_settlement_is_estimated_from_its_ledgers_and_its_roster()
        {
            Town town = Found("wealth-estimate", enter: false);
            Settlement settlement = town.Settlement;
            Stock(settlement);
            Assert.Null(settlement.InteriorMap);

            WealthBreakdown wealth = SettlementWealth.Of(settlement);

            float stores = 80 * global::SimWorld.Economy.TradeUtility.BaseMarketValue(Def("WoodLog"))
                + 30 * global::SimWorld.Economy.TradeUtility.BaseMarketValue(Def("Steel"));
            float gear = settlement.Citizens.Sum(c => WealthWatcher.GetEquipmentApparelAndInventoryWealth(c));
            float structures = 12 * Def("Wall").GetStatValue(StatDefOf.MarketValue)
                + 6 * Def("WallGranite").GetStatValue(StatDefOf.MarketValue)
                + 3 * Def("Door").GetStatValue(StatDefOf.MarketValue);
            float people = settlement.Citizens.Sum(c => Value(c));

            // Float sums in a different order: a tenth of a silver on twenty thousand is the same number.
            Assert.Equal(stores + gear, wealth.Items, 1);
            Assert.Equal(structures, wealth.Buildings, 1);
            Assert.Equal(people, wealth.Pawns, 1);
            Assert.True(wealth.Pawns > 0f && wealth.Buildings > 0f);
            Assert.Equal(wealth.Items + 0.5f * wealth.Buildings + wealth.Pawns, wealth.ForStoryteller, 1);

            // Reading it opened nothing: an estimate must never be what triggers a map to be generated.
            Assert.Null(settlement.InteriorMap);
        }

        [Fact]
        public void The_two_routes_agree_for_the_same_settlement()
        {
            Town town = Found("wealth-agree", enter: false);
            Settlement settlement = town.Settlement;
            Stock(settlement);
            WealthBreakdown estimated = SettlementWealth.Of(settlement);

            // Open it and build, on the ground, what the ledger said it had.
            town.Map = settlement.EnterMap(town.World);
            SpawnOwned(town, "Wall", 12);
            SpawnOwned(town, "WallGranite", 6);
            SpawnOwned(town, "Door", 3);
            town.Map.wealthWatcher.ForceRecount();
            WealthBreakdown watched = SettlementWealth.Of(settlement);

            // The people and the buildings are the same people and buildings, priced by the same stat.
            Assert.Equal(estimated.Pawns, watched.Pawns, 1);
            Assert.Equal(estimated.Buildings, watched.Buildings, 1);

            // The goods differ by exactly what the map generator left lying about (a ruin's loot), which the
            // ledger of an unopened settlement cannot know.
            float lying = town.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Sum(t => Value(t) * t.stackCount);
            Assert.Equal(estimated.Items + lying, watched.Items, 1);

            // And the figure the storyteller reads moves by under two percent when the map is opened.
            float drift = System.Math.Abs(watched.ForStoryteller - estimated.ForStoryteller) / estimated.ForStoryteller;
            Assert.InRange(drift, 0f, 0.02f);
        }

        [Fact]
        public void A_ledger_structure_counts_at_half_weight_like_a_standing_one()
        {
            Town town = Found("wealth-ledger-half", enter: false);
            Settlement settlement = town.Settlement;
            float before = SettlementWealth.Of(settlement).ForStoryteller;

            settlement.AddStructure(Def("Wall"), 10);
            Assert.Equal(0.5f * 10 * Def("Wall").GetStatValue(StatDefOf.MarketValue), SettlementWealth.Of(settlement).ForStoryteller - before, 2);
        }

        [Fact]
        public void A_new_citizen_raises_an_unopened_settlements_wealth_by_their_market_value()
        {
            Town town = Found("wealth-newcomer", enter: false);
            Settlement settlement = town.Settlement;
            float before = SettlementWealth.Of(settlement).ForStoryteller;

            Pawn newcomer = NewHuman("Newcomer");
            foreach (var skill in newcomer.skills.skills) skill.Level = 6;
            settlement.AddCitizen(newcomer);

            Assert.Equal(Value(newcomer), SettlementWealth.Of(settlement).ForStoryteller - before, 2);
        }

        [Fact]
        public void The_dead_and_the_uncounted_cohort_are_not_wealth()
        {
            Town town = Found("wealth-dead", enter: false);
            Settlement settlement = town.Settlement;
            float before = SettlementWealth.Of(settlement).ForStoryteller;

            settlement.AddStatisticalPeople(500);
            Assert.Equal(before, SettlementWealth.Of(settlement).ForStoryteller, 3);

            Pawn victim = settlement.Citizens[0];
            // The person and what they carried and wore go together.
            float victimValue = Value(victim) + WealthWatcher.GetEquipmentApparelAndInventoryWealth(victim);
            Assert.True(victimValue > 0f);
            victim.health.Kill(null, null);
            Assert.Equal(before - victimValue, SettlementWealth.Of(settlement).ForStoryteller, 1);
        }

        // ---- what the storyteller does with it ----

        [Fact]
        public void The_same_colony_draws_bigger_raids_when_it_holds_and_has_built_more()
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"), DefDatabase<DifficultyDef>.GetNamed("Medium"));
            Find.Storyteller = storyteller;
            Find.TickManager.DebugSetTicksGame(45 * GenDate.TicksPerDay);

            Town town = Found("wealth-points", enter: false);
            var target = new CivilizationTarget(storyteller);
            target.SetSettlements(new[] { town.Settlement });
            float poor = StorytellerUtility.DefaultThreatPointsNow(target);
            float poorWealth = target.PlayerWealthForStoryteller;

            // Same people, same day. Only what the settlement has gone and made differs.
            town.Settlement.AddStore(Def("Steel"), 4000);
            town.Settlement.SetStructureCount(Def("Wall"), 800);
            town.Settlement.SetStructureCount(Def("Bed"), 60);
            float rich = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.True(target.PlayerWealthForStoryteller > poorWealth);
            Assert.True(rich > poor, "a settlement that has built and stocked more must draw a bigger raid than the same people with nothing");
        }

        [Fact]
        public void A_person_is_charged_for_by_the_wealth_term_as_well_as_the_roster_term()
        {
            // RimWorld's people are counted twice (the per-colonist term and WealthPawns), and this is the
            // sign of it: with the wealth term live, a newcomer raises the points by more than the roster
            // alone would, because the same newcomer has also raised the wealth the curves are read at.
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"), DefDatabase<DifficultyDef>.GetNamed("Medium"));
            Find.Storyteller = storyteller;
            Find.TickManager.DebugSetTicksGame(45 * GenDate.TicksPerDay);

            Town town = Found("wealth-people", enter: false);
            var counted = new CivilizationTarget(storyteller);
            counted.SetSettlements(new[] { town.Settlement });
            town.Settlement.AddStore(Def("Steel"), 7000);
            float wealthBefore = counted.PlayerWealthForStoryteller;
            Assert.True(wealthBefore > 14000f, "fixture: the wealth term must be live");

            Pawn newcomer = NewHuman("Newcomer");
            foreach (var skill in newcomer.skills.skills) skill.Level = 6;
            town.Settlement.AddCitizen(newcomer);

            // The same roster, with the wealth frozen at what it was before the newcomer arrived.
            var rosterOnly = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = wealthBefore };
            rosterOnly.pawns.AddRange(town.Settlement.Citizens);

            Assert.True(counted.PlayerWealthForStoryteller > wealthBefore);
            Assert.True(StorytellerUtility.DefaultThreatPointsNow(counted) > StorytellerUtility.DefaultThreatPointsNow(rosterOnly));
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
