using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <see cref="Area.TrueCount"/> and <see cref="Area.ActiveCells"/> (RimWorld: <c>BoolGrid.TrueCount</c> and
    /// <c>BoolGrid.ActiveCells</c>, read from <c>josh-m/rw-decompile/Verse/BoolGrid.cs</c>). The count was a walk
    /// of the whole grid, asked once per piece of filth by <c>CleaningBounds.IsCleanable</c>, and came to
    /// sixty percent of an idle settlement's time. What these pin is that making it a kept count changed no
    /// answer: it must agree with the grid through every way a cell can be set, unset, loaded or mis-saved.
    /// </summary>
    public class AreaTrueCountTests : ContentTestBase
    {
        public AreaTrueCountTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        /// <summary>The answer the old implementation gave: a walk over every cell of the map.</summary>
        private static int CountByWalking(CoreMap map, Area area)
        {
            int n = 0;
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int z = 0; z < map.Size.z; z++)
                {
                    if (area[new IntVec3(x, 0, z)]) n++;
                }
            }
            return n;
        }

        [Fact]
        public void A_fresh_area_has_no_cells_and_yields_none()
        {
            CoreMap map = NewMap(12, 12);

            Assert.Equal(0, map.areaManager.Home.TrueCount);
            Assert.Empty(map.areaManager.Home.ActiveCells);
        }

        [Fact]
        public void Setting_a_cell_counts_it_once_however_often_it_is_set()
        {
            CoreMap map = NewMap(12, 12);
            Area home = map.areaManager.Home;
            var c = new IntVec3(3, 0, 4);

            home[c] = true;
            home[c] = true;
            home[c] = true;

            Assert.Equal(1, home.TrueCount);
            Assert.True(home[c]);
        }

        [Fact]
        public void Unsetting_a_cell_takes_it_off_the_count_and_unsetting_an_unset_cell_changes_nothing()
        {
            CoreMap map = NewMap(12, 12);
            Area home = map.areaManager.Home;
            var a = new IntVec3(1, 0, 1);
            var b = new IntVec3(2, 0, 2);
            home[a] = true;
            home[b] = true;

            home[a] = false;
            home[a] = false;
            home[new IntVec3(9, 0, 9)] = false;

            Assert.Equal(1, home.TrueCount);
            Assert.False(home[a]);
            Assert.True(home[b]);
        }

        [Fact]
        public void A_write_outside_the_map_is_ignored_and_not_counted()
        {
            CoreMap map = NewMap(12, 12);
            Area home = map.areaManager.Home;

            home[new IntVec3(-1, 0, 3)] = true;
            home[new IntVec3(12, 0, 3)] = true;
            home[new IntVec3(3, 0, 99)] = true;

            Assert.Equal(0, home.TrueCount);
            Assert.False(home[new IntVec3(-1, 0, 3)]);
        }

        [Fact]
        public void Areas_on_one_map_keep_their_own_counts()
        {
            CoreMap map = NewMap(12, 12);
            map.areaManager.Home[new IntVec3(1, 0, 1)] = true;
            map.areaManager.BuildRoof[new IntVec3(2, 0, 2)] = true;
            map.areaManager.BuildRoof[new IntVec3(3, 0, 3)] = true;

            Assert.Equal(1, map.areaManager.Home.TrueCount);
            Assert.Equal(2, map.areaManager.BuildRoof.TrueCount);
            Assert.Equal(0, map.areaManager.NoRoof.TrueCount);
        }

        /// <summary>The kept count against the old walk, and ActiveCells against both, after a long seeded run
        /// of sets and unsets that revisits cells — a trend over many states, not one hand-picked case.</summary>
        [Fact]
        public void The_kept_count_and_ActiveCells_agree_with_a_walk_of_the_grid_through_a_long_random_run()
        {
            CoreMap map = NewMap(23, 17);
            Area area = map.areaManager.Home;
            var rng = new RandomStream(7701);

            for (int step = 0; step < 4000; step++)
            {
                var c = new IntVec3(rng.Range(-2, 25), 0, rng.Range(-2, 19)); // some outside the map
                area[c] = rng.Range(0, 3) != 0; // set about two writes in three, so the area keeps growing and shrinking

                if (step % 97 == 0)
                {
                    int walked = CountByWalking(map, area);
                    Assert.Equal(walked, area.TrueCount);
                    List<IntVec3> active = area.ActiveCells.ToList();
                    Assert.Equal(walked, active.Count);
                    Assert.All(active, a => Assert.True(area[a]));
                }
            }
            Assert.Equal(CountByWalking(map, area), area.TrueCount);
        }

        [Fact]
        public void ActiveCells_come_in_cell_index_order_and_are_exactly_the_set_cells()
        {
            CoreMap map = NewMap(10, 10);
            Area area = map.areaManager.BuildRoof;
            var cells = new[] { new IntVec3(7, 0, 8), new IntVec3(0, 0, 0), new IntVec3(9, 0, 9), new IntVec3(3, 0, 4), new IntVec3(5, 0, 0) };
            foreach (IntVec3 c in cells) area[c] = true;

            List<int> indices = area.ActiveCells.Select(c => map.cellIndices.CellToIndex(c)).ToList();

            Assert.Equal(cells.Length, indices.Count);
            Assert.Equal(indices.OrderBy(i => i).ToList(), indices);
            Assert.Equal(cells.Select(c => map.cellIndices.CellToIndex(c)).OrderBy(i => i), indices);
        }

        [Fact]
        public void ActiveCells_of_an_area_whose_last_cell_is_set_is_complete_and_of_one_whose_first_cell_is_set_is_just_that_cell()
        {
            CoreMap map = NewMap(10, 10);
            Area first = map.areaManager.Home;
            Area last = map.areaManager.BuildRoof;
            first[new IntVec3(0, 0, 0)] = true;
            last[new IntVec3(9, 0, 9)] = true;

            Assert.Equal(new[] { new IntVec3(0, 0, 0) }, first.ActiveCells.ToArray());
            Assert.Equal(new[] { new IntVec3(9, 0, 9) }, last.ActiveCells.ToArray());
        }

        [Fact]
        public void The_count_survives_a_Scribe_round_trip_with_every_cell_and_a_matching_ActiveCells()
        {
            CoreMap map = NewMap(20, 20);
            var rng = new RandomStream(7702);
            for (int i = 0; i < 120; i++)
            {
                var c = new IntVec3(rng.Range(0, 20), 0, rng.Range(0, 20));
                map.areaManager.Home[c] = true;
                if (i % 3 == 0) map.areaManager.BuildRoof[c] = true;
                if (i % 7 == 0) map.areaManager.Home[c] = false;
            }
            int home = map.areaManager.Home.TrueCount;
            int roof = map.areaManager.BuildRoof.TrueCount;
            Assert.True(home > 0 && roof > 0);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(home, loaded.areaManager.Home.TrueCount);
            Assert.Equal(roof, loaded.areaManager.BuildRoof.TrueCount);
            Assert.Equal(0, loaded.areaManager.NoRoof.TrueCount);
            Assert.Equal(map.areaManager.Home.ActiveCells.ToList(), loaded.areaManager.Home.ActiveCells.ToList());
            Assert.Equal(CountByWalking(loaded, loaded.areaManager.Home), loaded.areaManager.Home.TrueCount);

            // And the loaded count keeps tracking: it is a live count, not a number restored once.
            loaded.areaManager.Home[new IntVec3(19, 0, 19)] = true;
            loaded.areaManager.Home[new IntVec3(19, 0, 19)] = false;
            Assert.Equal(home, loaded.areaManager.Home.TrueCount);
        }

        /// <summary>Scribe constructs its root itself, so the area under test is reached through a static; the
        /// tests of this class share one collection and never run beside each other.</summary>
        public sealed class AreaHolder : IExposable
        {
            public static Area? Target;

            public void ExposeData() => Target!.ExposeData("a");
        }

        [Fact]
        public void Loading_over_an_area_that_already_holds_cells_replaces_them_and_recounts()
        {
            CoreMap map = NewMap(10, 10);
            Area saved = new Area(map, "saved");
            saved[new IntVec3(1, 0, 1)] = true;
            saved[new IntVec3(2, 0, 2)] = true;
            AreaHolder.Target = saved;
            string xml = Scribe.SaveToString(new AreaHolder(), "holder");

            var target = new Area(map, "target");
            foreach (IntVec3 c in new CellRect(4, 4, 3, 3).Cells) target[c] = true; // 9 cells that are not in the save
            Assert.Equal(9, target.TrueCount);

            AreaHolder.Target = target;
            Scribe.Load<AreaHolder>(xml, "holder", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(2, target.TrueCount);
            Assert.Equal(CountByWalking(map, target), target.TrueCount);
            Assert.False(target[new IntVec3(5, 0, 5)]);
            Assert.True(target[new IntVec3(1, 0, 1)]);
        }

        /// <summary>A save is a file somebody can edit or truncate. A repeated cell index must not be counted
        /// twice and an out-of-range one must not be counted at all, or the count would disagree with the
        /// grid for the rest of the game.</summary>
        [Fact]
        public void A_hand_edited_save_with_a_repeated_and_an_out_of_range_cell_still_counts_what_the_grid_holds()
        {
            CoreMap saved = NewMap(10, 10);
            saved.areaManager.Home[new IntVec3(1, 0, 1)] = true;
            saved.areaManager.Home[new IntVec3(2, 0, 2)] = true;
            XDocument doc = XDocument.Parse(Scribe.SaveToString(saved, "map"));

            XElement trueCells = doc.Descendants().First(e => e.Name.LocalName == "homeTrueCells");
            XElement firstItem = trueCells.Elements().First();
            trueCells.Add(new XElement(firstItem.Name, firstItem.Value));        // the same cell again
            trueCells.Add(new XElement(firstItem.Name, "999999"));              // beyond the grid
            trueCells.Add(new XElement(firstItem.Name, "-4"));                  // before it

            CoreMap loaded = Scribe.Load<CoreMap>(doc.ToString(), "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(2, loaded.areaManager.Home.TrueCount);
            Assert.Equal(CountByWalking(loaded, loaded.areaManager.Home), loaded.areaManager.Home.TrueCount);
            Assert.Equal(2, loaded.areaManager.Home.ActiveCells.Count());
        }
    }
}
