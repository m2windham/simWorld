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

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <c>HaulGeneral</c> wired to a real scanner (system 9: AI — hauling): <see cref="WorkGiver_Haul"/>
    /// finds a not-yet-stored haulable item and <see cref="JobDriver_HaulToCell"/> carries it to a
    /// <see cref="Zone_Stockpile"/> cell, exactly the shape the other wired givers already use
    /// (reservations via <see cref="AI.Toils_Reserve"/>, the job created by the giver itself rather than a
    /// test calling the driver directly).
    /// </summary>
    public class HaulingAITests : ContentTestBase
    {
        public HaulingAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Hauler")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnItem(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            thing.stackCount = count;
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        /// <summary>A stockpile zone allowing exactly one def, over the given cells.</summary>
        private static Zone_Stockpile NewStockpile(CoreMap map, string allowedDefName, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetAllow(Def(allowedDefName), true);
            return zone;
        }

        // ---- content ----

        [Fact]
        public void Hauling_content_loads_with_no_errors_and_HaulGeneral_is_wired()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(JobDefOf.HaulToCell);
            Assert.IsType<WorkGiver_Haul>(DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker);
        }

        // ---- end to end: the real work-giver path, not the driver called directly ----

        [Fact]
        public void A_citizen_hauls_a_loose_item_into_the_only_stockpile()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnItem(map, new IntVec3(9, 0, 9), "WoodLog", 10);
            Zone_Stockpile stockpile = NewStockpile(map, "WoodLog", new IntVec3(1, 0, 1));

            RunTicks(3000, citizen);

            var piles = map.listerThings.ThingsOfDef(Def("WoodLog"));
            Assert.Single(piles);
            Assert.Equal(stockpile.Cells[0], piles[0].Position);
            Assert.Equal(10, piles[0].stackCount);
            Assert.True(HaulAIUtility.IsInValidStorage(piles[0]), "The delivered stack should now read as validly stored.");
        }

        [Fact]
        public void Hauling_onto_an_existing_stack_merges_the_count()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            var stockpileCell = new IntVec3(1, 0, 1);
            NewStockpile(map, "WoodLog", stockpileCell);
            Thing existingStack = SpawnItem(map, stockpileCell, "WoodLog", 20);
            Thing loose = SpawnItem(map, new IntVec3(9, 0, 9), "WoodLog", 5);

            RunTicks(3000, citizen);

            Assert.True(loose.Destroyed, "The loose stack should have been consumed by the merge, not left behind.");
            Assert.False(existingStack.Destroyed);
            Assert.Equal(25, existingStack.stackCount);
            Assert.Single(map.listerThings.ThingsOfDef(Def("WoodLog")));
        }

        [Fact]
        public void Two_haulers_do_not_both_claim_the_only_loose_item()
        {
            CoreMap map = NewMap(10, 10);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(9, 0, 9), "B");
            NewStockpile(map, "WoodLog", new IntVec3(5, 0, 0));
            Thing wood = SpawnItem(map, new IntVec3(5, 0, 5), "WoodLog", 10);

            a.jobs.TryFindAndStartJob();
            b.jobs.TryFindAndStartJob();

            bool aHasIt = a.jobs.curJob != null && a.jobs.curJob.def == JobDefOf.HaulToCell
                && a.jobs.curJob.GetTarget(TargetIndex.A).Thing == wood;
            bool bHasIt = b.jobs.curJob != null && b.jobs.curJob.def == JobDefOf.HaulToCell
                && b.jobs.curJob.GetTarget(TargetIndex.A).Thing == wood;
            Assert.True(aHasIt ^ bHasIt, "Exactly one pawn should have claimed the only loose item.");

            // The real work-giver path itself refuses the second pawn a job on an already-reserved thing —
            // not just an accident of which pawn's think tree ran first.
            var giver = (WorkGiver_Haul)DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker;
            Pawn loser = aHasIt ? b : a;
            Assert.False(giver.HasJobOnThing(loser, wood), "A thing already reserved by the other hauler should not offer a second job on it.");
        }

        // ---- the honest boundary: nowhere valid means no job, not an invented dumping ground ----

        [Fact]
        public void No_stockpile_anywhere_means_the_giver_yields_no_job()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing wood = SpawnItem(map, new IntVec3(4, 0, 4), "WoodLog", 10);

            var giver = (WorkGiver_Haul)DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker;
            Assert.False(giver.HasJobOnThing(pawn, wood));
            Assert.Null(giver.JobOnThing(pawn, wood));

            pawn.jobs.TryFindAndStartJob();
            Assert.NotEqual(JobDefOf.HaulToCell, pawn.jobs.curJob?.def);
        }

        [Fact]
        public void A_stockpile_that_does_not_allow_the_item_is_not_a_valid_destination()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing wood = SpawnItem(map, new IntVec3(4, 0, 4), "WoodLog", 10);
            NewStockpile(map, "Steel", new IntVec3(1, 0, 1)); // allows Steel only, not WoodLog

            var giver = (WorkGiver_Haul)DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker;
            Assert.False(giver.HasJobOnThing(pawn, wood));
        }

        [Fact]
        public void An_item_already_in_a_valid_stockpile_is_not_re_hauled()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            var cell = new IntVec3(3, 0, 3);
            NewStockpile(map, "WoodLog", cell);
            Thing wood = SpawnItem(map, cell, "WoodLog", 10);

            var giver = (WorkGiver_Haul)DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker;
            Assert.False(giver.HasJobOnThing(pawn, wood), "Already-stored stock is not this giver's job.");
        }

        // ---- Scribe round trip, mid-haul ----

        [Fact]
        public void A_pawn_mid_haul_round_trips_through_Scribe_and_finishes_after_loading()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Zone_Stockpile stockpile = NewStockpile(map, "WoodLog", new IntVec3(5, 0, 5));
            Thing wood = SpawnItem(map, new IntVec3(3, 0, 3), "WoodLog", 8);

            pawn.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.HaulToCell, pawn.jobs.curJob?.def);
            Assert.True(map.reservationManager.IsReservedBy(pawn, wood));
            Assert.True(map.reservationManager.IsReservedBy(pawn, stockpile.Cells[0]));

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            Thing loadedWood = loaded.listerThings.ThingsOfDef(Def("WoodLog"))[0];

            Assert.NotNull(loadedPawn.jobs.curJob);
            Assert.Equal(JobDefOf.HaulToCell, loadedPawn.jobs.curJob!.def);
            Assert.Same(loadedWood, loadedPawn.jobs.curJob.GetTarget(TargetIndex.A).Thing);
            Assert.Equal(stockpile.Cells[0], loadedPawn.jobs.curJob.GetTarget(TargetIndex.B).Cell);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedPawn, loadedWood));
            Assert.True(loaded.reservationManager.IsReservedBy(loadedPawn, stockpile.Cells[0]));

            // The loaded job resumes through a freshly rebuilt driver rather than sitting inert — same
            // guarantee AITests' own Mine round-trip pins for a Thing-only job.
            RunTicks(3000, loadedPawn);
            Assert.Equal(8, loaded.listerThings.ThingsOfDef(Def("WoodLog")).Sum(t => t.stackCount));
            Assert.Equal(stockpile.Cells[0], loaded.listerThings.ThingsOfDef(Def("WoodLog")).First().Position);
        }
    }
}
