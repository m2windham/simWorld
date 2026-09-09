using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The Region/RegionLink graph and its incremental rebuild (RimWorld: <c>Verse.Region</c> /
    /// <c>Verse.RegionLink</c> / <c>Verse.RegionGrid</c> / <c>Verse.RegionMaker</c> /
    /// <c>Verse.RegionAndRoomUpdater</c> / <c>Verse.RegionTraverser</c>). <see cref="AI.Reachability"/>'s own
    /// tests in <c>AITests.cs</c> cover the public reachability contract this graph now answers underneath;
    /// these tests cover the graph itself.
    /// </summary>
    public class RegionTests : ContentTestBase
    {
        public RegionTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Thing BuildWall(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), c, map);

        private static Thing BuildDoor(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Door")), c, map);

        // ---- basic shape ----

        [Fact]
        public void Region_flood_fill_does_not_fragment_a_large_open_area()
        {
            // Pins down this class's own documented decision not to bake in an unsourced per-region cell
            // cap: a big open room stays one region, exactly like real RimWorld's boundary-only growth.
            CoreMap map = NewMap(40, 40);
            map.regionAndRoomUpdater.RebuildIfNeeded();

            Assert.Single(map.regionGrid.AllRegions);
            Assert.Equal(1600, map.regionGrid.AllRegions[0].Cells.Count);
        }

        [Fact]
        public void A_wall_with_no_doorway_splits_one_region_into_two_unlinked_regions()
        {
            CoreMap map = NewMap(9, 5);
            for (int z = 0; z < 5; z++) BuildWall(map, new IntVec3(4, 0, z));

            map.regionAndRoomUpdater.RebuildIfNeeded();

            Region? left = map.regionGrid.RegionAt(new IntVec3(0, 0, 2));
            Region? right = map.regionGrid.RegionAt(new IntVec3(8, 0, 2));
            Assert.NotNull(left);
            Assert.NotNull(right);
            Assert.NotSame(left, right);
            Assert.False(RegionTraverser.WithinRegions(left!, right!));
            Assert.Null(map.regionGrid.RegionAt(new IntVec3(4, 0, 2))); // the wall cell itself has no region
        }

        [Fact]
        public void A_door_becomes_its_own_Portal_region_linking_the_regions_on_either_side()
        {
            CoreMap map = NewMap(5, 3);
            BuildWall(map, new IntVec3(2, 0, 0));
            BuildDoor(map, new IntVec3(2, 0, 1));
            BuildWall(map, new IntVec3(2, 0, 2));

            map.regionAndRoomUpdater.RebuildIfNeeded();

            Region? doorRegion = map.regionGrid.RegionAt(new IntVec3(2, 0, 1));
            Region? leftRegion = map.regionGrid.RegionAt(new IntVec3(0, 0, 1));
            Region? rightRegion = map.regionGrid.RegionAt(new IntVec3(4, 0, 1));

            Assert.NotNull(doorRegion);
            Assert.NotNull(leftRegion);
            Assert.NotNull(rightRegion);
            Assert.Equal(RegionType.Portal, doorRegion!.type);
            Assert.Single(doorRegion.Cells);
            Assert.NotSame(leftRegion, doorRegion);
            Assert.NotSame(rightRegion, doorRegion);
            Assert.NotSame(leftRegion, rightRegion);

            Assert.Contains(doorRegion.Links, l => ReferenceEquals(l.GetOtherRegion(doorRegion), leftRegion));
            Assert.Contains(doorRegion.Links, l => ReferenceEquals(l.GetOtherRegion(doorRegion), rightRegion));
            Assert.True(RegionTraverser.WithinRegions(leftRegion!, rightRegion!));

            // And the public reachability query the graph now serves agrees: the door bridges what would
            // otherwise be a solid wall.
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 1), map);
            Assert.True(Reachability.CanReach(pawn, new IntVec3(4, 0, 1)));
        }

        // ---- incremental invalidation: the whole point of this pass ----

        [Fact]
        public void Building_one_wall_rebuilds_only_the_region_it_touches_not_the_whole_map()
        {
            // A 5x5 grid of 3x3 rooms, each fully enclosed by walls (no doors) — 25 completely separate
            // regions, nowhere near enough to be a "large open area" that could hide a whole-map rebuild.
            const int roomsPerSide = 5;
            const int roomSize = 3;
            const int pitch = roomSize + 1; // one wall cell between rooms
            int mapSize = roomsPerSide * pitch - 1; // no trailing wall past the last room

            CoreMap map = NewMap(mapSize, mapSize);
            for (int x = 0; x < mapSize; x++)
            {
                for (int z = 0; z < mapSize; z++)
                {
                    if (x % pitch == roomSize || z % pitch == roomSize)
                    {
                        BuildWall(map, new IntVec3(x, 0, z));
                    }
                }
            }

            map.regionAndRoomUpdater.RebuildIfNeeded();
            Assert.Equal(roomsPerSide * roomsPerSide, map.regionGrid.AllRegions.Count);

            // Capture region identity for a handful of rooms far from the one about to be split.
            var farCells = new List<IntVec3>
            {
                new IntVec3(mapSize - 2, 0, mapSize - 2), // room (4,4)
                new IntVec3(mapSize - 2, 0, 1), // room (4,0)
                new IntVec3(1, 0, mapSize - 2), // room (0,4)
            };
            var before = new List<Region>();
            foreach (IntVec3 c in farCells)
            {
                Region? r = map.regionGrid.RegionAt(c);
                Assert.NotNull(r);
                before.Add(r!);
            }

            // Bisect room (0,0)'s 3x3 interior with a one-cell-wide wall, splitting it into two regions.
            BuildWall(map, new IntVec3(1, 0, 0));
            BuildWall(map, new IntVec3(1, 0, 1));
            BuildWall(map, new IntVec3(1, 0, 2));

            map.regionAndRoomUpdater.RebuildIfNeeded();

            // The whole point: every region that never touched a dirty cell is the exact same object it was
            // before — not rebuilt, not replaced by an equivalent-looking new one.
            for (int i = 0; i < farCells.Count; i++)
            {
                Assert.Same(before[i], map.regionGrid.RegionAt(farCells[i]));
            }

            // A bound, not a literal: this single interior wall must not have rebuilt anywhere near all 25
            // rooms' worth of regions, even though it did (correctly) split its own room into two.
            Assert.InRange(map.regionAndRoomUpdater.RegionsCreatedLastRebuild, 1, 5);
            Assert.Equal(roomsPerSide * roomsPerSide + 1, map.regionGrid.AllRegions.Count);

            Region? splitLeft = map.regionGrid.RegionAt(new IntVec3(0, 0, 1));
            Region? splitRight = map.regionGrid.RegionAt(new IntVec3(2, 0, 1));
            Assert.NotNull(splitLeft);
            Assert.NotNull(splitRight);
            Assert.NotSame(splitLeft, splitRight);
            Assert.False(RegionTraverser.WithinRegions(splitLeft!, splitRight!));
        }

        [Fact]
        public void Mining_a_wall_reopens_a_walkable_cell_and_reconnects_the_regions_on_either_side()
        {
            CoreMap map = NewMap(9, 5);
            Thing wall = BuildWall(map, new IntVec3(4, 0, 2));
            for (int z = 0; z < 5; z++)
            {
                if (z != 2) BuildWall(map, new IntVec3(4, 0, z));
            }

            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region leftBefore = map.regionGrid.RegionAt(new IntVec3(0, 0, 2))!;
            Region rightBefore = map.regionGrid.RegionAt(new IntVec3(8, 0, 2))!;
            Assert.NotSame(leftBefore, rightBefore);
            Assert.False(RegionTraverser.WithinRegions(leftBefore, rightBefore));

            wall.Destroy(); // e.g. mined away

            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region? merged = map.regionGrid.RegionAt(new IntVec3(4, 0, 2));
            Assert.NotNull(merged);
            Assert.Same(merged, map.regionGrid.RegionAt(new IntVec3(0, 0, 2)));
            Assert.Same(merged, map.regionGrid.RegionAt(new IntVec3(8, 0, 2)));
        }

        [Fact]
        public void Pawn_spawning_and_despawning_does_not_dirty_the_region_graph()
        {
            CoreMap map = NewMap(10, 10);
            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region before = map.regionGrid.RegionAt(new IntVec3(5, 0, 5))!;

            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 0), map);
            map.regionAndRoomUpdater.RebuildIfNeeded();
            Assert.Same(before, map.regionGrid.RegionAt(new IntVec3(5, 0, 5)));

            pawn.Destroy();
            map.regionAndRoomUpdater.RebuildIfNeeded();
            Assert.Same(before, map.regionGrid.RegionAt(new IntVec3(5, 0, 5)));
        }

        // ---- Scribe: regions are never saved, only rebuilt ----

        [Fact]
        public void Region_graph_is_not_saved_and_rebuilds_correctly_after_a_scribe_round_trip()
        {
            CoreMap map = NewMap(9, 5);
            for (int z = 0; z < 5; z++) BuildWall(map, new IntVec3(4, 0, z));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            // Nothing region-shaped is in the saved XML at all — only the wall Things and the grids they
            // imply. The graph on the loaded map starts completely unbuilt...
            Assert.Empty(loaded.regionGrid.AllRegions);

            // ...and the first query rebuilds it correctly from the loaded map's own grids.
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 2), loaded);
            Assert.True(Reachability.CanReach(pawn, new IntVec3(2, 0, 2)));
            Assert.False(Reachability.CanReach(pawn, new IntVec3(8, 0, 2)));
        }
    }
}
