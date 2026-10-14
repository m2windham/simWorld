using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="WealthWatcher"/> on a real map: what the storyteller sees of a settlement that has one.
    /// Each test changes exactly one thing about a map and asserts exactly how much the reading moves, and by
    /// which of the three terms — a building by half its market value, a person by theirs, a hauled stack not
    /// at all. The amounts are always derived from the same stat the watcher reads, never typed in, so the
    /// tests pin the <i>accounting</i> (what counts, what is weighted, when it is recounted) and leave the
    /// prices to <c>PawnMarketValueTests</c>.
    /// </summary>
    public class WealthWatcherTests : ContentTestBase
    {
        private readonly Faction player;
        private readonly Faction rival;

        public WealthWatcherTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            player = NewFaction("Ours", "PlayerCivilization");
            rival = NewFaction("Theirs", "TribalCivilization");
            player.SetRelationDirect(rival, FactionRelationKind.Hostile, -100);
        }

        private static Faction NewFaction(string name, string defName)
        {
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(faction);
            return faction;
        }

        private static CoreMap NewMap(int size = 40) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static float Value(Thing thing) => thing.GetStatValue(StatDefOf.MarketValue);

        private static Thing Spawn(CoreMap map, string defName, int x, int z, int stack = 1)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            thing.stackCount = stack;
            GenSpawn.Spawn(thing, new IntVec3(x, 0, z), map);
            return thing;
        }

        private static Thing BuildWall(CoreMap map, int x, int z, Faction? owner)
        {
            var wall = (SimWorld.Building.Building)ThingMaker.MakeThing(Def("Wall"));
            wall.faction = owner;
            GenSpawn.Spawn(wall, new IntVec3(x, 0, z), map);
            return wall;
        }

        private Pawn Settle(CoreMap map, int x, int z, Faction? faction, ThingDef? race = null)
        {
            Pawn pawn = race == null ? NewHuman() : new Pawn(race, "Beast");
            if (race == null) foreach (var skill in pawn.skills.skills) skill.Level = 6;
            pawn.faction = faction;
            GenSpawn.Spawn(pawn, new IntVec3(x, 0, z), map);
            return pawn;
        }

        // ---- the formula ----

        [Fact]
        public void Wealth_is_items_plus_half_the_buildings_plus_the_pawns()
        {
            CoreMap map = NewMap();
            Thing logs = Spawn(map, "WoodLog", 3, 3, stack: 40);
            Thing wall = BuildWall(map, 5, 5, player);
            Pawn colonist = Settle(map, 8, 8, player);

            map.wealthWatcher.ForceRecount();

            Assert.Equal(Value(logs) * 40f, map.wealthWatcher.WealthItems, 3);
            Assert.Equal(Value(wall), map.wealthWatcher.WealthBuildings, 3);
            Assert.Equal(Value(colonist), map.wealthWatcher.WealthPawns, 3);

            Assert.Equal(
                map.wealthWatcher.WealthItems + 0.5f * map.wealthWatcher.WealthBuildings + map.wealthWatcher.WealthPawns,
                map.wealthWatcher.PlayerWealthForStoryteller,
                3);
            Assert.Equal(
                map.wealthWatcher.WealthItems + map.wealthWatcher.WealthBuildings + map.wealthWatcher.WealthPawns,
                map.wealthWatcher.WealthTotal,
                3);
        }

        [Fact]
        public void Building_a_wall_raises_wealth_by_half_its_market_value()
        {
            CoreMap map = NewMap();
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.PlayerWealthForStoryteller;
            float buildingsBefore = map.wealthWatcher.WealthBuildings;

            Thing wall = BuildWall(map, 5, 5, player);
            map.wealthWatcher.ForceRecount();

            Assert.True(Value(wall) > 0f, "fixture: a wall must be worth something for this to say anything");
            Assert.Equal(Value(wall), map.wealthWatcher.WealthBuildings - buildingsBefore, 3);
            Assert.Equal(0.5f * Value(wall), map.wealthWatcher.PlayerWealthForStoryteller - before, 3);
        }

        [Fact]
        public void A_new_colonist_raises_wealth_by_their_market_value()
        {
            CoreMap map = NewMap();
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.PlayerWealthForStoryteller;

            Pawn colonist = Settle(map, 8, 8, player);
            map.wealthWatcher.ForceRecount();

            Assert.True(Value(colonist) > 0f);
            Assert.Equal(Value(colonist), map.wealthWatcher.PlayerWealthForStoryteller - before, 3);
            Assert.Equal(Value(colonist), map.wealthWatcher.WealthPawns, 3);
        }

        // ---- what does not count ----

        [Fact]
        public void A_hauled_item_changes_nothing()
        {
            CoreMap map = NewMap();
            Pawn hauler = Settle(map, 8, 8, player);
            Thing stack = Spawn(map, "WoodLog", 3, 3, stack: 30);
            map.wealthWatcher.ForceRecount();
            float resting = map.wealthWatcher.PlayerWealthForStoryteller;
            float restingItems = map.wealthWatcher.WealthItems;

            // Picked up: off the map's ground and into the hauler's hands.
            Assert.Equal(30, hauler.carryTracker.TryStartCarry(stack, 30));
            Assert.False(stack.Spawned);
            map.wealthWatcher.ForceRecount();
            Assert.Equal(resting, map.wealthWatcher.PlayerWealthForStoryteller, 3);
            Assert.Equal(restingItems, map.wealthWatcher.WealthItems, 3);

            // Set down somewhere else.
            Assert.True(hauler.carryTracker.TryDropCarriedThing(new IntVec3(12, 0, 12), out Thing? dropped));
            Assert.NotNull(dropped);
            map.wealthWatcher.ForceRecount();
            Assert.Equal(resting, map.wealthWatcher.PlayerWealthForStoryteller, 3);
        }

        [Fact]
        public void A_tame_animal_counts_and_a_wild_one_does_not()
        {
            CoreMap map = NewMap();
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.WealthPawns;

            Pawn wild = Settle(map, 4, 4, faction: null, race: Husky);
            map.wealthWatcher.ForceRecount();
            Assert.Equal(before, map.wealthWatcher.WealthPawns, 3);

            Pawn tame = Settle(map, 6, 6, player, race: Husky);
            map.wealthWatcher.ForceRecount();
            Assert.True(Value(tame) > 0f);
            Assert.Equal(Value(tame), map.wealthWatcher.WealthPawns - before, 3);

            // Tame it: the same animal, now ours.
            wild.faction = player;
            map.wealthWatcher.ForceRecount();
            Assert.Equal(Value(tame) + Value(wild), map.wealthWatcher.WealthPawns - before, 3);
        }

        [Fact]
        public void Another_factions_building_does_not_count_and_neither_does_a_ruin()
        {
            CoreMap map = NewMap();
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.PlayerWealthForStoryteller;

            BuildWall(map, 5, 5, rival);
            BuildWall(map, 6, 5, owner: null);
            map.wealthWatcher.ForceRecount();
            Assert.Equal(before, map.wealthWatcher.PlayerWealthForStoryteller, 3);
            Assert.Equal(0f, map.wealthWatcher.WealthBuildings, 3);

            Thing ours = BuildWall(map, 7, 5, player);
            map.wealthWatcher.ForceRecount();
            Assert.Equal(Value(ours), map.wealthWatcher.WealthBuildings, 3);
        }

        [Fact]
        public void A_raider_is_not_wealth_and_neither_is_what_the_raider_carries()
        {
            CoreMap map = NewMap();
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.PlayerWealthForStoryteller;

            Pawn raider = Settle(map, 4, 4, rival);
            raider.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Gun_AssaultRifle")));
            map.wealthWatcher.ForceRecount();

            Assert.Equal(before, map.wealthWatcher.PlayerWealthForStoryteller, 3);
        }

        [Fact]
        public void Nothing_belongs_to_a_player_who_does_not_exist_but_the_goods_on_the_ground_still_count()
        {
            Find.FactionManager = new FactionManager();
            CoreMap map = NewMap();
            Thing logs = Spawn(map, "WoodLog", 3, 3, stack: 10);
            BuildWall(map, 5, 5, owner: null);
            Settle(map, 8, 8, faction: null);

            map.wealthWatcher.ForceRecount();

            Assert.Equal(Value(logs) * 10f, map.wealthWatcher.WealthItems, 3);
            Assert.Equal(0f, map.wealthWatcher.WealthBuildings, 3);
            Assert.Equal(0f, map.wealthWatcher.WealthPawns, 3);
        }

        // ---- what a pawn holds ----

        [Fact]
        public void What_a_colonist_wields_and_wears_counts_as_items()
        {
            CoreMap map = NewMap();
            Pawn colonist = Settle(map, 8, 8, player);
            map.wealthWatcher.ForceRecount();
            float before = map.wealthWatcher.WealthItems;

            var rifle = (ThingWithComps)ThingMaker.MakeThing(Def("Gun_AssaultRifle"));
            var shirt = (ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"));
            colonist.equipment.AddEquipment(rifle);
            colonist.apparel.Wear(shirt);
            map.wealthWatcher.ForceRecount();

            Assert.True(Value(rifle) > 0f);
            Assert.Equal(Value(rifle) + Value(shirt), map.wealthWatcher.WealthItems - before, 3);
            // And it is items, not pawns: wearing a shirt does not make the wearer worth more.
            Assert.Equal(Value(colonist), map.wealthWatcher.WealthPawns, 3);
        }

        [Fact]
        public void A_colonists_body_keeps_its_gear_counted_and_a_raiders_does_not()
        {
            CoreMap map = NewMap();
            Pawn colonist = Settle(map, 8, 8, player);
            Pawn raider = Settle(map, 10, 10, rival);
            var colonistKit = (ThingWithComps)ThingMaker.MakeThing(Def("Gun_AssaultRifle"));
            var raiderKit = (ThingWithComps)ThingMaker.MakeThing(Def("Gun_AssaultRifle"));
            colonist.equipment.AddEquipment(colonistKit);
            raider.equipment.AddEquipment(raiderKit);
            map.wealthWatcher.ForceRecount();
            float alive = map.wealthWatcher.WealthItems;
            Assert.Equal(Value(colonistKit), alive, 3);

            colonist.health.Kill(null, null);
            raider.health.Kill(null, null);
            Assert.NotNull(colonist.corpse);
            Assert.NotNull(raider.corpse);
            map.wealthWatcher.ForceRecount();

            // The dead are not pawns for the wealth term, and the bodies are worth nothing, but the colonist's
            // rifle is still the settlement's and the raider's is not.
            Assert.Equal(0f, map.wealthWatcher.WealthPawns, 3);
            Assert.Equal(Value(colonistKit), map.wealthWatcher.WealthItems, 3);
        }

        // ---- floors ----

        [Fact]
        public void Floors_count_with_the_buildings_at_half_weight()
        {
            CoreMap map = NewMap();
            var flagstone = new TerrainDef { defName = "TestFlagstone", marketValue = 2f, layerable = false };
            for (int x = 0; x < 10; x++) map.terrainGrid.SetTerrain(new IntVec3(x, 0, 0), flagstone);

            map.wealthWatcher.ForceRecount();

            Assert.Equal(20f, map.wealthWatcher.WealthFloorsOnly, 3);
            Assert.Equal(20f, map.wealthWatcher.WealthBuildings, 3);
            Assert.Equal(10f, map.wealthWatcher.PlayerWealthForStoryteller, 3);
        }

        [Fact]
        public void The_ground_nobody_laid_is_worth_nothing()
        {
            foreach (TerrainDef terrain in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                Assert.Equal(0f, terrain.marketValue);
            }
        }

        // ---- the recount cadence ----

        [Fact]
        public void A_reading_can_lag_the_map_by_the_recount_interval_and_not_longer()
        {
            CoreMap map = NewMap();
            Find.TickManager.DebugSetTicksGame(100_000);
            float first = map.wealthWatcher.PlayerWealthForStoryteller;

            Thing wall = BuildWall(map, 5, 5, player);

            // Inside the interval: still the old figure, as RimWorld's is.
            Find.TickManager.DebugSetTicksGame(100_000 + WealthWatcher.MinCountInterval);
            Assert.Equal(first, map.wealthWatcher.PlayerWealthForStoryteller, 3);

            // Past it, with no one asking for a recount: the next read counts the wall.
            Find.TickManager.DebugSetTicksGame(100_000 + WealthWatcher.MinCountInterval + 1);
            Assert.Equal(first + 0.5f * Value(wall), map.wealthWatcher.PlayerWealthForStoryteller, 3);
        }

        [Fact]
        public void The_interval_is_RimWorlds()
        {
            Assert.Equal(5000, WealthWatcher.MinCountInterval);
            Assert.Equal(0.5f, WealthWatcher.BuildingsFactorForStoryteller);
        }

        // ---- save and load ----

        [Fact]
        public void A_map_that_loads_is_counted_afresh_and_reads_the_same()
        {
            // The watcher keeps nothing worth saving: this pins that a round trip loses nothing a recount
            // needs. The player's faction is a reference into the world, which a map saved alone cannot
            // resolve, so what is checked is the part a map owns: the goods on it, and the recount itself. A
            // building's owner surviving a load is checked in SettlementWealthTests, inside a world.
            CoreMap map = NewMap();
            Spawn(map, "WoodLog", 3, 3, stack: 40);
            Spawn(map, "Apparel_Shirt", 4, 4);
            map.wealthWatcher.ForceRecount();
            float items = map.wealthWatcher.WealthItems;
            Assert.True(items > 0f);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(items, loaded.wealthWatcher.WealthItems, 3);
        }
    }
}
