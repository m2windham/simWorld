using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The designation layer (system: mining): a mark on a cell or a Thing that citizens act on, kept on the
    /// map and saved with it (RimWorld: <c>Verse.Designation</c>, <c>DesignationDef</c>,
    /// <c>DesignationManager</c>).
    ///
    /// <para/>What this class pins is behaviour, not shape: a mark can be added once per kind per place, found
    /// again, and taken off; it ends exactly when the rock it was drawn over leaves the map, however it leaves;
    /// and a map that is saved and loaded still has every one of its marks. The hand-built defs below are
    /// never put in the <see cref="DefDatabase"/>, so they exercise the manager's rules about Thing-indexed
    /// marks and about marks that outlive a building without a shipped def having to ask for either — the
    /// shipped content only carries <c>Mine</c>, which is cell-indexed and ends with its rock.
    /// </summary>
    public class DesignationTests : ContentTestBase
    {
        public DesignationTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX = 10, int sizeZ = 10) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Mineable SpawnRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            var rock = (Mineable)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        private static Designation Mark(CoreMap map, IntVec3 cell)
        {
            var mark = new Designation(cell, DesignationDefOf.Mine);
            Assert.True(map.designationManager.AddDesignation(mark));
            return mark;
        }

        /// <summary>A cell-indexed kind that does NOT go with a despawning building.</summary>
        private static DesignationDef StickyCellMark() =>
            new DesignationDef { defName = "TestStickyCellMark", targetType = TargetType.Cell, removeIfBuildingDespawned = false };

        /// <summary>A Thing-indexed kind, as hunt and tame will be.</summary>
        private static DesignationDef ThingMark(bool cancelable = true) =>
            new DesignationDef { defName = "TestThingMark", targetType = TargetType.Thing, designateCancelable = cancelable };

        // ---- content ----

        [Fact]
        public void The_mine_designation_is_shipped_cell_indexed_and_ends_with_its_rock()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(DesignationDefOf.Mine);
            Assert.Equal("Mine", DesignationDefOf.Mine.defName);
            Assert.Equal(TargetType.Cell, DesignationDefOf.Mine.targetType);
            Assert.True(DesignationDefOf.Mine.removeIfBuildingDespawned,
                "Nothing else ends a mine mark: it must go when the rock does.");
        }

        // ---- adding and asking ----

        [Fact]
        public void A_mark_is_found_by_its_cell_and_not_by_a_neighbours()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(3, 0, 3));
            Designation mark = Mark(map, new IntVec3(3, 0, 3));

            Assert.Same(mark, map.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
            Assert.Null(map.designationManager.DesignationAt(new IntVec3(4, 0, 3), DesignationDefOf.Mine));
            Assert.Same(map.designationManager, mark.designationManager);
            Assert.Single(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_second_mark_of_the_same_kind_on_the_same_cell_is_refused_and_changes_nothing()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(3, 0, 3));
            Designation first = Mark(map, new IntVec3(3, 0, 3));

            bool added = map.designationManager.AddDesignation(new Designation(new IntVec3(3, 0, 3), DesignationDefOf.Mine));

            Assert.False(added);
            Assert.Single(map.designationManager.AllDesignations);
            Assert.Same(first, map.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
        }

        [Fact]
        public void Different_kinds_share_a_cell_and_are_listed_apart()
        {
            CoreMap map = NewMap();
            DesignationDef other = StickyCellMark();
            Mark(map, new IntVec3(2, 0, 2));
            Assert.True(map.designationManager.AddDesignation(new Designation(new IntVec3(2, 0, 2), other)));

            Assert.Equal(2, map.designationManager.AllDesignationsAt(new IntVec3(2, 0, 2)).Count());
            Assert.Single(map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine));
            Assert.Single(map.designationManager.SpawnedDesignationsOfDef(other));
        }

        [Fact]
        public void A_mark_off_the_map_is_refused()
        {
            CoreMap map = NewMap(5, 5);

            Assert.False(map.designationManager.AddDesignation(new Designation(new IntVec3(9, 0, 9), DesignationDefOf.Mine)));
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_cell_indexed_kind_cannot_be_looked_up_by_thing_and_a_thing_kind_not_by_cell()
        {
            CoreMap map = NewMap();
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 3));
            Mark(map, rock.Position);
            DesignationDef thingKind = ThingMark();
            Assert.True(map.designationManager.AddDesignation(new Designation(rock, thingKind)));

            // RimWorld logs an error for each of these; the answer here is the same "nothing" without the noise.
            Assert.Null(map.designationManager.DesignationOn(rock, DesignationDefOf.Mine));
            Assert.Null(map.designationManager.DesignationAt(rock.Position, thingKind));
            Assert.NotNull(map.designationManager.DesignationOn(rock, thingKind));
        }

        // ---- removing ----

        [Fact]
        public void Deleting_a_mark_takes_it_off_and_frees_the_cell_for_another()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(3, 0, 3));
            Designation mark = Mark(map, new IntVec3(3, 0, 3));

            mark.Delete();

            Assert.Empty(map.designationManager.AllDesignations);
            Assert.Null(map.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
            Mark(map, new IntVec3(3, 0, 3)); // and can be marked again
        }

        [Fact]
        public void TryRemoveDesignation_and_RemoveAllDesignationsOfDef_take_only_what_they_name()
        {
            CoreMap map = NewMap();
            DesignationDef other = StickyCellMark();
            Mark(map, new IntVec3(1, 0, 1));
            Mark(map, new IntVec3(2, 0, 2));
            map.designationManager.AddDesignation(new Designation(new IntVec3(1, 0, 1), other));

            map.designationManager.TryRemoveDesignation(new IntVec3(1, 0, 1), DesignationDefOf.Mine);
            Assert.Null(map.designationManager.DesignationAt(new IntVec3(1, 0, 1), DesignationDefOf.Mine));
            Assert.NotNull(map.designationManager.DesignationAt(new IntVec3(1, 0, 1), other));
            map.designationManager.TryRemoveDesignation(new IntVec3(9, 0, 9), DesignationDefOf.Mine); // nothing there: no-op

            map.designationManager.RemoveAllDesignationsOfDef(DesignationDefOf.Mine);
            Assert.Null(map.designationManager.DesignationAt(new IntVec3(2, 0, 2), DesignationDefOf.Mine));
            Assert.Single(map.designationManager.AllDesignations);
        }

        [Fact]
        public void Standard_cancelling_spares_a_kind_that_is_not_cancelable()
        {
            CoreMap map = NewMap();
            Mineable subject = SpawnRock(map, new IntVec3(3, 0, 3));
            DesignationDef cancelable = ThingMark(cancelable: true);
            DesignationDef fixedMark = new DesignationDef { defName = "TestFixedMark", targetType = TargetType.Thing, designateCancelable = false };
            map.designationManager.AddDesignation(new Designation(subject, cancelable));
            map.designationManager.AddDesignation(new Designation(subject, fixedMark));

            map.designationManager.RemoveAllDesignationsOn(subject, standardCanceling: true);

            Assert.Null(map.designationManager.DesignationOn(subject, cancelable));
            Assert.NotNull(map.designationManager.DesignationOn(subject, fixedMark));

            map.designationManager.RemoveAllDesignationsOn(subject);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        // ---- the mark ends with its rock ----

        [Fact]
        public void A_mine_mark_ends_when_its_rock_is_mined_out()
        {
            CoreMap map = NewMap();
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 3));
            Mark(map, rock.Position);

            rock.DestroyMined(null);

            Assert.True(rock.Destroyed);
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_mine_mark_ends_when_its_rock_leaves_the_map_any_other_way()
        {
            CoreMap map = NewMap();
            Mineable vanished = SpawnRock(map, new IntVec3(2, 0, 2));
            Mineable despawned = SpawnRock(map, new IntVec3(5, 0, 5));
            Mark(map, vanished.Position);
            Mark(map, despawned.Position);

            vanished.Destroy(DestroyMode.Vanish); // what map generation does when it carves a cave
            Assert.Single(map.designationManager.AllDesignations);

            despawned.DeSpawn();
            Assert.Empty(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_mark_on_a_cell_with_no_rock_in_it_is_not_taken_by_other_things_leaving()
        {
            CoreMap map = NewMap();
            Mark(map, new IntVec3(2, 0, 2));
            Thing log = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog"));
            GenSpawn.Spawn(log, new IntVec3(2, 0, 2), map);

            log.Destroy();

            Assert.Single(map.designationManager.AllDesignations);
        }

        [Fact]
        public void A_kind_that_does_not_ask_to_go_with_the_building_keeps_its_mark()
        {
            CoreMap map = NewMap();
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 3));
            DesignationDef sticky = StickyCellMark();
            map.designationManager.AddDesignation(new Designation(rock.Position, sticky));
            Mark(map, rock.Position);

            rock.DestroyMined(null);

            Assert.Null(map.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
            Assert.NotNull(map.designationManager.DesignationAt(new IntVec3(3, 0, 3), sticky));
        }

        [Fact]
        public void A_building_leaving_takes_only_the_marks_in_its_own_footprint()
        {
            CoreMap map = NewMap();
            Mineable rock = SpawnRock(map, new IntVec3(3, 0, 3));
            Mark(map, rock.Position);
            Mark(map, new IntVec3(4, 0, 3));

            map.designationManager.Notify_BuildingDespawned(rock);

            Assert.Null(map.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
            Assert.NotNull(map.designationManager.DesignationAt(new IntVec3(4, 0, 3), DesignationDefOf.Mine));
        }

        // ---- Scribe ----

        [Fact]
        public void Every_mark_survives_a_save_and_load_of_the_map()
        {
            CoreMap map = NewMap(12, 12);
            var marked = new List<IntVec3> { new IntVec3(2, 0, 2), new IntVec3(7, 0, 4), new IntVec3(11, 0, 11), new IntVec3(0, 0, 0) };
            foreach (IntVec3 cell in marked)
            {
                SpawnRock(map, cell);
                Mark(map, cell);
            }
            SpawnRock(map, new IntVec3(5, 0, 5)); // standing, never marked

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(marked.Count, loaded.designationManager.AllDesignations.Count);
            foreach (IntVec3 cell in marked)
            {
                Designation? found = loaded.designationManager.DesignationAt(cell, DesignationDefOf.Mine);
                Assert.NotNull(found);
                Assert.Same(DesignationDefOf.Mine, found!.def);
                Assert.Same(loaded.designationManager, found.designationManager);
            }
            Assert.Null(loaded.designationManager.DesignationAt(new IntVec3(5, 0, 5), DesignationDefOf.Mine));
        }

        [Fact]
        public void A_loaded_mark_still_ends_with_its_rock()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(3, 0, 3));
            Mark(map, new IntVec3(3, 0, 3));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var rock = (Mineable)loaded.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Sandstone"))[0];
            rock.DestroyMined(null);

            Assert.Empty(loaded.designationManager.AllDesignations);
            Assert.Null(loaded.designationManager.DesignationAt(new IntVec3(3, 0, 3), DesignationDefOf.Mine));
        }

        [Fact]
        public void A_map_saved_with_no_marks_loads_with_none()
        {
            CoreMap map = NewMap();
            SpawnRock(map, new IntVec3(3, 0, 3));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Empty(loaded.designationManager.AllDesignations);
        }
    }
}
