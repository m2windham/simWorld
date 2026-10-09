using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// <see cref="WorkGiver_BuildRoof"/> and <see cref="WorkGiver_RemoveRoof"/> skip outright while their
    /// <see cref="Area"/> is empty. RimWorld's roof givers reach the same end inside
    /// <c>BoolGrid.ActiveCells</c>, which yields nothing at a true count of zero
    /// (<c>josh-m/rw-decompile/Verse/BoolGrid.cs</c>); skipping also spares the scan its iterator. A skip is a
    /// claim that the scan would have found nothing, so these hold it to that claim.
    /// </summary>
    public class RoofShouldSkipTests : ContentTestBase
    {
        public RoofShouldSkipTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX = 20, int sizeZ = 20) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static WorkGiver_BuildRoof BuildGiver => (WorkGiver_BuildRoof)DefDatabase<WorkGiverDef>.GetNamed("BuildRoof").Worker;

        private static WorkGiver_RemoveRoof RemoveGiver => (WorkGiver_RemoveRoof)DefDatabase<WorkGiverDef>.GetNamed("RemoveRoof").Worker;

        private static Pawn Spawn(CoreMap map)
        {
            Pawn p = NewHuman("Roofer");
            GenSpawn.Spawn(p, new IntVec3(1, 0, 1), map);
            return p;
        }

        [Fact]
        public void The_build_roof_giver_skips_while_the_build_roof_area_is_empty_and_stops_skipping_once_a_cell_is_marked()
        {
            CoreMap map = NewMap();
            Pawn pawn = Spawn(map);

            Assert.True(BuildGiver.ShouldSkip(pawn));

            map.areaManager.BuildRoof[new IntVec3(5, 0, 5)] = true;
            Assert.False(BuildGiver.ShouldSkip(pawn));

            map.areaManager.BuildRoof[new IntVec3(5, 0, 5)] = false;
            Assert.True(BuildGiver.ShouldSkip(pawn));
        }

        [Fact]
        public void The_remove_roof_giver_skips_while_the_no_roof_area_is_empty_and_stops_skipping_once_a_cell_is_marked()
        {
            CoreMap map = NewMap();
            Pawn pawn = Spawn(map);

            Assert.True(RemoveGiver.ShouldSkip(pawn));

            map.areaManager.NoRoof[new IntVec3(5, 0, 5)] = true;
            Assert.False(RemoveGiver.ShouldSkip(pawn));
        }

        [Fact]
        public void Each_roof_giver_watches_only_its_own_area()
        {
            CoreMap map = NewMap();
            Pawn pawn = Spawn(map);

            map.areaManager.NoRoof[new IntVec3(5, 0, 5)] = true;
            map.areaManager.Home[new IntVec3(6, 0, 6)] = true;
            Assert.True(BuildGiver.ShouldSkip(pawn), "a no-roof cell is not roof to build");

            map.areaManager.NoRoof[new IntVec3(5, 0, 5)] = false;
            map.areaManager.BuildRoof[new IntVec3(7, 0, 7)] = true;
            Assert.True(RemoveGiver.ShouldSkip(pawn), "a build-roof cell is not roof to remove");
        }

        [Fact]
        public void A_roof_giver_that_skips_has_no_cell_the_scan_would_have_looked_at()
        {
            CoreMap map = NewMap();
            Pawn pawn = Spawn(map);

            Assert.True(BuildGiver.ShouldSkip(pawn));
            Assert.Empty(BuildGiver.PotentialWorkCellsGlobal(pawn));
            Assert.Null(WorkGiverScanUtility.TryFindJobOnScanner(pawn, BuildGiver));

            Assert.True(RemoveGiver.ShouldSkip(pawn));
            Assert.Empty(RemoveGiver.PotentialWorkCellsGlobal(pawn));
            Assert.Null(WorkGiverScanUtility.TryFindJobOnScanner(pawn, RemoveGiver));
        }

        [Fact]
        public void With_cells_marked_the_priority_scan_is_not_skipped_and_hands_a_builder_the_roof_job()
        {
            // A walled room in the home area: AutoBuildRoofAreaSetter queues its cells, as the roof tests do.
            CoreMap map = NewMap();
            var interior = new CellRect(8, 8, 2, 2);
            foreach (IntVec3 c in interior.ExpandedBy(1).EdgeCells)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(c == new IntVec3(8, 0, 7) ? "Door" : "Wall")), c, map);
            }
            foreach (IntVec3 c in interior.ExpandedBy(1).Cells) map.areaManager.Home[c] = true;
            map.MapTick();
            Pawn builder = NewHuman("Builder");
            GenSpawn.Spawn(builder, new IntVec3(8, 0, 8), map);

            Assert.True(map.areaManager.BuildRoof.TrueCount > 0, "test setup: a roof should have been queued");
            Assert.False(BuildGiver.ShouldSkip(builder));

            Job? job = WorkGiverScanUtility.TryGiveJobInGivers(builder, new[] { DefDatabase<WorkGiverDef>.GetNamed("BuildRoof") });

            Assert.NotNull(job);
            Assert.Same(RoofJobDefOf.BuildRoof, job!.def);
        }

        [Fact]
        public void Skipping_never_changes_which_job_a_think_would_have_found_across_random_area_states()
        {
            // For random states of both areas: a giver that ShouldSkip would have found no job, so skipping it
            // cannot have hidden one. The converse (not skipping and finding nothing) is allowed.
            var rng = new RandomStream(8811);
            for (int trial = 0; trial < 60; trial++)
            {
                CoreMap map = NewMap(24, 24);
                Pawn pawn = Spawn(map);
                for (int i = 0; i < rng.Range(0, 6); i++) map.areaManager.BuildRoof[new IntVec3(rng.Range(0, 24), 0, rng.Range(0, 24))] = true;
                for (int i = 0; i < rng.Range(0, 6); i++) map.areaManager.NoRoof[new IntVec3(rng.Range(0, 24), 0, rng.Range(0, 24))] = true;

                if (BuildGiver.ShouldSkip(pawn)) Assert.Null(WorkGiverScanUtility.TryFindJobOnScanner(pawn, BuildGiver));
                if (RemoveGiver.ShouldSkip(pawn)) Assert.Null(WorkGiverScanUtility.TryFindJobOnScanner(pawn, RemoveGiver));
            }
        }
    }
}
