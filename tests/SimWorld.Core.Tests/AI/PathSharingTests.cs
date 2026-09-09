using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Region-graph path sharing (RimWorld has no such pass; this is a translation for full-agent civilization
    /// scale — <c>docs/status.json</c>'s <c>ai.pathing.sharing</c>). <see cref="PathFinder"/>'s own remarks and
    /// <see cref="RegionPathCorridorCache"/>'s own remarks carry the design argument; these tests pin the
    /// properties that argument depends on: a corridor never routes through a wall, a reachable destination
    /// stays reachable, a cache hit answers exactly what a fresh build would for the same query, the cache
    /// updates when the map it was built from changes under it, and — the one deliberate trade-off — a
    /// corridor can be longer than the true optimum, bounded here rather than hidden.
    /// </summary>
    public class PathSharingTests : ContentTestBase
    {
        public PathSharingTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing BuildWall(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), c, map);

        private static Thing BuildDoor(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Door")), c, map);

        /// <summary>Every step of a found path is onto a currently-walkable cell and is a single legal move
        /// from the previous cell — cardinal, or diagonal only when neither flanking cardinal cell is blocked
        /// (<see cref="PathFinder"/>'s own corner-cutting rule). A corridor-constrained search reuses that same
        /// per-step logic verbatim (see <see cref="PathFinder.RunSearch"/> in source — the corridor only ever
        /// removes candidate cells, it adds no new rule) so this check applies identically to a hierarchical
        /// path and an unconstrained one; it never distinguishes which one produced the path.</summary>
        private static void AssertPathNeverCutsAWallOrACorner(CoreMap map, IntVec3 start, PawnPath path)
        {
            IntVec3 prev = start;
            while (!path.Finished)
            {
                IntVec3 next = path.ConsumeNextNode();
                Assert.True(map.pathGrid.Walkable(next), $"path stepped onto unwalkable cell {next}");

                int dx = Math.Abs(next.x - prev.x), dz = Math.Abs(next.z - prev.z);
                Assert.True(dx <= 1 && dz <= 1 && dx + dz > 0, $"step from {prev} to {next} is not one adjacent cell");
                if (dx == 1 && dz == 1)
                {
                    Assert.True(map.pathGrid.Walkable(new IntVec3(next.x, 0, prev.z)), $"diagonal step {prev}->{next} cut a corner");
                    Assert.True(map.pathGrid.Walkable(new IntVec3(prev.x, 0, next.z)), $"diagonal step {prev}->{next} cut a corner");
                }
                prev = next;
            }
        }

        /// <summary>A <paramref name="roomsPerSide"/> x <paramref name="roomsPerSide"/> grid of
        /// <paramref name="roomSize"/> x <paramref name="roomSize"/> rooms (same wall-drawing rule as
        /// <c>RegionTests</c>'s own room-grid test), each door-connected to its right and lower neighbour so
        /// the whole grid is one connected, many-region, many-door graph — the shape path sharing exists for.
        /// </summary>
        private static CoreMap BuildConnectedRoomGrid(int roomsPerSide, int roomSize, out IntVec3[,] roomCenters)
        {
            int pitch = roomSize + 1;
            int mapSize = roomsPerSide * pitch - 1;
            CoreMap map = NewMap(mapSize, mapSize);

            roomCenters = new IntVec3[roomsPerSide, roomsPerSide];
            for (int rx = 0; rx < roomsPerSide; rx++)
            {
                for (int ry = 0; ry < roomsPerSide; ry++)
                {
                    roomCenters[rx, ry] = new IntVec3(rx * pitch + roomSize / 2, 0, ry * pitch + roomSize / 2);
                }
            }

            var doorCells = new HashSet<IntVec3>();
            for (int rx = 0; rx < roomsPerSide; rx++)
            {
                for (int ry = 0; ry < roomsPerSide; ry++)
                {
                    if (rx + 1 < roomsPerSide) doorCells.Add(new IntVec3(rx * pitch + roomSize, 0, ry * pitch + roomSize / 2));
                    if (ry + 1 < roomsPerSide) doorCells.Add(new IntVec3(rx * pitch + roomSize / 2, 0, ry * pitch + roomSize));
                }
            }

            for (int x = 0; x < mapSize; x++)
            {
                for (int z = 0; z < mapSize; z++)
                {
                    if (x % pitch != roomSize && z % pitch != roomSize) continue;
                    var c = new IntVec3(x, 0, z);
                    if (doorCells.Contains(c)) BuildDoor(map, c); else BuildWall(map, c);
                }
            }

            return map;
        }

        /// <summary>
        /// A map shaped so the region graph's cheapest-<i>hop-count</i> route and the true cheapest-
        /// <i>cell-cost</i> route disagree — see this fixture's remarks and <c>Hierarchical corridor picks the
        /// detour...</c> below. West room (x 0-5) and east room (x 11-16) span every row and are connected in
        /// exactly two ways: a fully open top row (z=8), which merges both rooms and the top row into a single
        /// region (hop distance zero — nothing beats that), and a straight, physically short chain of five
        /// doors at z=1, x 6-10, sealed off from the open rooms on every other side. A door is always its own
        /// single-cell region (<c>RegionMaker</c>) and this chain never touches the open top row, so it costs
        /// five region hops versus the top route's zero — the coarse search always prefers the top route
        /// despite it being the physically longer one.
        /// </summary>
        private static CoreMap BuildDetourMap()
        {
            const int width = 17, height = 9;
            CoreMap map = NewMap(width, height);
            for (int x = 6; x <= 10; x++)
            {
                for (int z = 0; z < height - 1; z++) // z 0-7; z=8 is left open, the top-row detour
                {
                    if (z == 1) BuildDoor(map, new IntVec3(x, 0, z));
                    else BuildWall(map, new IntVec3(x, 0, z));
                }
            }
            return map;
        }

        // ---- correctness: never through a wall, reachable stays reachable ----

        [Fact]
        public void Many_pawns_from_different_rooms_get_correct_paths_to_the_same_shared_destination()
        {
            CoreMap map = BuildConnectedRoomGrid(roomsPerSide: 3, roomSize: 3, out IntVec3[,] centers);
            IntVec3 destination = centers[2, 2];

            for (int rx = 0; rx < 3; rx++)
            {
                for (int ry = 0; ry < 3; ry++)
                {
                    if (rx == 2 && ry == 2) continue; // the destination's own room

                    Pawn pawn = SpawnHuman(map, centers[rx, ry], $"P{rx}{ry}");
                    PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, destination, PathEndMode.OnCell);
                    Assert.True(path.Found, $"room ({rx},{ry}) should reach the shared destination");
                    AssertPathNeverCutsAWallOrACorner(map, pawn.Position, path);
                    path.ReleaseToPool();
                }
            }
        }

        [Fact]
        public void A_previously_unreachable_destination_becomes_reachable_once_mining_a_wall_opens_a_connection()
        {
            CoreMap map = NewMap(9, 5);
            Thing gap = BuildWall(map, new IntVec3(4, 0, 2));
            for (int z = 0; z < 5; z++)
            {
                if (z != 2) BuildWall(map, new IntVec3(4, 0, z));
            }

            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 2));
            var dest = new IntVec3(8, 0, 2);

            // Sealed off entirely: this both exercises "reachable stays reachable" from the other direction
            // (unreachable must stay correctly unreachable) and warms the corridor cache with a tree that
            // does not include the pawn's own region at all.
            PawnPath before = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.False(before.Found, "fully walled off: nothing to path through yet");

            gap.Destroy(); // mined away, same trigger RegionTests.Mining_a_wall_reopens... uses

            PawnPath after = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.True(after.Found, "the region graph reconnected; a stale cached tree must not keep reporting unreachable");
            AssertPathNeverCutsAWallOrACorner(map, pawn.Position, after);
        }

        [Fact]
        public void Sealing_the_only_door_after_the_corridor_is_cached_correctly_makes_the_destination_unreachable()
        {
            CoreMap map = NewMap(5, 3);
            BuildWall(map, new IntVec3(2, 0, 0));
            Thing door = BuildDoor(map, new IntVec3(2, 0, 1));
            BuildWall(map, new IntVec3(2, 0, 2));

            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 1));
            var dest = new IntVec3(4, 0, 1);

            PawnPath before = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.True(before.Found, "the door connects the two halves");
            AssertPathNeverCutsAWallOrACorner(map, pawn.Position, before);

            door.Destroy();
            BuildWall(map, new IntVec3(2, 0, 1)); // seal the gap completely — no crossing left at all

            PawnPath after = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.False(after.Found, "the only crossing is gone; a stale cached corridor must not still find a path through it");
        }

        // ---- determinism: a cache hit answers exactly what a fresh build would ----

        [Fact]
        public void A_cache_hit_for_a_second_pawn_matches_what_an_independent_fresh_corridor_build_gives_for_its_own_start()
        {
            CoreMap map = BuildConnectedRoomGrid(roomsPerSide: 3, roomSize: 3, out IntVec3[,] centers);
            IntVec3 dest = centers[2, 2];

            // First pawn's query builds and caches the destination's corridor tree.
            Pawn first = SpawnHuman(map, centers[0, 0], "First");
            PawnPath firstPath = map.pathFinder.FindPath(first, first.Position, dest, PathEndMode.OnCell);
            Assert.True(firstPath.Found);
            firstPath.ReleaseToPool();

            // Second pawn, a different start, reuses that already-built tree — a cache hit, and exactly the
            // sharing this whole pass exists to deliver.
            Pawn second = SpawnHuman(map, centers[0, 2], "Second");
            PawnPath hit = map.pathFinder.FindPath(second, second.Position, dest, PathEndMode.OnCell);
            Assert.True(hit.Found);
            var hitCells = new List<IntVec3>();
            while (!hit.Finished) hitCells.Add(hit.ConsumeNextNode());
            hit.ReleaseToPool();

            // An independent PathFinder over the same map has its own, empty corridor cache, so this call is
            // guaranteed to build the tree fresh rather than reuse anything — the "uncached" side of the
            // comparison the determinism rule asks for.
            var freshFinder = new PathFinder(map);
            PawnPath fresh = freshFinder.FindPath(second, second.Position, dest, PathEndMode.OnCell);
            Assert.True(fresh.Found);
            var freshCells = new List<IntVec3>();
            while (!fresh.Finished) freshCells.Add(fresh.ConsumeNextNode());
            fresh.ReleaseToPool();

            Assert.Equal(freshCells, hitCells);
        }

        // ---- disclosed trade-off: correct, but not always optimal ----

        [Fact]
        public void Hierarchical_corridor_can_be_longer_than_optimal_but_is_still_a_valid_path()
        {
            CoreMap map = BuildDetourMap();
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 1));
            var dest = new IntVec3(11, 0, 1);

            PawnPath hierarchical = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.True(hierarchical.Found);
            int hierarchicalLen = hierarchical.NodesLeftCount;
            AssertPathNeverCutsAWallOrACorner(map, pawn.Position, hierarchical);

            // The corridor picked the zero-hop top-row detour over the five-hop door chain (see
            // BuildDetourMap) even though the door chain is the true shortest route — disabling sharing for
            // this one call recovers PathFinder's own unconstrained optimum to compare against.
            map.pathFinder.DisableRegionCorridor = true;
            PawnPath optimal = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            map.pathFinder.DisableRegionCorridor = false;
            Assert.True(optimal.Found);
            int optimalLen = optimal.NodesLeftCount;
            optimal.ReleaseToPool();

            // The door chain is a straight, one-cell-wide cardinal corridor: exactly 6 steps, no ambiguity.
            Assert.Equal(6, optimalLen);

            // Not hidden, bounded: the detour must cross z=1 to z=8 and back (14 steps of z-displacement
            // alone), so it is comfortably, provably longer than optimal — pinned as a generous ratio rather
            // than an exact literal so the test does not depend on the diagonal-move tie-breaking inside the
            // open detour region.
            Assert.True(hierarchicalLen > optimalLen, "the whole point of this test: the corridor is not optimal here");
            Assert.True(hierarchicalLen >= optimalLen * 2, $"expected a substantial detour, got {hierarchicalLen} vs optimal {optimalLen}");
        }

        [Fact]
        public void Disabling_region_corridor_is_a_pure_bench_test_knob_that_still_finds_the_same_optimum_every_time()
        {
            CoreMap map = BuildDetourMap();
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 1));
            var dest = new IntVec3(11, 0, 1);

            map.pathFinder.DisableRegionCorridor = true;
            PawnPath a = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            var aCells = new List<IntVec3>();
            while (!a.Finished) aCells.Add(a.ConsumeNextNode());
            a.ReleaseToPool();

            PawnPath b = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            var bCells = new List<IntVec3>();
            while (!b.Finished) bCells.Add(b.ConsumeNextNode());
            b.ReleaseToPool();
            map.pathFinder.DisableRegionCorridor = false;

            Assert.Equal(aCells, bCells);
        }
    }
}
