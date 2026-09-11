using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
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
    /// <c>Repair</c> wired to a real scanner (system: work — the Construction work type's maintenance half):
    /// <see cref="WorkGiver_Repair"/> finds a damaged artificial building and
    /// <see cref="JobDriver_Repair"/> puts its hit points back one at a time. Same end-to-end shape as
    /// <c>HaulingAITests</c> — the job comes from the giver through the think tree, not from a test calling
    /// the driver — and the tuned numbers are pinned as bands and trends, never as literals (the two tick
    /// counts in <see cref="JobDriver_Repair"/> are RimWorld's shape recalled, not sourced).
    /// </summary>
    public class RepairAITests : ContentTestBase
    {
        public RepairAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Builder")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A spawned building, damaged through the real <see cref="Thing.TakeDamage"/> path rather
        /// than by poking HitPoints — so what the giver reacts to is the same state an explosion or a roof
        /// collapse would actually leave behind.</summary>
        private static Thing SpawnDamaged(CoreMap map, IntVec3 cell, string defName, float damage)
        {
            Thing building = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(building, cell, map);
            building.TakeDamage(new DamageInfo(DamageDefOf.Blunt, damage));
            return building;
        }

        // ---- content ----

        [Fact]
        public void Repair_content_loads_with_no_errors_and_the_WorkGiverDef_is_wired()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(RepairJobDefOf.Repair);
            Assert.IsType<WorkGiver_Repair>(DefDatabase<WorkGiverDef>.GetNamed("Repair").Worker);
        }

        // ---- the predicate that stands in for RimWorld's faction-owned repairable list ----

        [Fact]
        public void An_undamaged_building_is_not_repair_work()
        {
            CoreMap map = NewMap(8, 8);
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, new IntVec3(4, 0, 4), map);

            Assert.Equal(wall.MaxHitPoints, wall.HitPoints);
            Assert.False(WorkGiver_Repair.IsRepairable(wall));
        }

        [Fact]
        public void A_damaged_building_is_repair_work()
        {
            CoreMap map = NewMap(8, 8);
            Thing wall = SpawnDamaged(map, new IntVec3(4, 0, 4), "Wall", 25f);

            Assert.True(wall.HitPoints < wall.MaxHitPoints, "TakeDamage should have taken hit points off the wall.");
            Assert.True(WorkGiver_Repair.IsRepairable(wall));
        }

        [Fact]
        public void Natural_rock_is_never_repair_work_however_battered_it_is()
        {
            CoreMap map = NewMap(8, 8);
            Thing rock = ThingMaker.MakeThing(Def("Sandstone"));
            GenSpawn.Spawn(rock, new IntVec3(4, 0, 4), map);
            rock.HitPoints = 1;

            // The stand-in for RimWorld's building.repairable: mineable rock is not something anyone built,
            // so patching it up is not a job — WorkGiver_Miner's business, not this one's.
            Assert.False(WorkGiver_Repair.IsRepairable(rock));
        }

        [Fact]
        public void A_plant_or_an_item_is_never_repair_work()
        {
            CoreMap map = NewMap(8, 8);
            Thing plant = ThingMaker.MakeThing(Def("WildPlant"));
            GenSpawn.Spawn(plant, new IntVec3(2, 0, 2), map);
            plant.HitPoints = 1;
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 5;
            GenSpawn.Spawn(wood, new IntVec3(3, 0, 3), map);
            wood.HitPoints = 1;

            Assert.False(WorkGiver_Repair.IsRepairable(plant));
            Assert.False(WorkGiver_Repair.IsRepairable(wood));
        }

        // ---- end to end: the real work-giver path ----

        [Fact]
        public void A_citizen_walks_to_a_damaged_wall_and_repairs_it_back_to_whole()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing wall = SpawnDamaged(map, new IntVec3(8, 0, 8), "Wall", 3f);
            int damaged = wall.HitPoints;

            RunTicks(3000, citizen);

            Assert.True(wall.HitPoints > damaged, "Repair work should have put hit points back on.");
            Assert.Equal(wall.MaxHitPoints, wall.HitPoints);
            Assert.False(WorkGiver_Repair.IsRepairable(wall), "A repaired building should stop being repair work.");
        }

        [Fact]
        public void Repairing_never_overshoots_maximum_hit_points()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing door = SpawnDamaged(map, new IntVec3(5, 0, 5), "Door", 1f);

            // Long enough to repair the single lost point several times over; the cap is what stops it.
            RunTicks(4000, citizen);

            Assert.Equal(door.MaxHitPoints, door.HitPoints);
        }

        [Fact]
        public void A_repaired_citizen_learns_construction_from_the_work()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            SkillRecord construction = citizen.skills!.GetSkill(SkillDefOf.Construction)!;
            construction.Level = 5;
            float xpBefore = construction.XpTotalEarned;
            SpawnDamaged(map, new IntVec3(5, 0, 5), "Wall", 4f);

            RunTicks(2000, citizen);

            Assert.True(construction.XpTotalEarned > xpBefore, "Repairing should teach the repairer construction.");
        }

        [Fact]
        public void A_more_skilled_builder_restores_more_hit_points_in_the_same_time()
        {
            int RestoredAfter(int constructionLevel, int ticks)
            {
                CoreMap map = NewMap(6, 6);
                Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
                citizen.skills!.GetSkill(SkillDefOf.Construction)!.Level = constructionLevel;
                // Adjacent to the pawn, so walking time is the same negligible amount in both runs and the
                // difference measured is the repair rate itself.
                Thing wall = SpawnDamaged(map, new IntVec3(1, 0, 0), "Wall", 60f);
                int damaged = wall.HitPoints;
                RunTicks(ticks, citizen);
                return wall.HitPoints - damaged;
            }

            int unskilled = RestoredAfter(0, 1500);
            int skilled = RestoredAfter(20, 1500);

            Assert.True(unskilled > 0, "Even an unskilled builder should make progress in 1500 ticks.");
            Assert.True(skilled > unskilled,
                $"Higher construction skill should repair faster: level 20 restored {skilled}, level 0 restored {unskilled}.");
        }

        [Fact]
        public void Two_citizens_do_not_both_claim_the_only_damaged_building()
        {
            CoreMap map = NewMap(10, 10);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(9, 0, 9), "B");
            Thing wall = SpawnDamaged(map, new IntVec3(5, 0, 5), "Wall", 40f);

            RunTicks(200, a, b);

            bool aHasIt = map.reservationManager.IsReservedBy(a, wall);
            bool bHasIt = map.reservationManager.IsReservedBy(b, wall);
            Assert.True(aHasIt ^ bHasIt, "Exactly one of the two should hold the only damaged building's reservation.");
        }

        [Fact]
        public void Damage_from_an_explosion_becomes_repair_work_a_citizen_picks_up_on_its_own()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, new IntVec3(8, 0, 8), map);
            Assert.Equal(wall.MaxHitPoints, wall.HitPoints);

            // One of the three things in this codebase that can actually damage a building — see
            // WorkGiver_Repair's own doc for the other two.
            SimWorld.Combat.GenExplosion.DoExplosion(
                new IntVec3(8, 0, 7), map, 1.5f, DamageDefOf.Blunt, null, 10f);

            Assert.True(wall.HitPoints < wall.MaxHitPoints, "The blast should have damaged the wall.");

            RunTicks(4000, citizen);

            Assert.Equal(wall.MaxHitPoints, wall.HitPoints);
        }

        // ---- ordering inside the Construction work type ----

        [Fact]
        public void Repair_is_tried_after_every_giver_that_actually_builds_something()
        {
            WorkGiverDef repair = DefDatabase<WorkGiverDef>.GetNamed("Repair");
            foreach (string builder in new[]
            {
                "ConstructFinishFrames",
                "ConstructDeliverResourcesToFrames",
                "ConstructDeliverResourcesToBlueprints",
            })
            {
                WorkGiverDef other = DefDatabase<WorkGiverDef>.GetNamed(builder);
                Assert.Same(repair.workType, other.workType);
                Assert.True(repair.priorityInType < other.priorityInType,
                    $"Repair must sit below {builder} in the Construction work type — see this def's own comment: " +
                    "without a faction to scan by, repair work also covers every weathered ruin wall on a generated " +
                    "map, and above the builders it starves construction outright.");
            }
        }

        [Fact]
        public void A_builder_finishes_the_frame_it_could_be_working_on_before_patching_up_a_damaged_wall()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            citizen.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20;

            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Bed"));
            frame.AddMaterial(Def("WoodLog"), 100); // fully materialed: nothing left but the work itself
            GenSpawn.Spawn(frame, new IntVec3(2, 0, 0), map);
            SpawnDamaged(map, new IntVec3(1, 0, 0), "Wall", 80f);

            RunTicks(600, citizen);

            Assert.NotEmpty(map.listerThings.ThingsOfDef(Def("Bed")));
        }

        // ---- Scribe round trip ----

        [Fact]
        public void A_repair_job_in_progress_round_trips_through_Scribe_and_finishes_after_loading()
        {
            CoreMap map = NewMap(8, 8);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing wall = SpawnDamaged(map, new IntVec3(3, 0, 0), "Wall", 2f);

            citizen.jobs.StartJob(new Job(RepairJobDefOf.Repair, wall));
            Assert.NotNull(citizen.jobs.curJob);
            Assert.True(map.reservationManager.IsReservedBy(citizen, wall));

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            Thing loadedWall = loaded.listerThings.ThingsOfDef(wall.def)[0];

            Assert.Equal(RepairJobDefOf.Repair, loadedPawn.jobs.curJob!.def);
            Assert.Same(loadedWall, loadedPawn.jobs.curJob.GetTarget(TargetIndex.A).Thing);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedPawn, loadedWall));
            Assert.True(loadedWall.HitPoints < loadedWall.MaxHitPoints, "The damage itself should have survived the round trip.");

            // The loaded job resumes through a freshly rebuilt driver rather than sitting inert.
            RunTicks(3000, loadedPawn);
            Assert.Equal(loadedWall.MaxHitPoints, loadedWall.HitPoints);
        }
    }
}
