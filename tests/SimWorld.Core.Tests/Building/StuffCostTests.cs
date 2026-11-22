using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A building made from stuff costs <c>costStuffCount</c> of its chosen stuff on top of its
    /// <c>costList</c> (RimWorld: <c>BuildableDef.CostListAdjusted</c>), and that cost reaches every
    /// construction reader: the blueprint, the frame, the delivery work giver and the banking hold. Before it,
    /// <c>costStuffCount</c> was read by nothing, so a def priced in stuff alone was built out of nothing.
    /// </summary>
    public class StuffCostTests : ContentTestBase
    {
        public StuffCostTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Wall => Def("Wall");
        private static ThingDef Wood => Def("WoodLog");
        private static ThingDef Granite => Def("BlocksGranite");

        private static readonly IntVec3 Site = new IntVec3(6, 0, 6);

        private static CoreMap NewMap() => new CoreMap(12, 12, SimWorld.Map.TerrainDefOf.Soil);

        private static Blueprint PlaceWallBlueprint(CoreMap map, ThingDef? stuff)
        {
            var blueprint = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_Wall"));
            blueprint.stuffToUse = stuff;
            GenSpawn.Spawn(blueprint, Site, map);
            return blueprint;
        }

        private static void SpawnStack(CoreMap map, ThingDef def, int count, IntVec3 at)
        {
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, at, map);
        }

        // ---- CostListAdjusted ----

        [Fact]
        public void The_shipped_wall_costs_its_costStuffCount_of_whatever_it_is_built_from()
        {
            Assert.True(Wall.MadeFromStuff, "fixture: Wall is made from stuff");
            int perWall = Wall.costStuffCount;
            Assert.True(perWall > 0, "fixture: Wall has a stuff cost");

            Assert.Equal(new[] { (Granite, perWall) }, Wall.CostListAdjusted(Granite).Select(e => (e.thingDef, e.count)));
            Assert.Equal(new[] { (Wood, perWall) }, Wall.CostListAdjusted(Wood).Select(e => (e.thingDef, e.count)));
            // No stuff named means RimWorld's default, wood: a wall placed by a planner that picks no material.
            Assert.Equal(new[] { (Wood, perWall) }, Wall.CostListAdjusted(null).Select(e => (e.thingDef, e.count)));
        }

        private static ThingDef StuffBuilt(int costStuffCount, params (ThingDef def, int count)[] costList) => new ThingDef
        {
            defName = "Test_StuffBuilt",
            category = ThingCategory.Building,
            stuffCategories = new List<StuffCategoryDef> { DefDatabase<StuffCategoryDef>.GetNamed("Woody"), DefDatabase<StuffCategoryDef>.GetNamed("Metallic") },
            costStuffCount = costStuffCount,
            costList = costList.Select(c => new ThingDefCountClass(c.def, c.count)).ToList(),
        };

        [Fact]
        public void Stuff_the_costList_already_names_is_merged_into_that_entry()
        {
            ThingDef steel = Def("Steel");
            ThingDef def = StuffBuilt(30, (steel, 10), (Wood, 2));

            Assert.Equal(new[] { (steel, 10), (Wood, 32) }, def.CostListAdjusted(Wood).Select(e => (e.thingDef, e.count)));
            // A stuff the costList does not name goes first, as RimWorld inserts it.
            Assert.Equal(new[] { (steel, 40), (Wood, 2) }, def.CostListAdjusted(steel).Select(e => (e.thingDef, e.count)));
        }

        [Theory]
        [InlineData(30, 300)]
        [InlineData(0, 1)] // at least one unit of the stuff, whatever the count
        public void A_small_volume_stuff_takes_ten_units_for_each_unit_of_volume(int costStuffCount, int expected)
        {
            var dense = new ThingDef
            {
                defName = "Test_DenseMetal",
                category = ThingCategory.Item,
                smallVolume = true,
                stuffProps = new StuffProperties { categories = new List<StuffCategoryDef> { DefDatabase<StuffCategoryDef>.GetNamed("Metallic") } },
            };
            ThingDef def = StuffBuilt(costStuffCount);

            Assert.Equal(new[] { (dense, expected) }, def.CostListAdjusted(dense).Select(e => (e.thingDef, e.count)));
        }

        [Fact]
        public void A_def_not_made_from_stuff_costs_its_costList_whatever_stuff_is_passed()
        {
            ThingDef hut = Def("Campfire");
            Assert.False(hut.MadeFromStuff);
            Assert.Equal(hut.costList!.Select(e => (e.thingDef, e.count)), hut.CostListAdjusted(Granite).Select(e => (e.thingDef, e.count)));
        }

        // ---- blueprints, frames, delivery ----

        [Fact]
        public void A_wall_blueprint_with_no_stuff_named_is_wooden_and_not_free()
        {
            CoreMap map = NewMap();
            Pawn builder = NewHuman("Builder");
            GenSpawn.Spawn(builder, new IntVec3(0, 0, 0), map);
            Blueprint blueprint = PlaceWallBlueprint(map, null);

            Assert.Same(Wood, blueprint.stuffToUse);
            Assert.Equal(Wall.costStuffCount, blueprint.ThingCountNeeded(Wood));

            // Nothing on the map to build it with: a free wall would hand out PlaceNoCostFrame here.
            var giver = new WorkGiver_ConstructDeliverResourcesToBlueprints();
            Assert.False(giver.HasJobOnThing(builder, blueprint));

            SpawnStack(map, Wood, 10, new IntVec3(1, 0, 1));
            Job? job = giver.JobOnThing(builder, blueprint);
            Assert.NotNull(job);
            Assert.Same(JobDefOf.HaulToBuildingSite, job!.def);
        }

        [Fact]
        public void A_granite_wall_wants_granite_blocks_and_wood_does_not_satisfy_it()
        {
            CoreMap map = NewMap();
            Pawn builder = NewHuman("Builder");
            GenSpawn.Spawn(builder, new IntVec3(0, 0, 0), map);
            SpawnStack(map, Wood, 50, new IntVec3(1, 0, 1));
            Blueprint blueprint = PlaceWallBlueprint(map, Granite);
            var giver = new WorkGiver_ConstructDeliverResourcesToBlueprints();

            Assert.False(giver.HasJobOnThing(builder, blueprint)); // wood is no use to it

            SpawnStack(map, Granite, 20, new IntVec3(2, 0, 1));
            Job? job = giver.JobOnThing(builder, blueprint);
            Assert.NotNull(job);
            Assert.Same(Granite, job!.targetA.Thing!.def);

            Frame frame = blueprint.ReplaceWithFrame();
            Assert.Same(Granite, frame.Stuff);
            Assert.Equal(Wall.costStuffCount, frame.MaterialStillNeeded(Granite));
            Assert.Equal(0, frame.MaterialStillNeeded(Wood));
            Assert.False(frame.MaterialsFullySatisfied());

            frame.AddMaterial(Granite, frame.MaterialStillNeeded(Granite));
            Assert.True(frame.MaterialsFullySatisfied());

            Thing built = frame.CompleteConstruction(builder);
            Assert.Same(Wall, built.def);
            Assert.Same(Granite, built.Stuff);
        }

        [Fact]
        public void A_failed_frame_leaves_a_blueprint_for_the_same_stuff()
        {
            CoreMap map = NewMap();
            Pawn builder = NewHuman("Builder");
            GenSpawn.Spawn(builder, new IntVec3(0, 0, 0), map);
            Frame frame = PlaceWallBlueprint(map, Granite).ReplaceWithFrame();
            frame.AddMaterial(Granite, 4);

            frame.FailConstruction(builder);

            Blueprint again = map.thingGrid.ThingsListAt(Site).OfType<Blueprint>().Single();
            Assert.Same(Granite, again.stuffToUse);
            Assert.Equal(Wall.costStuffCount, again.ThingCountNeeded(Granite));
        }

        [Fact]
        public void Stone_blocks_a_planned_wall_needs_are_held_back_from_the_ledger()
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Stonehome", 0);
            CoreMap map = NewMap();
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(2, 0, 2));
            zone.filter.SetAllowAll(null);
            SpawnStack(map, Granite, 20, new IntVec3(2, 0, 2));
            PlaceWallBlueprint(map, Granite);

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(0, settlement.StoreCountOf(Granite));
        }

        [Fact]
        public void Blueprint_stuff_and_frame_stuff_round_trip_through_scribe()
        {
            CoreMap map = NewMap();
            PlaceWallBlueprint(map, Granite);
            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Wall"), Def("Steel"));
            GenSpawn.Spawn(frame, new IntVec3(2, 0, 2), map);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Blueprint blueprint = loaded.thingGrid.ThingsListAt(Site).OfType<Blueprint>().Single();
            Assert.Equal("BlocksGranite", blueprint.stuffToUse?.defName);
            var loadedFrame = (Frame)loaded.edificeGrid[new IntVec3(2, 0, 2)]!;
            Assert.Equal("Steel", loadedFrame.Stuff?.defName);
            Assert.Equal(Wall.costStuffCount, loadedFrame.MaterialStillNeeded(Def("Steel")));
        }
    }
}
