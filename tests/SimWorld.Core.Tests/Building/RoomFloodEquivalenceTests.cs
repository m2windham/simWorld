using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The room tracker's flood was rewritten for speed (tick-cost lane: one index queue instead of a queue per
    /// room and an iterator per cell, no wall bookkeeping for the open ground). Its output is read in order — the
    /// wall sums walk <see cref="RoomGroup.wallBeyondCells"/>, the roof offers walk <see cref="Room.Cells"/> — so
    /// "the same rooms" is not enough: these tests hold the rewrite to the flood it replaced, kept here verbatim
    /// as the reference, room by room, cell by cell and wall by wall.
    /// </summary>
    public class RoomFloodEquivalenceTests : ContentTestBase
    {
        public RoomFloodEquivalenceTests(CoreContentFixture content) : base(content)
        {
        }

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)), cell, map);

        private static void Ring(CoreMap map, CellRect rect, string wall, IntVec3? door = null)
        {
            foreach (IntVec3 c in rect.EdgeCells)
            {
                if (!GenGrid.InBounds(c, map) || map.edificeGrid[c] != null) continue;
                Spawn(map, c, c == door ? "Door" : wall);
            }
        }

        private static void Roof(CoreMap map, CellRect rect, RoofDef roof)
        {
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, roof);
        }

        /// <summary>Every shape the flood distinguishes: a roofed house with a door and a bed in it, a pit house
        /// of earth-banked walls under mixed roof, a room sharing a wall with the house, one closed against the map
        /// edge, and a scatter of lone walls in the open.</summary>
        private static CoreMap Town()
        {
            var map = new CoreMap(40, 30, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = 5f };
            var house = new CellRect(2, 2, 7, 7);
            Ring(map, house, "Wall", new IntVec3(8, 0, 5));
            Roof(map, house, RoofDefOf.RoofConstructed);
            Spawn(map, new IntVec3(4, 0, 4), "Bed");

            var shed = new CellRect(8, 2, 5, 7);
            Ring(map, shed, "Wall", new IntVec3(12, 0, 4));

            var pit = new CellRect(16, 2, 6, 6);
            Ring(map, pit, "WallEarth");
            Roof(map, new CellRect(16, 2, 6, 3), RoofDefOf.RoofRockThick);
            Roof(map, new CellRect(16, 5, 6, 3), RoofDefOf.RoofConstructed);

            var edge = new CellRect(-1, 20, 6, 6);
            Ring(map, edge, "Wall");
            Roof(map, new CellRect(0, 21, 4, 4), RoofDefOf.RoofConstructed);

            for (int z = 10; z < 30; z++)
            {
                for (int x = 20; x < 40; x++)
                {
                    if ((x * 7 + z * 3) % 11 == 0 && map.edificeGrid[new IntVec3(x, 0, z)] == null) Spawn(map, new IntVec3(x, 0, z), "Wall");
                }
            }
            return map;
        }

        private sealed class RefRoom
        {
            public readonly List<IntVec3> cells = new List<IntVec3>();
            public readonly HashSet<Thing> boundary = new HashSet<Thing>();
            public bool touchesMapEdge;
            public bool anyCellUnroofed;
        }

        private static bool IsFullEdifice(CoreMap map, IntVec3 cell)
        {
            Thing? edifice = map.edificeGrid[cell];
            return edifice != null && edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>The flood as it stood before the rewrite (RoomTracker.RegenerateAllRooms at 6345600).</summary>
        private static List<RefRoom> ReferenceFlood(CoreMap map)
        {
            var rooms = new List<RefRoom>();
            var visited = new HashSet<IntVec3>();
            foreach (IntVec3 seed in map.AllCells)
            {
                if (visited.Contains(seed)) continue;
                if (IsFullEdifice(map, seed)) continue;
                var room = new RefRoom();
                var queue = new Queue<IntVec3>();
                queue.Enqueue(seed);
                visited.Add(seed);
                while (queue.Count > 0)
                {
                    IntVec3 cur = queue.Dequeue();
                    room.cells.Add(cur);
                    if (!map.roofGrid.Roofed(cur)) room.anyCellUnroofed = true;
                    if (cur.x == 0 || cur.z == 0 || cur.x == map.Size.x - 1 || cur.z == map.Size.z - 1) room.touchesMapEdge = true;
                    foreach (IntVec3 n in GenAdj.CellsAdjacentCardinal(cur))
                    {
                        if (!GenGrid.InBounds(n, map))
                        {
                            room.touchesMapEdge = true;
                            continue;
                        }
                        if (IsFullEdifice(map, n))
                        {
                            room.boundary.Add(map.edificeGrid[n]!);
                            continue;
                        }
                        if (!visited.Add(n)) continue;
                        queue.Enqueue(n);
                    }
                }
                rooms.Add(room);
            }
            return rooms;
        }

        /// <summary>RoomTracker.RegenerateEqualizationData at 6345600: (thin share, thick share, earth-banked walls,
        /// far side of each wall in order).</summary>
        private static (float thin, float thick, int earth, List<IntVec3> beyond) ReferenceEqualization(CoreMap map, RefRoom room)
        {
            int thick = 0, thin = 0, earth = 0;
            var walls = new HashSet<IntVec3>();
            var beyond = new List<IntVec3>();
            foreach (IntVec3 cell in room.cells)
            {
                RoofDef? roof = map.roofGrid.RoofAt(cell);
                if (roof != null)
                {
                    if (roof.isThickRoof || roof.isEarthen) thick++;
                    else thin++;
                }
                for (int d = 0; d < 4; d++)
                {
                    IntVec3 dir = GenAdj.CardinalDirections[d];
                    IntVec3 wall = cell + dir;
                    if (!GenGrid.InBounds(wall, map) || !IsFullEdifice(map, wall)) continue;
                    if (map.edificeGrid[wall] is Door) continue;
                    if (!walls.Add(wall)) continue;
                    if (map.edificeGrid[wall]!.def.earthBanked) earth++;
                    else beyond.Add(wall + dir);
                }
            }
            int n = System.Math.Max(1, room.cells.Count);
            return ((float)thin / n, (float)thick / n, earth, beyond);
        }

        private static void AssertSameAsReference(CoreMap map)
        {
            List<RefRoom> expected = ReferenceFlood(map);
            IReadOnlyList<Room> actual = map.roomTracker.Rooms;
            Assert.Equal(expected.Count, actual.Count);
            for (int r = 0; r < expected.Count; r++)
            {
                Room room = actual[r];
                Assert.Equal(expected[r].cells, room.Cells);
                Assert.Equal(expected[r].boundary.ToList(), room.boundaryEdifices.ToList());
                Assert.Equal(expected[r].touchesMapEdge, room.touchesMapEdge);
                Assert.Equal(expected[r].anyCellUnroofed, room.anyCellUnroofed);
                foreach (IntVec3 c in expected[r].cells) Assert.Same(room, map.roomTracker.RoomAt(c));

                if (room.Group.UsesOutdoorTemperature) continue;
                var eq = ReferenceEqualization(map, expected[r]);
                Assert.Equal(eq.thin, room.Group.thinRoofShare);
                Assert.Equal(eq.thick, room.Group.thickRoofShare);
                Assert.Equal(eq.earth, room.Group.earthBankedWallCells);
                Assert.Equal(eq.beyond, room.Group.wallBeyondCells);
            }
        }

        [Fact]
        public void The_flood_finds_the_rooms_the_old_flood_found_in_the_same_order()
        {
            CoreMap map = Town();
            map.roomTracker.RoomTrackerTick();

            // The town is what it says: indoor rooms, outdoor rooms, and one against the map edge.
            Assert.Contains(map.roomTracker.Rooms, r => !r.TouchesOutside);
            Assert.Contains(map.roomTracker.Rooms, r => r.touchesMapEdge && r.Cells.Count < 30);
            Assert.Contains(map.roomTracker.Rooms, r => r.TouchesOutside && !r.touchesMapEdge);
            AssertSameAsReference(map);
        }

        [Fact]
        public void A_rebuild_after_walls_come_and_go_still_matches_the_old_flood()
        {
            CoreMap map = Town();
            map.roomTracker.RoomTrackerTick();

            // Open the shed to the house, wall the house's door shut, and close a gap in the open: three rebuilds'
            // worth of change, one rebuild, over arrays the first rebuild left behind.
            map.edificeGrid[new IntVec3(8, 0, 4)]!.Destroy();
            map.edificeGrid[new IntVec3(8, 0, 5)]!.Destroy();
            Spawn(map, new IntVec3(8, 0, 5), "Wall");
            Spawn(map, new IntVec3(30, 0, 5), "Wall");
            map.roomTracker.RoomTrackerTick();

            AssertSameAsReference(map);
        }
    }
}
