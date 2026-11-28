using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A bed or a table coming or going does not rebuild any room (<see cref="RoomTracker.CanAlterRooms"/>): it
    /// cannot bound one, so the rooms are what a whole-map flood would find without asking. These tests hold the
    /// skip to that flood, and hold the two kinds of edifice that do matter — Fillage-Full walls and doors, and
    /// impassable things the roof offers read — to still rebuilding.
    /// </summary>
    public class RoomSkipRebuildTests : ContentTestBase
    {
        private const int SizeX = 30;
        private const int SizeZ = 24;

        private static readonly string[] Furniture = { "Bed", "DiningTable", "Furnishing_Agrarian" };

        public RoomSkipRebuildTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static bool FootprintFree(CoreMap map, ThingDef def, IntVec3 cell)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, Rot4.North, def.size).Cells)
            {
                if (!GenGrid.InBounds(c, map) || map.edificeGrid[c] != null) return false;
            }
            return true;
        }

        /// <summary>Two identical towns of rooms with doors and roofs.</summary>
        private static CoreMap[] Towns(Faction builders)
        {
            var maps = new CoreMap[2];
            for (int m = 0; m < 2; m++)
            {
                var map = new CoreMap(SizeX, SizeZ, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 4f };
                for (int hx = 0; hx < 3; hx++)
                {
                    var house = new CellRect(1 + hx * 9, 2, 8, 8);
                    foreach (IntVec3 c in house.EdgeCells)
                    {
                        if (map.edificeGrid[c] != null) continue;
                        Thing wall = ThingMaker.MakeThing(Def(c == new IntVec3(house.minX + 3, 0, house.minZ) ? "Door" : "Wall"));
                        wall.SetFaction(builders);
                        GenSpawn.Spawn(wall, c, map);
                    }
                    // The third house is left half open to the sky.
                    foreach (IntVec3 c in house.Cells)
                    {
                        if (hx < 2 || c.z < 6) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    }
                }
                map.roomTracker.RoomTrackerTick();
                maps[m] = map;
            }
            return maps;
        }

        [Fact]
        public void Furniture_coming_and_going_rebuilds_no_room_and_leaves_the_rooms_a_full_flood_finds()
        {
            var builders = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            CoreMap[] maps = Towns(builders);
            var rand = new RandomStream(7);
            var rooms = new List<Room>(maps[0].roomTracker.Rooms);
            int placed = 0, removed = 0;

            for (int step = 0; step < 300; step++)
            {
                var cell = new IntVec3(rand.Range(0, SizeX), 0, rand.Range(0, SizeZ));
                ThingDef def = Def(Furniture[rand.Range(0, Furniture.Length)]);
                bool remove = rand.Range(0, 3) == 0;
                foreach (CoreMap map in maps)
                {
                    Thing? there = map.edificeGrid[cell];
                    if (remove)
                    {
                        if (there == null || RoomTracker.CanAlterRooms(there.def)) continue;
                        there.Destroy();
                        if (map == maps[0]) removed++;
                    }
                    else if (FootprintFree(map, def, cell))
                    {
                        Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                        thing.SetFaction(builders);
                        GenSpawn.Spawn(thing, cell, map);
                        if (map == maps[0]) placed++;
                    }
                }
                maps[1].roomTracker.Notify_Dirty();
                foreach (CoreMap map in maps) map.roomTracker.RoomTrackerTick();

                Assert.Equal(rooms, maps[0].roomTracker.Rooms);
                RoomIncrementalRebuildTests.AssertSameRooms(maps[0], maps[1], step);
            }

            Assert.True(placed > 50 && removed > 10, $"placed {placed}, removed {removed}");
        }

        [Fact]
        public void A_bed_coming_or_going_offers_the_roof_again_though_no_room_is_rebuilt()
        {
            var builders = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            CoreMap map = Towns(builders)[0];
            // The third house is walled but half open: its first offer marked the open half for roofing.
            var open = new IntVec3(23, 0, 7);
            Assert.Null(map.roofGrid.RoofAt(open));
            Assert.True(map.areaManager.BuildRoof[open]);
            var rooms = new List<Room>(map.roomTracker.Rooms);

            // Whatever undid that mark, a bed moving in offers the room again, as a rebuild of the whole map did.
            foreach (IntVec3 c in map.AllCells) map.areaManager.BuildRoof[c] = false;
            ThingDef bed = Def("Bed");
            Thing thing = ThingMaker.MakeThing(bed, bed.MadeFromStuff ? GenStuff.DefaultStuffFor(bed) : null);
            thing.SetFaction(builders);
            GenSpawn.Spawn(thing, new IntVec3(21, 0, 3), map);
            map.roomTracker.RoomTrackerTick();

            Assert.Equal(rooms, map.roomTracker.Rooms);
            Assert.True(map.areaManager.BuildRoof[open], "the half-open house was not offered its roof again");

            // And moving out.
            foreach (IntVec3 c in map.AllCells) map.areaManager.BuildRoof[c] = false;
            thing.Destroy();
            map.roomTracker.RoomTrackerTick();
            Assert.Equal(rooms, map.roomTracker.Rooms);
            Assert.True(map.areaManager.BuildRoof[open], "the half-open house was not offered its roof again when the bed left");
        }

        [Theory]
        [InlineData("Wall")]
        [InlineData("Door")]
        [InlineData("Sculpture")]
        public void A_wall_a_door_or_an_impassable_thing_still_rebuilds_the_rooms_round_it(string defName)
        {
            var builders = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            CoreMap[] maps = Towns(builders);
            var cell = new IntVec3(4, 0, 5);
            Room before = maps[0].roomTracker.RoomAt(cell)!;
            ThingDef def = Def(defName);

            foreach (CoreMap map in maps)
            {
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.SetFaction(builders);
                GenSpawn.Spawn(thing, cell, map);
            }
            maps[1].roomTracker.Notify_Dirty();
            foreach (CoreMap map in maps) map.roomTracker.RoomTrackerTick();

            Assert.True(RoomTracker.CanAlterRooms(def));
            Assert.NotSame(before, maps[0].roomTracker.RoomAt(cell + new IntVec3(1, 0, 0)));
            RoomIncrementalRebuildTests.AssertSameRooms(maps[0], maps[1], 0);

            // And taken away again.
            foreach (CoreMap map in maps) map.edificeGrid[cell]!.Destroy();
            maps[1].roomTracker.Notify_Dirty();
            foreach (CoreMap map in maps) map.roomTracker.RoomTrackerTick();
            RoomIncrementalRebuildTests.AssertSameRooms(maps[0], maps[1], 1);
        }
    }
}
