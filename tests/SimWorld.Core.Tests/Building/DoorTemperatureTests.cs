using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A door keeps a room thermally separate, and heat crosses walls, roofs and doors at RimWorld's rates
    /// (<c>RoomGroupTempTracker</c> and <c>Building_Door</c>, read in the B18-era decompile). Before this, any
    /// room with a door to the outside used outdoor air outright, so a house was no shelter from a heat wave.
    /// The rates are asserted as behaviour here (separate, lags, shut slower than open), not as literals.
    /// </summary>
    public class DoorTemperatureTests : ContentTestBase
    {
        public DoorTemperatureTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(float outdoor)
        {
            var map = new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };
            return map;
        }

        private static void Spawn(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);

        /// <summary>A 5x5-inside roofed house whose east wall holds one door; returns the inside room.</summary>
        private static Room BuildHouse(CoreMap map, int originX, int originZ, out IntVec3 door)
        {
            var shell = new CellRect(originX, originZ, 7, 7);
            door = new IntVec3(originX + 6, 0, originZ + 3);
            foreach (IntVec3 c in shell.EdgeCells) Spawn(map, c, c == door ? "Door" : "Wall");
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            return map.roomTracker.RoomAt(new IntVec3(originX + 3, 0, originZ + 3))!;
        }

        [Fact]
        public void A_house_with_a_door_to_the_outside_keeps_its_own_air()
        {
            CoreMap map = NewMap(45f);
            Room inside = BuildHouse(map, 1, 1, out _);
            Assert.False(inside.Group.UsesOutdoorTemperature);
            inside.Group.temperature = 22f;
            map.MapTick();
            Assert.InRange(inside.Temperature, 22f, 23f);
        }

        [Fact]
        public void Two_rooms_behind_one_door_are_separate_thermal_units()
        {
            CoreMap map = NewMap(10f);
            var outer = new CellRect(1, 1, 9, 5);
            foreach (IntVec3 c in outer.EdgeCells) Spawn(map, c, "Wall");
            foreach (int z in new[] { 1, 2, 4, 5 }) Spawn(map, new IntVec3(5, 0, z), "Wall");
            Spawn(map, new IntVec3(5, 0, 3), "Door");
            foreach (IntVec3 c in outer.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            Room left = map.roomTracker.RoomAt(new IntVec3(3, 0, 3))!;
            Room right = map.roomTracker.RoomAt(new IntVec3(7, 0, 3))!;
            Assert.NotSame(left.Group, right.Group);
            left.Group.temperature = 30f;
            right.Group.temperature = 0f;
            map.MapTick();
            Assert.True(left.Temperature > 25f);
            Assert.True(right.Temperature < 5f);
        }

        [Fact]
        public void Over_an_hour_of_heat_a_house_warms_but_lags_well_behind_the_air_outside()
        {
            CoreMap map = NewMap(45f);
            Room inside = BuildHouse(map, 1, 1, out _);
            inside.Group.temperature = 25f;
            for (int i = 0; i < 2500; i++) map.MapTick();
            Assert.True(inside.Temperature > 26f, $"should have warmed, was {inside.Temperature}");
            Assert.True(inside.Temperature < 40f, $"should lag the 45 C outside, was {inside.Temperature}");
        }

        [Fact]
        public void A_door_held_open_by_someone_in_it_lets_heat_in_faster_than_a_shut_one()
        {
            float After(bool occupied)
            {
                CoreMap map = NewMap(45f);
                Room inside = BuildHouse(map, 1, 1, out IntVec3 door);
                if (occupied) GenSpawn.Spawn(NewHuman("Doorstop"), door, map);
                inside.Group.temperature = 25f;
                for (int i = 0; i < 600; i++) map.MapTick();
                return inside.Temperature;
            }

            float shut = After(false);
            float open = After(true);
            Assert.True(open > shut + 0.5f, $"open {open} vs shut {shut}");
        }

        [Fact]
        public void A_door_stays_open_a_while_after_its_doorway_clears()
        {
            var door = (Door)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Door"));
            Assert.False(door.IsOpenAt(1000));
            door.Notify_Occupied(1000);
            Assert.True(door.IsOpenAt(1000 + Door.CloseDelayTicks));
            Assert.True(door.IsOpenAt(1000 + Door.MinOpenTicks - 1));
            Assert.False(door.IsOpenAt(1000 + Door.MinOpenTicks));
        }
    }
}
