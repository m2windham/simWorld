using System.Collections.Generic;
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
    /// <see cref="AutoBuildRoofAreaSetter"/> (RimWorld: <c>Verse.AutoBuildRoofAreaSetter</c>, read from
    /// <c>josh-m/rw-decompile/Verse/AutoBuildRoofAreaSetter.cs</c>) — an enclosed room the settlement built
    /// growing its own <see cref="AreaManager.BuildRoof"/>, the way <see cref="AutoHomeAreaMakerTests"/> pins
    /// the home area growing on its own around what the settlement builds. See that class's own doc for the
    /// two translations these tests pin: the home-area requirement (this port's own addition, not RimWorld's)
    /// and <see cref="ThingDef.mineable"/> standing in for RimWorld's Faction check.
    /// </summary>
    public class AutoBuildRoofAreaSetterTests : ContentTestBase
    {
        public AutoBuildRoofAreaSetterTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>Walls (and, if given, one door) around <paramref name="interior"/>'s own perimeter, spawned
        /// straight onto the map — real settlement construction as far as <see cref="RoofUtility.GetRoofHolderOrImpassable"/>
        /// is concerned (an ordinary, non-mineable <see cref="Building.Building"/>), without needing the full
        /// Blueprint→Frame→<see cref="Frame.CompleteConstruction"/> pipeline <see cref="AutoHomeAreaMakerTests"/>
        /// uses to get the same fact — that pipeline is what marks home area on its own, and this class's own
        /// home-area gate is tested independently of it below, so these tests mark <see cref="AreaManager.Home"/>
        /// by hand instead.</summary>
        private static void BuildWallRing(CoreMap map, CellRect interior, IntVec3? doorAt = null)
        {
            CellRect ring = interior.ExpandedBy(1);
            foreach (IntVec3 c in ring.EdgeCells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                ThingDef def = doorAt.HasValue && c == doorAt.Value ? Def("Door") : Def("Wall");
                GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
            }
        }

        private static void MarkHome(CoreMap map, CellRect area)
        {
            foreach (IntVec3 c in area.ClipInsideMap(map).Cells) map.areaManager.Home[c] = true;
        }

        // ---- the settlement's own construction, inside its own home area, gets auto-roofed ----

        [Fact]
        public void An_enclosed_walled_room_in_the_home_area_gets_a_build_roof_area()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 3, 3);
            var doorAt = new IntVec3(9, 0, 7);
            BuildWallRing(map, interior, doorAt);
            MarkHome(map, interior.ExpandedBy(1));

            map.MapTick();

            // Every interior floor cell, and every wall/door cell adjacent to one, is a legitimate roof cell
            // (RimWorld roofs over the walls holding a room up, not just its floor).
            foreach (IntVec3 c in interior.ExpandedBy(1).Cells)
            {
                Assert.True(map.areaManager.BuildRoof[c], c + " should have been added to the build-roof area");
            }

            // Nothing outside the room's own ring is touched.
            Assert.False(map.areaManager.BuildRoof[new IntVec3(2, 0, 2)]);
        }

        [Fact]
        public void A_room_outside_the_home_area_is_not_auto_roofed()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 3, 3);
            BuildWallRing(map, interior);
            // No MarkHome call at all — this port's own addition (see the class doc's second bullet).

            map.MapTick();

            foreach (IntVec3 c in interior.ExpandedBy(1).Cells)
            {
                Assert.False(map.areaManager.BuildRoof[c], c + " should not auto-roof outside the home area");
            }
        }

        [Fact]
        public void A_NoRoof_cell_is_never_added_to_the_build_roof_area()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 3, 3);
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));

            var forbidden = new IntVec3(9, 0, 9); // the interior's own centre cell
            map.areaManager.NoRoof[forbidden] = true;

            map.MapTick();

            Assert.False(map.areaManager.BuildRoof[forbidden], "a NoRoof cell must never enter BuildRoof");
            // A neighbouring cell, with nothing forbidding it, is still added — the refusal is per-cell.
            Assert.True(map.areaManager.BuildRoof[new IntVec3(8, 0, 9)]);
        }

        /// <summary>A room deep enough that its own centre sits further than
        /// <see cref="RoofCollapseUtility.RoofMaxSupportDistance"/> from every wall (a straight-line 6.9,
        /// sourced from decompile): the wall-adjacent ring still qualifies, the centre does not. 17x17 keeps
        /// the whole room's cell count (289) under <see cref="AutoBuildRoofAreaSetter.MaxCellCount"/> (320), so
        /// this pins the support-range gate on its own, independent of the size gate below.</summary>
        [Fact]
        public void A_cell_out_of_range_of_any_roof_holder_is_not_auto_roofed()
        {
            CoreMap map = NewMap(30, 30);
            var interior = new CellRect(5, 5, 17, 17); // centre at (13, 13); half-width 8 > 6.9
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));
            Assert.True(interior.Area <= AutoBuildRoofAreaSetter.MaxCellCount, "test setup: keep this under the size gate");

            map.MapTick();

            var nearWall = new IntVec3(5, 0, 13); // one cell in from the west wall
            var centre = new IntVec3(13, 0, 13);
            Assert.True(map.areaManager.BuildRoof[nearWall], "a cell next to a wall should be within support range");
            Assert.False(map.areaManager.BuildRoof[centre], "the room's own centre is more than 6.9 cells from every wall");
        }

        // ---- the two size/shape gates ----

        [Fact]
        public void A_room_touching_the_map_edge_is_not_auto_roofed()
        {
            CoreMap map = NewMap(20, 20);
            // Interior starts at x = 0: every cell on that edge is at the map's own boundary, regardless of
            // whether a wall could even be placed one further cell out (it cannot — that cell is off-map).
            var interior = new CellRect(0, 5, 3, 3);
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));

            map.MapTick();

            Room? room = map.roomTracker.RoomAt(new IntVec3(1, 0, 6));
            Assert.NotNull(room);
            Assert.True(room!.TouchesMapEdge, "test setup: this room must actually touch the map edge");

            foreach (IntVec3 c in interior.Cells)
            {
                Assert.False(map.areaManager.BuildRoof[c], c + " a room touching the map edge must not auto-roof");
            }
        }

        [Fact]
        public void A_room_larger_than_RimWorlds_320_cell_limit_is_not_auto_roofed()
        {
            CoreMap map = NewMap(40, 40);
            var interior = new CellRect(2, 2, 19, 19); // 361 cells > 320
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));
            Assert.True(interior.Area > AutoBuildRoofAreaSetter.MaxCellCount, "test setup: this room must exceed the cap");

            map.MapTick();

            Room? room = map.roomTracker.RoomAt(new IntVec3(10, 0, 10));
            Assert.NotNull(room);
            Assert.False(room!.TouchesMapEdge, "test setup: only the size gate should be in play here");
            Assert.True(room.Cells.Count > AutoBuildRoofAreaSetter.MaxCellCount);

            foreach (IntVec3 c in interior.Cells)
            {
                Assert.False(map.areaManager.BuildRoof[c], c + " a too-large room must not auto-roof");
            }
        }

        /// <summary>A room bordered only by bare, unmined rock — no settlement construction anywhere on its
        /// border — is left alone even though every cell of it sits inside a home area painted over the whole
        /// thing (RimWorld: the room's border edifices all carry a null Faction, so the "at least one border
        /// edifice belongs to the player" flag is never set). <see cref="ThingDef.mineable"/> is this port's
        /// substitute — see <see cref="AutoBuildRoofAreaSetter"/>'s own doc.</summary>
        [Fact]
        public void A_room_bordered_only_by_bare_rock_is_not_auto_roofed_even_inside_the_home_area()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 3, 3);
            CellRect ring = interior.ExpandedBy(1);
            ThingDef rock = Def("Granite");
            foreach (IntVec3 c in ring.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(rock), c, map);
            MarkHome(map, ring);

            map.MapTick();

            foreach (IntVec3 c in interior.Cells)
            {
                Assert.False(map.areaManager.BuildRoof[c], c + " a room enclosed only by natural rock must not auto-roof");
            }
        }

        // ---- Scribe ----

        [Fact]
        public void BuildRoof_and_NoRoof_areas_round_trip_through_Scribe_independently_of_Home()
        {
            CoreMap map = NewMap(20, 20);
            var interior = new CellRect(8, 8, 3, 3);
            BuildWallRing(map, interior);
            MarkHome(map, interior.ExpandedBy(1));
            map.areaManager.NoRoof[new IntVec3(2, 0, 2)] = true;
            map.MapTick();

            int buildRoofBefore = map.areaManager.BuildRoof.TrueCount;
            int noRoofBefore = map.areaManager.NoRoof.TrueCount;
            int homeBefore = map.areaManager.Home.TrueCount;
            Assert.True(buildRoofBefore > 0);
            Assert.Equal(1, noRoofBefore);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            // Each area kept its own cells — not shuffled between each other by a shared Scribe label (see
            // Building.Area.ExposeData's own doc on why each of the three needs a distinct one).
            Assert.Equal(buildRoofBefore, loaded.areaManager.BuildRoof.TrueCount);
            Assert.Equal(noRoofBefore, loaded.areaManager.NoRoof.TrueCount);
            Assert.Equal(homeBefore, loaded.areaManager.Home.TrueCount);
            Assert.All(map.AllCells, c =>
            {
                Assert.Equal(map.areaManager.BuildRoof[c], loaded.areaManager.BuildRoof[c]);
                Assert.Equal(map.areaManager.NoRoof[c], loaded.areaManager.NoRoof[c]);
                Assert.Equal(map.areaManager.Home[c], loaded.areaManager.Home[c]);
            });
            Assert.True(loaded.areaManager.NoRoof[new IntVec3(2, 0, 2)]);
        }
    }
}
