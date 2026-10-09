using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// What the bench-shaped work givers scan. <see cref="global::SimWorld.Crafting.WorkGiver_DoBill"/> walked every
    /// building on the map to find the few that hold a bill stack — on a settlement cut into a mountain that
    /// is thirteen thousand rock cells, for every idle citizen's every think — and
    /// <see cref="WorkGiver_Research"/> and <see cref="WorkGiver_ButcherCorpse"/> did the same to find one kind
    /// of bench. RimWorld asks <c>ThingRequestGroup.PotentialBillGiver</c> and <c>ResearchBench</c> instead
    /// (<c>WorkGiver_DoBill.PotentialWorkThingRequest</c>, <c>WorkGiver_Researcher.PotentialWorkThingRequest</c>).
    /// Making that change must offer the same Things in the same order: a scan keeps the first of several
    /// equally near candidates.
    /// </summary>
    public class BillGiverScanTests : ContentTestBase
    {
        public BillGiverScanTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static global::SimWorld.Crafting.WorkGiver_DoBill DoBillGiver(string workGiverDefName) =>
            (global::SimWorld.Crafting.WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed(workGiverDefName).Worker;

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell)
        {
            Pawn p = NewHuman();
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A mountain's worth of rock and wall with benches of every kind scattered through it.</summary>
        private static List<Thing> MountainWithBenches(CoreMap map)
        {
            var benches = new List<Thing>();
            string[] benchDefs = { "Smithy", "TableTailor", "TableStonecutter", "ResearchBench", "TableButcher" };
            int b = 0;
            for (int x = 0; x < 40; x++)
            {
                for (int z = 0; z < 6; z++)
                {
                    var cell = new IntVec3(x, 0, z + 2);
                    if (x % 9 == 4 && z == 2) benches.Add(Spawn(map, cell, benchDefs[b++ % benchDefs.Length]));
                    else Spawn(map, cell, (x + z) % 3 == 0 ? "Wall" : "Limestone");
                }
            }
            return benches;
        }

        // ---- WorkGiver_DoBill ----

        [Fact]
        public void The_bill_scan_offers_the_benches_that_hold_a_bill_stack_in_the_order_the_old_walk_met_them()
        {
            CoreMap map = NewMap(60, 12);
            MountainWithBenches(map);
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 0));
            var giver = DoBillGiver("DoBillsSmith");

            // The old scan: every building, kept when it carries a CompBillGiver.
            List<Thing> expected = map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .Where(t => global::SimWorld.Crafting.WorkGiver_DoBill.BillGiverFor(t) != null)
                .ToList();
            List<Thing> offered = giver.PotentialWorkThingsGlobal(pawn).ToList();

            Assert.NotEmpty(expected);
            Assert.True(expected.Count < map.listerThings.ThingsInGroup(ThingRequestGroup.Building).Count / 10);
            Assert.Equal(expected, offered);
        }

        [Fact]
        public void Every_bill_giving_work_giver_scans_the_same_short_list()
        {
            CoreMap map = NewMap(60, 12);
            MountainWithBenches(map);
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 0));

            List<Thing> smith = DoBillGiver("DoBillsSmith").PotentialWorkThingsGlobal(pawn).ToList();
            List<Thing> tailor = DoBillGiver("DoBillsTailor").PotentialWorkThingsGlobal(pawn).ToList();
            List<Thing> cook = DoBillGiver("CookMeals").PotentialWorkThingsGlobal(pawn).ToList();

            Assert.Equal(smith, tailor);
            Assert.Equal(smith, cook);
        }

        /// <summary>
        /// The one test that tells "asks the potential-bill-giver group" from "walks every building and keeps
        /// the benches": the answers are the same by design, so it builds a building the group does not hold
        /// whose instance nevertheless carries the comp — a state no real game reaches, which is what lets it
        /// see which list the scan reads. Without this a scan that went back to walking the mountain would
        /// pass every other test here and cost thirteen thousand checks per think again.
        /// </summary>
        [Fact]
        public void The_bill_scan_reads_the_potential_bill_giver_group_and_not_every_building()
        {
            CoreMap map = NewMap(20, 20);
            Thing realBench = Spawn(map, new IntVec3(3, 0, 3), "Smithy");

            // A def that, when it spawns, declares no bill stack — so the lister keeps it out of the group —
            // and whose instance is then given the comp afterwards.
            var decoyDef = new ThingDef { defName = "DecoyBenchNotInTheGroup", category = ThingCategory.Building, thingClass = typeof(global::SimWorld.Building.Building) };
            Thing decoy = ThingMaker.MakeThing(decoyDef);
            GenSpawn.Spawn(decoy, new IntVec3(5, 0, 5), map);
            decoyDef.comps = new List<CompProperties> { new CompProperties_BillGiver { workType = WorkTypeDefOf.Smithing } };
            ((ThingWithComps)decoy).InitializeComps();

            Assert.NotNull(global::SimWorld.Crafting.WorkGiver_DoBill.BillGiverFor(decoy)); // the old walk would have offered it
            Assert.Contains(decoy, map.listerThings.ThingsInGroup(ThingRequestGroup.Building));
            Assert.DoesNotContain(decoy, map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver));

            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 1));
            List<Thing> offered = DoBillGiver("DoBillsSmith").PotentialWorkThingsGlobal(pawn).ToList();

            Assert.Equal(new[] { realBench }, offered);
        }

        [Fact]
        public void The_bill_scan_follows_a_bench_out_of_the_group_when_it_is_deconstructed()
        {
            CoreMap map = NewMap(20, 20);
            Thing first = Spawn(map, new IntVec3(3, 0, 3), "Smithy");
            Thing second = Spawn(map, new IntVec3(6, 0, 6), "TableTailor");
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 1));
            var giver = DoBillGiver("DoBillsSmith");

            Assert.Equal(new[] { first, second }, giver.PotentialWorkThingsGlobal(pawn).ToArray());

            first.DeSpawn();

            Assert.Equal(new[] { second }, giver.PotentialWorkThingsGlobal(pawn).ToArray());
        }

        // ---- WorkGiver_Research ----

        [Fact]
        public void The_research_scan_offers_the_research_benches_in_building_order_and_nothing_else()
        {
            CoreMap map = NewMap(60, 12);
            List<Thing> benches = MountainWithBenches(map);
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 0));
            var giver = (WorkGiver_Research)DefDatabase<WorkGiverDef>.GetNamed("Research").Worker;

            // The old scan: every building, kept when its def is the research bench.
            List<Thing> expected = map.listerThings.ThingsInGroup(ThingRequestGroup.Building)
                .Where(t => t.def == ResearchWorkDefOf.ResearchBench)
                .ToList();

            Assert.NotEmpty(expected);
            Assert.All(expected, e => Assert.Contains(e, benches));
            Assert.Equal(expected, giver.PotentialWorkThingsGlobal(pawn).ToList());
        }

        [Fact]
        public void The_research_scan_has_nothing_to_offer_on_a_map_without_a_bench()
        {
            CoreMap map = NewMap(20, 20);
            Spawn(map, new IntVec3(3, 0, 3), "Wall");
            Spawn(map, new IntVec3(5, 0, 5), "Smithy");
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 1));
            var giver = (WorkGiver_Research)DefDatabase<WorkGiverDef>.GetNamed("Research").Worker;

            Assert.Empty(giver.PotentialWorkThingsGlobal(pawn));
        }

        // ---- WorkGiver_ButcherCorpse ----

        [Fact]
        public void A_butcher_bench_is_found_among_the_potential_bill_givers_and_a_map_of_rock_has_none()
        {
            CoreMap map = NewMap(60, 12);
            for (int x = 0; x < 40; x++) Spawn(map, new IntVec3(x, 0, 3), x % 2 == 0 ? "Wall" : "Limestone");
            Spawn(map, new IntVec3(4, 0, 7), "Smithy");

            Assert.False(WorkGiver_ButcherCorpse.AnyButcherBenchOn(map), "a smithy is a bench but not a butcher's");

            Thing table = Spawn(map, new IntVec3(9, 0, 7), "TableButcher");

            Assert.True(WorkGiver_ButcherCorpse.AnyButcherBenchOn(map));
            Pawn pawn = SpawnHuman(map, new IntVec3(1, 0, 6));
            Assert.True(WorkGiver_ButcherCorpse.TryFindButcherBench(pawn, out Thing? found));
            Assert.Same(table, found);
        }

        [Fact]
        public void The_nearest_of_several_butcher_tables_is_chosen_and_ties_go_to_the_first_spawned()
        {
            CoreMap map = NewMap(30, 12);
            Thing far = Spawn(map, new IntVec3(20, 0, 6), "TableButcher");
            Thing nearA = Spawn(map, new IntVec3(5, 0, 6), "TableButcher");
            Thing nearB = Spawn(map, new IntVec3(5, 0, 8), "TableButcher"); // exactly as far from the pawn as nearA
            Pawn pawn = SpawnHuman(map, new IntVec3(3, 0, 7));

            Assert.True(WorkGiver_ButcherCorpse.TryFindButcherBench(pawn, out Thing? found));

            Assert.Same(nearA, found);
            Assert.NotSame(far, found);
            Assert.NotSame(nearB, found);
        }
    }
}
