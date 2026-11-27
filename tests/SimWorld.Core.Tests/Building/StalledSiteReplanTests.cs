using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The settlement re-plans a stuff-built site whose stuff is gone (<see
    /// cref="SettlementConstructionInitiative.ReplanStalledSites"/>): a door frame that got 2 of its 4 steel
    /// before the steel ran out waited fifteen days beside 47 logs on the founding seed of
    /// <c>SettlementCompoundTests</c>. RimWorld leaves it for the player; the settlement is the player here.
    /// </summary>
    [Collection("GlobalDefs")]
    public class StalledSiteReplanTests : ContentTestBase
    {
        public StalledSiteReplanTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static readonly IntVec3 DoorCell = new IntVec3(10, 0, 10);

        private static void SpawnStack(CoreMap map, ThingDef def, int count, IntVec3 cell)
        {
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = count;
            GenSpawn.Spawn(stack, cell, map);
        }

        private static int OnMap(CoreMap map, ThingDef def) =>
            map.listerThings.ThingsOfDef(def).Where(t => t.Spawned).Sum(t => t.stackCount);

        private static Frame SteelDoorFrameWithTwo(CoreMap map)
        {
            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Door"), Def("Steel"));
            GenSpawn.Spawn(frame, DoorCell, map, Rot4.East);
            frame.AddMaterial(Def("Steel"), 2);
            return frame;
        }

        [Fact]
        public void A_frame_whose_stuff_is_gone_is_cancelled_refunded_and_planned_again_in_what_is_on_hand()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            SpawnStack(map, Def("WoodLog"), 40, new IntVec3(3, 0, 3));
            Frame frame = SteelDoorFrameWithTwo(map);
            uint shared = Rand.Current.Iterations;

            Assert.Equal(1, SettlementConstructionInitiative.ReplanStalledSites(map));

            Assert.False(frame.Spawned);
            Blueprint again = map.thingGrid.ThingsListAt(DoorCell).OfType<Blueprint>().Single();
            Assert.Same(Def("Door"), again.EntityToBuild);
            Assert.Same(Def("WoodLog"), again.stuffToUse);
            Assert.Equal(Rot4.East, again.Rotation);
            Assert.Equal(2, OnMap(map, Def("Steel"))); // everything delivered comes back, nothing lost
            Assert.Equal(shared, Rand.Current.Iterations);

            // Planned once: the wood plan is not re-planned again on the next pass.
            Assert.Equal(0, SettlementConstructionInitiative.ReplanStalledSites(map));
        }

        [Fact]
        public void A_frame_whose_stuff_is_on_the_map_or_in_hand_is_left_to_be_built()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            SpawnStack(map, Def("WoodLog"), 40, new IntVec3(3, 0, 3));
            SpawnStack(map, Def("Steel"), 2, new IntVec3(4, 0, 4));
            Frame frame = SteelDoorFrameWithTwo(map);

            Assert.Equal(0, SettlementConstructionInitiative.ReplanStalledSites(map));
            Assert.True(frame.Spawned);
        }

        [Fact]
        public void A_blueprint_whose_stuff_is_gone_is_planned_again_and_a_wall_is_never_touched()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            SpawnStack(map, Def("WoodLog"), 40, new IntVec3(3, 0, 3));
            var bed = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_Bed"));
            bed.stuffToUse = Def("Steel");
            GenSpawn.Spawn(bed, DoorCell, map);
            var wall = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_Wall"));
            wall.stuffToUse = Def("BlocksGranite");
            GenSpawn.Spawn(wall, new IntVec3(15, 0, 15), map);

            Assert.Equal(1, SettlementConstructionInitiative.ReplanStalledSites(map));

            Assert.False(bed.Spawned);
            Assert.Same(Def("WoodLog"), map.thingGrid.ThingsListAt(DoorCell).OfType<Blueprint>().Single().stuffToUse);
            Assert.True(wall.Spawned);
        }

        /// <summary>exposure5 lane: the 600-day 40/201 arc's two new houses each waited from day 230 to day 600 on one
        /// limestone wall, a frame with four of its five blocks and a blueprint, with the town's last limestone
        /// gone into other walls; both of the planner's places for houses going up were held, and no house was
        /// renewed for 360 days.</summary>
        [Fact]
        public void A_stone_wall_whose_blocks_are_gone_is_planned_again_in_the_wall_the_town_can_raise()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            SpawnStack(map, Def("WoodLog"), 40, new IntVec3(3, 0, 3));
            var frameCell = new IntVec3(12, 0, 12);
            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_WallLimestone"));
            GenSpawn.Spawn(frame, frameCell, map);
            frame.AddMaterial(Def("BlocksLimestone"), 4);
            var blueprintCell = new IntVec3(15, 0, 15);
            var blueprint = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_WallLimestone"));
            GenSpawn.Spawn(blueprint, blueprintCell, map);
            uint shared = Rand.Current.Iterations;

            Assert.Equal(2, SettlementConstructionInitiative.ReplanStalledSites(map));

            Assert.False(frame.Spawned);
            Assert.False(blueprint.Spawned);
            Assert.Same(StoneWallMaterials.PreferredWallDef(map), map.thingGrid.ThingsListAt(frameCell).OfType<Blueprint>().Single().EntityToBuild);
            Assert.Same(StoneWallMaterials.PreferredWallDef(map), map.thingGrid.ThingsListAt(blueprintCell).OfType<Blueprint>().Single().EntityToBuild);
            Assert.NotSame(Def("WallLimestone"), StoneWallMaterials.PreferredWallDef(map));
            Assert.Equal(4, OnMap(map, Def("BlocksLimestone"))); // the frame gives back what it was given
            Assert.Equal(shared, Rand.Current.Iterations);
            Assert.Equal(0, SettlementConstructionInitiative.ReplanStalledSites(map));
        }

        [Fact]
        public void A_stone_wall_whose_blocks_are_on_the_map_is_left_to_be_built()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            SpawnStack(map, Def("BlocksLimestone"), 5, new IntVec3(3, 0, 3));
            var blueprint = (Blueprint)ThingMaker.MakeThing(Def("Blueprint_WallLimestone"));
            GenSpawn.Spawn(blueprint, new IntVec3(15, 0, 15), map);

            Assert.Equal(0, SettlementConstructionInitiative.ReplanStalledSites(map));
            Assert.True(blueprint.Spawned);
        }
    }
}
