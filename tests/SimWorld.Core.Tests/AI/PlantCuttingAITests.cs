using System.Collections.Generic;
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
    /// <c>PlantsCut</c> wired to a real scanner (system: work — the PlantCutting work type):
    /// <see cref="WorkGiver_PlantsCut"/> finds a plant that is in something's way and
    /// <see cref="JobDriver_PlantWork"/> clears it. RimWorld drives this from designations, which do not
    /// exist in this codebase at all, so what these tests really pin is the replacement predicate — that the
    /// two derived reasons fire, and just as importantly that nothing else does.
    /// </summary>
    public class PlantCuttingAITests : ContentTestBase
    {
        public PlantCuttingAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A citizen skilled enough at Plants that a cut or a sow fits comfortably inside a test's
        /// tick budget; the speed/skill relationship itself is <c>PlantGrowthTests</c>' business, not this
        /// file's.</summary>
        private static Pawn SpawnGrower(CoreMap map, IntVec3 cell, string name = "Grower")
        {
            Pawn p = NewHuman(name);
            p.skills!.GetSkill(SkillDefOf.Plants)!.Level = 20;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Plant SpawnPlant(CoreMap map, IntVec3 cell, string defName, float growth)
        {
            var plant = (Plant)ThingMaker.MakeThing(Def(defName));
            plant.Growth = growth;
            GenSpawn.Spawn(plant, cell, map);
            return plant;
        }

        /// <summary>A growing zone over exactly the given cells, sowing the named crop.</summary>
        private static Zone_Growing NewGrowingZone(CoreMap map, string cropDefName, params IntVec3[] cells)
        {
            var zone = new Zone_Growing { plantDefToGrow = Def(cropDefName) };
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            return zone;
        }

        // ---- content ----

        [Fact]
        public void PlantCutting_content_loads_with_no_errors_and_the_WorkGiverDef_is_wired()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(PlantCuttingJobDefOf.CutPlant);
            Assert.IsType<WorkGiver_PlantsCut>(DefDatabase<WorkGiverDef>.GetNamed("PlantsCut").Worker);
        }

        // ---- the predicate that stands in for a cut designation ----

        [Fact]
        public void A_plant_with_nothing_waiting_on_its_cell_is_not_cutting_work()
        {
            CoreMap map = NewMap(8, 8);
            Plant scrub = SpawnPlant(map, new IntVec3(4, 0, 4), "WildPlant", 1f);

            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(scrub));
        }

        [Fact]
        public void A_zones_own_crop_is_not_cutting_work_but_a_different_plant_in_that_zone_is()
        {
            CoreMap map = NewMap(8, 8);
            var potatoCell = new IntVec3(2, 0, 2);
            var riceCell = new IntVec3(3, 0, 2);
            NewGrowingZone(map, "Plant_Potato", potatoCell, riceCell);

            Plant ownCrop = SpawnPlant(map, potatoCell, "Plant_Potato", 0.3f);
            Plant wrongCrop = SpawnPlant(map, riceCell, "Plant_Rice", 0.3f);

            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(ownCrop));
            Assert.True(WorkGiver_PlantsCut.ShouldBeCut(wrongCrop));
        }

        [Fact]
        public void A_zone_that_is_not_sowing_blocks_nothing_so_its_stray_plants_are_left_alone()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(2, 0, 2);
            Zone_Growing zone = NewGrowingZone(map, "Plant_Potato", cell);
            Plant wrongCrop = SpawnPlant(map, cell, "Plant_Rice", 0.3f);
            Assert.True(WorkGiver_PlantsCut.ShouldBeCut(wrongCrop));

            zone.allowSow = false;
            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(wrongCrop));

            zone.allowSow = true;
            zone.plantDefToGrow = null; // an unset growing zone is not waiting to sow anything either
            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(wrongCrop));
        }

        [Fact]
        public void A_plant_sharing_a_cell_with_a_blueprint_or_a_frame_is_cutting_work()
        {
            CoreMap map = NewMap(8, 8);
            var blueprintCell = new IntVec3(2, 0, 2);
            var frameCell = new IntVec3(5, 0, 5);

            Plant underBlueprint = SpawnPlant(map, blueprintCell, "WildPlant", 1f);
            Plant underFrame = SpawnPlant(map, frameCell, "WildPlant", 1f);
            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(underBlueprint));
            Assert.False(WorkGiver_PlantsCut.ShouldBeCut(underFrame));

            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), blueprintCell, map);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Frame_Wall")), frameCell, map);

            Assert.True(WorkGiver_PlantsCut.ShouldBeCut(underBlueprint));
            Assert.True(WorkGiver_PlantsCut.ShouldBeCut(underFrame));
        }

        // ---- end to end: the real work-giver path ----

        [Fact]
        public void A_citizen_cuts_a_wrong_crop_out_of_a_growing_zone_and_the_grower_can_then_sow_it()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(6, 0, 6);
            // Exactly one cell, so there is nowhere else to sow: the only way the zone ever gets its potato
            // is if the rice standing in the way is cut first. That is the whole point of this giver —
            // WorkGiver_GrowerSow simply refuses an occupied cell and would otherwise stall here forever.
            NewGrowingZone(map, "Plant_Potato", cell);
            Plant rice = SpawnPlant(map, cell, "Plant_Rice", 0.2f);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));

            RunTicks(3000, citizen);

            Assert.True(rice.Destroyed, "The wrong crop should have been cut out of the growing zone.");
            Assert.NotEmpty(map.listerThings.ThingsOfDef(Def("Plant_Potato")));
        }

        [Fact]
        public void A_citizen_clears_a_plant_standing_where_a_blueprint_is_going_up()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(7, 0, 7);
            Plant scrub = SpawnPlant(map, cell, "WildPlant", 1f);
            // No wood anywhere on the map, so the construction givers find nothing to haul and this is the
            // only work available — the blueprint's presence alone is what makes the plant a job.
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), cell, map);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));

            RunTicks(2000, citizen);

            Assert.True(scrub.Destroyed, "A plant standing on a blueprint's cell should have been cleared.");
        }

        [Fact]
        public void Wild_growth_that_is_in_nobodys_way_is_left_standing()
        {
            CoreMap map = NewMap(12, 12);
            Plant scrub = SpawnPlant(map, new IntVec3(7, 0, 7), "WildPlant", 1f);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));

            RunTicks(3000, citizen);

            Assert.False(scrub.Destroyed,
                "Without a designation system there is nobody asking for this; a general cut-everything sweep would strip the map.");
        }

        [Fact]
        public void Two_citizens_do_not_both_claim_the_only_plant_worth_cutting()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(6, 0, 6);
            NewGrowingZone(map, "Plant_Potato", cell);
            Plant rice = SpawnPlant(map, cell, "Plant_Rice", 0.2f);
            Pawn a = SpawnGrower(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnGrower(map, new IntVec3(11, 0, 11), "B");

            RunTicks(120, a, b);

            bool aHasIt = map.reservationManager.IsReservedBy(a, rice);
            bool bHasIt = map.reservationManager.IsReservedBy(b, rice);
            Assert.True(aHasIt ^ bHasIt, "Exactly one of the two should hold the only cuttable plant's reservation.");
        }

        // ---- the yield, through Plant.Harvest rather than a second path ----

        [Fact]
        public void Cutting_a_grown_crop_leaves_its_product_behind()
        {
            CoreMap map = NewMap(8, 8);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));
            Plant rice = SpawnPlant(map, new IntVec3(1, 0, 0), "Plant_Rice", 1f);

            citizen.jobs.StartJob(new Job(PlantCuttingJobDefOf.CutPlant, rice));
            RunTicks(1000, citizen);

            Assert.True(rice.Destroyed);
            IReadOnlyList<Thing> yielded = map.listerThings.ThingsOfDef(Def("RawRice"));
            Assert.Single(yielded);
            Assert.True(yielded[0].stackCount > 0, "A fully grown crop cut down should leave a real stack behind.");
        }

        [Fact]
        public void Cutting_a_half_grown_crop_leaves_less_behind_than_cutting_a_grown_one()
        {
            int YieldFromCutting(float growth)
            {
                CoreMap map = NewMap(8, 8);
                Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));
                Plant rice = SpawnPlant(map, new IntVec3(1, 0, 0), "Plant_Rice", growth);
                citizen.jobs.StartJob(new Job(PlantCuttingJobDefOf.CutPlant, rice));
                RunTicks(1000, citizen);
                IReadOnlyList<Thing> yielded = map.listerThings.ThingsOfDef(Def("RawRice"));
                return yielded.Count == 0 ? 0 : yielded[0].stackCount;
            }

            Assert.True(YieldFromCutting(0.5f) < YieldFromCutting(1f),
                "Cutting goes through Plant.Harvest, which scales the yield by how grown the plant actually was.");
        }

        [Fact]
        public void Cutting_growth_that_yields_nothing_still_clears_the_cell()
        {
            CoreMap map = NewMap(8, 8);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));
            Plant scrub = SpawnPlant(map, new IntVec3(1, 0, 0), "WildPlant", 1f);
            int thingsBefore = map.listerThings.AllThings.Count;

            citizen.jobs.StartJob(new Job(PlantCuttingJobDefOf.CutPlant, scrub));
            RunTicks(1000, citizen);

            Assert.True(scrub.Destroyed);
            Assert.True(map.listerThings.AllThings.Count < thingsBefore,
                "Wild growth has no harvestedThingDef, so cutting it should leave nothing at all behind.");
        }

        [Fact]
        public void Cutting_teaches_the_cutter_the_plants_skill()
        {
            CoreMap map = NewMap(8, 8);
            Pawn citizen = NewHuman("Cutter");
            // Deliberately not SpawnGrower's level 20: skills at 10 and above decay on their own every 200
            // ticks (SkillRecord.Interval), which would swamp one cut's worth of xp. A middling cutter is
            // slower but shows the gain cleanly.
            SkillRecord plants = citizen.skills!.GetSkill(SkillDefOf.Plants)!;
            plants.Level = 5;
            GenSpawn.Spawn(citizen, new IntVec3(0, 0, 0), map);
            float xpBefore = plants.XpTotalEarned;
            Plant rice = SpawnPlant(map, new IntVec3(1, 0, 0), "Plant_Rice", 1f);

            citizen.jobs.StartJob(new Job(PlantCuttingJobDefOf.CutPlant, rice));
            RunTicks(1000, citizen);
            Assert.True(rice.Destroyed, "The cut should have finished inside the tick budget.");

            Assert.True(plants.XpTotalEarned > xpBefore, "Clearing a plant should teach the worker something about plants.");
        }

        // ---- Scribe round trip ----

        [Fact]
        public void A_cut_job_in_progress_round_trips_through_Scribe_and_finishes_after_loading()
        {
            CoreMap map = NewMap(8, 8);
            var cell = new IntVec3(3, 0, 0);
            NewGrowingZone(map, "Plant_Potato", cell);
            Pawn citizen = SpawnGrower(map, new IntVec3(0, 0, 0));
            Plant rice = SpawnPlant(map, cell, "Plant_Rice", 0.4f);

            citizen.jobs.StartJob(new Job(PlantCuttingJobDefOf.CutPlant, rice));
            Assert.NotNull(citizen.jobs.curJob);
            Assert.True(map.reservationManager.IsReservedBy(citizen, rice));

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            var loadedRice = (Plant)loaded.listerThings.ThingsOfDef(rice.def)[0];

            Assert.Equal(PlantCuttingJobDefOf.CutPlant, loadedPawn.jobs.curJob!.def);
            Assert.Same(loadedRice, loadedPawn.jobs.curJob.GetTarget(TargetIndex.A).Thing);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedPawn, loadedRice));
            // The zone it was blocking survived too, so the loaded plant is still genuinely cutting work.
            Assert.True(WorkGiver_PlantsCut.ShouldBeCut(loadedRice));

            RunTicks(1000, loadedPawn);
            Assert.True(loadedRice.Destroyed, "The loaded job should resume through a freshly rebuilt driver.");
        }
    }
}
