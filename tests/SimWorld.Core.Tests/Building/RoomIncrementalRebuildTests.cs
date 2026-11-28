using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
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
    /// <see cref="RoomTracker"/> re-floods only the rooms a change touches. These tests hold it to the whole-map
    /// flood it replaced: two maps get the same random walls, doors, pit walls, beds and roofs, coming and going
    /// in bursts; one is rebuilt incrementally, the other is made to re-flood the whole map every time, and after
    /// every burst the two must agree exactly on the rooms (cells in order, walls, map edge, open sky), the roof
    /// and wall data the temperature model reads, the doors, and the build-roof area the room offers mark. Only
    /// temperatures may differ, by rounding: a room nothing touched keeps its air instead of being recomputed as
    /// its own mean.
    /// </summary>
    public class RoomIncrementalRebuildTests : ContentTestBase
    {
        private const int SizeX = 30;
        private const int SizeZ = 24;

        public RoomIncrementalRebuildTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>Applies one random change to both maps alike.</summary>
        private static void RandomChange(RandomStream rand, CoreMap[] maps, Faction builders)
        {
            var cell = new IntVec3(rand.Range(0, SizeX), 0, rand.Range(0, SizeZ));
            int kind = rand.Range(0, 100);
            foreach (CoreMap map in maps)
            {
                Thing? edifice = map.edificeGrid[cell];
                if (kind < 45)
                {
                    if (edifice != null) continue;
                    string def = kind < 30 ? "Wall" : kind < 36 ? "Door" : kind < 41 ? "WallEarth" : "Bed";
                    if (def == "Bed" && cell.z + 1 >= SizeZ) continue;
                    if (def == "Bed" && map.edificeGrid[cell + new IntVec3(0, 0, 1)] != null) continue;
                    Thing thing = ThingMaker.MakeThing(Def(def));
                    // Most of the walls are a settlement's, so the room offers have rooms to roof.
                    if (kind % 3 != 0) thing.SetFaction(builders);
                    GenSpawn.Spawn(thing, cell, map);
                }
                else if (kind < 70)
                {
                    edifice?.Destroy();
                }
                else if (kind < 92)
                {
                    RoofDef? roof = kind < 80 ? RoofDefOf.RoofConstructed : kind < 84 ? RoofDefOf.RoofRockThick : null;
                    // A patch of roof, as a builder's work or a collapse would leave it.
                    for (int dz = 0; dz < 3; dz++)
                    {
                        for (int dx = 0; dx < 3; dx++)
                        {
                            var c = new IntVec3(cell.x + dx, 0, cell.z + dz);
                            if (GenGrid.InBounds(c, map)) map.roofGrid.SetRoof(c, roof);
                        }
                    }
                }
                else
                {
                    // The weather changes, so the rooms' air drifts apart from outdoors and from each other.
                    map.outdoorTemperature = -20f + (kind - 92) * 6f;
                }
            }
        }

        private static List<IntVec3> Positions(IEnumerable<Thing> things) => things.Select(t => t.Position).ToList();

        internal static void AssertSameRooms(CoreMap incremental, CoreMap full, int step)
        {
            IReadOnlyList<Room> a = incremental.roomTracker.Rooms;
            IReadOnlyList<Room> b = full.roomTracker.Rooms;
            Assert.True(a.Count == b.Count, $"step {step}: {a.Count} rooms incrementally, {b.Count} from a full flood");
            Assert.Equal(b.Count, incremental.roomTracker.Groups.Count);
            for (int r = 0; r < b.Count; r++)
            {
                Room x = a[r], y = b[r];
                Assert.Equal(y.Cells, x.Cells);
                Assert.Equal(Positions(y.boundaryEdifices), Positions(x.boundaryEdifices));
                Assert.Equal(y.touchesMapEdge, x.touchesMapEdge);
                Assert.Equal(y.anyCellUnroofed, x.anyCellUnroofed);
                Assert.Same(x.Group, incremental.roomTracker.Groups[r]);
                Assert.Same(x, x.Group.Rooms[0]);
                foreach (IntVec3 c in x.Cells) Assert.Same(x, incremental.roomTracker.RoomAt(c));

                Assert.Equal(y.Group.UsesOutdoorTemperature, x.Group.UsesOutdoorTemperature);
                if (y.Group.UsesOutdoorTemperature) continue;
                Assert.Equal(y.Group.thinRoofShare, x.Group.thinRoofShare);
                Assert.Equal(y.Group.thickRoofShare, x.Group.thickRoofShare);
                Assert.Equal(y.Group.earthBankedWallCells, x.Group.earthBankedWallCells);
                Assert.Equal(y.Group.wallBeyondCells, x.Group.wallBeyondCells);
                Assert.True(Math.Abs(y.Group.Temperature - x.Group.Temperature) < 0.01f,
                    $"step {step}: room {r} at {x.Group.Temperature} incrementally, {y.Group.Temperature} from a full flood");
            }
            foreach (IntVec3 c in incremental.AllCells)
            {
                Assert.Equal(full.roomTracker.RoomAt(c) == null, incremental.roomTracker.RoomAt(c) == null);
                Assert.Equal(full.areaManager.BuildRoof[c], incremental.areaManager.BuildRoof[c]);
                Assert.Equal(full.roofGrid.RoofAt(c), incremental.roofGrid.RoofAt(c));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void An_incremental_rebuild_finds_exactly_the_rooms_a_full_flood_finds(int seed)
        {
            var builders = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var maps = new[]
            {
                new CoreMap(SizeX, SizeZ, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 10f },
                new CoreMap(SizeX, SizeZ, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 10f },
            };
            foreach (CoreMap map in maps) map.roomTracker.RoomTrackerTick();
            var rand = new RandomStream(seed);
            int roomsKept = 0;

            for (int step = 0; step < 400; step++)
            {
                var before = new HashSet<Room>(maps[0].roomTracker.Rooms);
                int changes = rand.Range(1, 5);
                for (int i = 0; i < changes; i++) RandomChange(rand, maps, builders);

                maps[1].roomTracker.Notify_Dirty();
                // A handful of ticks, so the air equalises between bursts on both maps.
                for (int t = 0; t < 5; t++)
                {
                    foreach (CoreMap map in maps) map.roomTracker.RoomTrackerTick();
                }
                if (maps[0].roomTracker.Rooms.Any(before.Contains)) roomsKept++;
                AssertSameRooms(maps[0], maps[1], step);
            }

            // The comparison is only worth something if the maps grew real rooms.
            Assert.Contains(maps[0].roomTracker.Rooms, r => !r.TouchesOutside);
            Assert.Contains(maps[0].AllCells, c => maps[0].areaManager.BuildRoof[c]);
            // And if the incremental map really kept rooms across changes rather than re-flooding them all.
            Assert.True(roomsKept > 200, $"only {roomsKept} of 400 bursts kept any room");
        }

        [Fact]
        public void A_room_nothing_touched_keeps_its_own_room_and_its_air()
        {
            var map = new CoreMap(SizeX, SizeZ, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 0f };
            var house = new CellRect(2, 2, 6, 6);
            foreach (IntVec3 c in house.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), c, map);
            foreach (IntVec3 c in house.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            map.roomTracker.RoomTrackerTick();
            Room room = map.roomTracker.RoomAt(new IntVec3(4, 0, 4))!;
            room.Group.temperature = 18.123f;

            // A wall goes up across the map, well away from the house.
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall")), new IntVec3(20, 0, 15), map);
            map.outdoorTemperature = room.Group.temperature;
            map.roomTracker.RoomTrackerTick();

            Assert.Same(room, map.roomTracker.RoomAt(new IntVec3(4, 0, 4)));
            Assert.Equal(18.123f, room.Group.Temperature);
        }
    }
}
