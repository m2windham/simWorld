using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// <see cref="ThingRequestGroup"/> and <see cref="ThingRequestGroupUtility.Includes"/> (RimWorld:
    /// <c>Verse.ThingRequestGroup</c>, <c>Verse.ThingListGroupHelper.Includes</c>, read from
    /// <c>josh-m/rw-decompile/Verse/ThingListGroupHelper.cs</c>), and the two groups added for the work
    /// givers that were walking every building on the map: <see cref="ThingRequestGroup.PotentialBillGiver"/>
    /// and <see cref="ThingRequestGroup.ResearchBench"/>.
    /// </summary>
    public class ThingRequestGroupTests : ContentTestBase
    {
        public ThingRequestGroupTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>What <see cref="ListerThings"/> decided before the groups moved into
        /// <see cref="ThingRequestGroupUtility"/>: a switch on the def's category. Every group that existed then
        /// must still hold exactly the defs it held.</summary>
        private static bool OlderGroupHolds(ThingRequestGroup group, ThingDef def)
        {
            switch (group)
            {
                case ThingRequestGroup.Pawn: return def.category == ThingCategory.Pawn;
                case ThingRequestGroup.Building: return def.category == ThingCategory.Building;
                case ThingRequestGroup.BuildingArtificial: return def.category == ThingCategory.Building && def.mineable == false;
                case ThingRequestGroup.Item: return def.category == ThingCategory.Item;
                case ThingRequestGroup.Plant: return def.category == ThingCategory.Plant;
                case ThingRequestGroup.Filth: return def.category == ThingCategory.Filth;
                case ThingRequestGroup.Blueprint: return def.category == ThingCategory.Blueprint;
                case ThingRequestGroup.BuildingFrame: return def.category == ThingCategory.Frame;
                case ThingRequestGroup.HaulableEver: return def.EverHaulable;
                default: throw new System.ArgumentOutOfRangeException(nameof(group));
            }
        }

        private static readonly ThingRequestGroup[] OlderGroups =
        {
            ThingRequestGroup.Pawn, ThingRequestGroup.Building, ThingRequestGroup.BuildingArtificial,
            ThingRequestGroup.Item, ThingRequestGroup.Plant, ThingRequestGroup.Filth, ThingRequestGroup.Blueprint,
            ThingRequestGroup.BuildingFrame, ThingRequestGroup.HaulableEver,
        };

        // ---- the groups ----

        [Fact]
        public void The_stored_groups_are_every_group_but_Undefined_and_Everything()
        {
            ThingRequestGroup[] all = (ThingRequestGroup[])System.Enum.GetValues(typeof(ThingRequestGroup));
            Assert.Equal(
                all.Where(g => g != ThingRequestGroup.Undefined && g != ThingRequestGroup.Everything).OrderBy(g => g),
                ThingRequestGroupUtility.StoredGroups.OrderBy(g => g));
            Assert.Contains(ThingRequestGroup.PotentialBillGiver, ThingRequestGroupUtility.StoredGroups);
            Assert.Contains(ThingRequestGroup.ResearchBench, ThingRequestGroupUtility.StoredGroups);
        }

        [Fact]
        public void Every_group_is_decided_for_every_def_in_the_content_without_throwing()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                foreach (ThingRequestGroup group in ThingRequestGroupUtility.StoredGroups)
                {
                    _ = group.Includes(def);
                }
            }
        }

        [Fact]
        public void The_groups_that_existed_before_still_hold_exactly_the_defs_they_held()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                foreach (ThingRequestGroup group in OlderGroups)
                {
                    Assert.True(
                        OlderGroupHolds(group, def) == group.Includes(def),
                        def.defName + " in " + group + ": membership moved");
                }
            }
        }

        [Fact]
        public void Includes_rejects_Undefined_and_accepts_Everything()
        {
            ThingDef wall = Def("Wall");
            Assert.False(ThingRequestGroup.Undefined.Includes(wall));
            Assert.True(ThingRequestGroup.Everything.Includes(wall));
        }

        // ---- PotentialBillGiver ----

        [Fact]
        public void A_bench_with_a_bill_giver_comp_is_a_potential_bill_giver()
        {
            foreach (string bench in new[] { "Smithy", "TableTailor", "TableStonecutter" })
            {
                Assert.True(ThingRequestGroup.PotentialBillGiver.Includes(Def(bench)), bench);
            }
        }

        [Fact]
        public void The_butcher_table_is_a_potential_bill_giver_through_the_recipe_that_names_it_with_no_comp_at_all()
        {
            ThingDef table = Def("TableButcher");
            Assert.Null(table.comps?.OfType<CompProperties_BillGiver>().FirstOrDefault());
            Assert.NotEmpty(table.AllRecipes);

            Assert.True(ThingRequestGroup.PotentialBillGiver.Includes(table));
        }

        [Fact]
        public void A_building_with_neither_a_bill_giver_comp_nor_a_recipe_is_not()
        {
            foreach (string notABench in new[] { "Wall", "Door", "Bed", "StorageHut", "Limestone", "ResearchBench" })
            {
                Assert.False(ThingRequestGroup.PotentialBillGiver.Includes(Def(notABench)), notABench);
            }
        }

        [Fact]
        public void Items_and_plants_are_never_potential_bill_givers()
        {
            Assert.False(ThingRequestGroup.PotentialBillGiver.Includes(Def("WoodLog")));
            Assert.False(ThingRequestGroup.PotentialBillGiver.Includes(Def("Plant_Rice")));
        }

        [Fact]
        public void A_def_that_carries_the_comp_but_no_recipe_is_still_a_potential_bill_giver()
        {
            // The old whole-building walk offered any building whose instance carried a CompBillGiver, recipes
            // or not; the group must not drop one.
            var bench = new ThingDef
            {
                defName = "TestBenchNoRecipes",
                category = ThingCategory.Building,
                comps = new List<CompProperties> { new CompProperties_BillGiver { workType = DefDatabase<SimWorld.Work.WorkTypeDef>.GetNamed("Crafting") } },
            };

            Assert.Empty(bench.AllRecipes);
            Assert.True(ThingRequestGroup.PotentialBillGiver.Includes(bench));
        }

        [Fact]
        public void The_potential_bill_giver_group_lists_the_benches_in_the_order_they_were_spawned_and_follows_them_out()
        {
            CoreMap map = NewMap(20, 20);
            Thing wall1 = Spawn(map, new IntVec3(1, 0, 1), "Wall");
            Thing smithy = Spawn(map, new IntVec3(3, 0, 3), "Smithy");
            Thing rock = Spawn(map, new IntVec3(5, 0, 5), "Limestone");
            Thing tailor = Spawn(map, new IntVec3(7, 0, 7), "TableTailor");
            Thing butcher = Spawn(map, new IntVec3(9, 0, 9), "TableButcher");
            Thing wall2 = Spawn(map, new IntVec3(11, 0, 11), "Wall");

            Assert.Equal(new[] { smithy, tailor, butcher }, map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).ToArray());
            Assert.Equal(6, map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count);

            tailor.DeSpawn();
            Assert.Equal(new[] { smithy, butcher }, map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).ToArray());
            Assert.Equal(5, map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count);
        }

        // ---- ResearchBench ----

        [Fact]
        public void Only_the_research_bench_is_in_the_research_bench_group()
        {
            Assert.True(ThingRequestGroup.ResearchBench.Includes(ResearchWorkDefOf.ResearchBench));
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d != ResearchWorkDefOf.ResearchBench))
            {
                Assert.False(ThingRequestGroup.ResearchBench.Includes(def), def.defName);
            }
        }

        [Fact]
        public void The_research_bench_group_is_the_benches_alone_among_a_map_full_of_buildings()
        {
            CoreMap map = NewMap(30, 30);
            for (int i = 0; i < 25; i++) Spawn(map, new IntVec3(i, 0, 0), i % 2 == 0 ? "Limestone" : "Wall");
            Thing bench1 = Spawn(map, new IntVec3(2, 0, 5), "ResearchBench");
            Spawn(map, new IntVec3(4, 0, 5), "Smithy");
            Thing bench2 = Spawn(map, new IntVec3(6, 0, 5), "ResearchBench");

            Assert.Equal(new[] { bench1, bench2 }, map.listerThings.ThingsInGroup(ThingRequestGroup.ResearchBench).ToArray());
            Assert.Equal(28, map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count);
        }

        // ---- the lister ----

        [Fact]
        public void A_group_nothing_is_in_is_empty_and_a_thing_in_no_group_is_still_in_the_master_list()
        {
            CoreMap map = NewMap(8, 8);

            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver));
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.ResearchBench));

            Thing wall = Spawn(map, new IntVec3(2, 0, 2), "Wall");
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver));
            Assert.Contains(wall, map.listerThings.ThingsInGroup(ThingRequestGroup.Everything));
        }

        [Fact]
        public void The_groups_are_rebuilt_by_a_Scribe_round_trip_in_the_same_order()
        {
            CoreMap map = NewMap(20, 20);
            Spawn(map, new IntVec3(1, 0, 1), "Wall");
            Spawn(map, new IntVec3(3, 0, 3), "Smithy");
            Spawn(map, new IntVec3(5, 0, 5), "ResearchBench");
            Spawn(map, new IntVec3(7, 0, 7), "TableButcher");
            Spawn(map, new IntVec3(9, 0, 9), "TableTailor");
            List<int> before = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).Select(t => t.thingIDNumber).ToList();
            Assert.Equal(3, before.Count);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(before, loaded.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).Select(t => t.thingIDNumber).ToList());
            Assert.Single(loaded.listerThings.ThingsInGroup(ThingRequestGroup.ResearchBench));
            Assert.Equal(map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count, loaded.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count);
        }
    }
}
