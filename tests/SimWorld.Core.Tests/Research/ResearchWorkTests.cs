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

// The production namespace SimWorld.Research shares its leaf segment with this test namespace
// (SimWorld.Tests.Research); alias every production type explicitly, matching ResearchTests.cs's own
// convention (and CLAUDE.md's "Inside SimWorld.Tests.X, a reference to SimWorld.X can resolve to the test
// namespace — qualify with global:: when it bites").
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using ResearchTabDefOf = global::SimWorld.Research.ResearchTabDefOf;
using ResearchProjectDefOf = global::SimWorld.Research.ResearchProjectDefOf;
using ResearchWorkDefOf = global::SimWorld.Research.ResearchWorkDefOf;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// The Research work type end to end (docs/WORK-REGISTER.md's "eighteen work types have no worker",
    /// this lane's own slice): a pawn walking to a <see cref="ResearchWorkDefOf.ResearchBench"/> and
    /// advancing <see cref="ResearchManager.CurrentProj"/> through the real <see cref="WorkGiver_Research"/> /
    /// <see cref="JobDriver_Research"/> path, not by calling the driver directly.
    /// </summary>
    public class ResearchWorkTests : ContentTestBase
    {
        public ResearchWorkTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Researcher")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnBench(CoreMap map, IntVec3 cell) =>
            SpawnBuilding(map, cell, "ResearchBench");

        private static Thing SpawnBuilding(CoreMap map, IntVec3 cell, string defName)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static WorkGiverDef ResearchGiverDef => DefDatabase<WorkGiverDef>.GetNamed("Research");

        // ---- content ----

        [Fact]
        public void Content_loads_with_no_errors_and_the_new_DefOfs_bind()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ResearchWorkDefOf.Research);
            Assert.Equal("Research", ResearchWorkDefOf.Research.defName);
            Assert.NotNull(ResearchWorkDefOf.ResearchBench);
            Assert.Equal("ResearchBench", ResearchWorkDefOf.ResearchBench.defName);
        }

        [Fact]
        public void Research_WorkGiverDef_is_wired_to_a_real_scanner()
        {
            Assert.IsType<WorkGiver_Research>(ResearchGiverDef.Worker);
        }

        [Fact]
        public void ResearchBench_has_a_hand_authored_blueprint_and_frame()
        {
            Assert.NotNull(GenConstruct.BlueprintDefFor(ResearchWorkDefOf.ResearchBench));
            Assert.NotNull(GenConstruct.FrameDefFor(ResearchWorkDefOf.ResearchBench));
        }

        // ---- buildable through the existing construction pipeline ----

        [Fact]
        public void ResearchBench_is_buildable_through_blueprint_frame_and_finish()
        {
            CoreMap map = NewMap(6, 6);
            Thing blueprintThing = ThingMaker.MakeThing(Def("Blueprint_ResearchBench"));
            GenSpawn.Spawn(blueprintThing, new IntVec3(2, 0, 2), map);
            var blueprint = (Blueprint)blueprintThing;

            Frame frame = blueprint.ReplaceWithFrame();
            Assert.False(frame.MaterialsFullySatisfied());

            frame.AddMaterial(Def("WoodLog"), 10);
            Assert.True(frame.MaterialsFullySatisfied());

            Thing built = frame.CompleteConstruction(NewHuman());

            Assert.Equal(ResearchWorkDefOf.ResearchBench, built.def);
            Assert.True(built.Spawned);
        }

        // ---- no bench / no project: no job ----

        [Fact]
        public void No_current_project_produces_no_research_job_even_with_a_reachable_bench()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnBench(map, new IntVec3(2, 0, 0));
            Find.ResearchManager.CurrentProj = null;

            Job? job = WorkGiverScanUtility.TryGiveJobInGivers(pawn, new List<WorkGiverDef> { ResearchGiverDef });

            Assert.Null(job);
        }

        [Fact]
        public void No_bench_on_the_map_produces_no_research_job_even_with_a_current_project()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Find.ResearchManager.CurrentProj = ResearchProjectDefOf.Agriculture;

            Job? job = WorkGiverScanUtility.TryGiveJobInGivers(pawn, new List<WorkGiverDef> { ResearchGiverDef });

            Assert.Null(job);
        }

        [Fact]
        public void A_bench_and_a_current_project_together_produce_a_real_research_job()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing bench = SpawnBench(map, new IntVec3(2, 0, 0));
            Find.ResearchManager.CurrentProj = ResearchProjectDefOf.Agriculture;

            Job? job = WorkGiverScanUtility.TryGiveJobInGivers(pawn, new List<WorkGiverDef> { ResearchGiverDef });

            Assert.NotNull(job);
            Assert.Equal(ResearchWorkDefOf.Research, job!.def);
            Assert.Same(bench, job.GetTarget(TargetIndex.A).Thing);
        }

        // ---- end to end: the point of the whole module ----

        [Fact]
        public void Idle_pawn_with_a_bench_and_a_project_advances_it_through_the_real_work_giver_path()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnBench(map, new IntVec3(4, 0, 0));
            Find.ResearchManager.CurrentProj = ResearchProjectDefOf.Agriculture;

            Assert.Equal(0f, Find.ResearchManager.GetProgress(ResearchProjectDefOf.Agriculture));

            // Nothing calls WorkGiver_Research or JobDriver_Research directly: an idle pawn's own think
            // tree (JobGiver_Work, via WorkGiverScanUtility) is what has to find the bench and start the job.
            RunTicks(3000, pawn);

            Assert.True(Find.ResearchManager.GetProgress(ResearchProjectDefOf.Agriculture) > 0f,
                "An idle pawn with a reachable bench and a current project should have advanced it by now.");
            Assert.False(Find.ResearchManager.IsFinished(ResearchProjectDefOf.Agriculture),
                "Agriculture's baseCost should comfortably outlast 3000 ticks from one researcher.");
        }

        [Fact]
        public void A_higher_Intellectual_skill_advances_research_faster()
        {
            ResearchProjectDef project = ResearchProjectDefOf.Agriculture; // baseCost 250 — outlasts both phases below.
            const int ticks = 2000;

            // Phase 1: a level-0 researcher on her own map, tick manager and research manager.
            Find.TickManager = new TickManager();
            var lowManager = new ResearchManager { CurrentProj = project };
            Find.ResearchManager = lowManager;
            CoreMap lowMap = NewMap(8, 8);
            Pawn lowSkill = SpawnHuman(lowMap, new IntVec3(0, 0, 0), "Novice");
            SpawnBench(lowMap, new IntVec3(4, 0, 0));
            Assert.Equal(0, lowSkill.skills.GetSkill(SkillDefOf.Intellectual)!.Level);

            RunTicks(ticks, lowSkill);
            float lowProgress = lowManager.GetProgress(project);

            // Phase 2: a level-20 researcher, on a fresh tick manager so the level-0 pawn above (still
            // registered on its own, now-abandoned TickManager instance) never ticks again and never
            // contributes to this phase's manager.
            Find.TickManager = new TickManager();
            var highManager = new ResearchManager { CurrentProj = project };
            Find.ResearchManager = highManager;
            CoreMap highMap = NewMap(8, 8);
            Pawn highSkill = SpawnHuman(highMap, new IntVec3(0, 0, 0), "Expert");
            SpawnBench(highMap, new IntVec3(4, 0, 0));
            highSkill.skills.GetSkill(SkillDefOf.Intellectual)!.levelInt = 20;

            RunTicks(ticks, highSkill);
            float highProgress = highManager.GetProgress(project);

            Assert.True(lowProgress > 0f, "The level-0 researcher should still have made some progress.");
            Assert.True(highProgress > lowProgress,
                $"A level-20 Intellectual researcher ({highProgress}) should out-research a level-0 one ({lowProgress}).");
        }

        [Fact]
        public void The_project_finishing_mid_job_ends_the_job_successfully_rather_than_getting_stuck()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnBench(map, new IntVec3(4, 0, 0));

            // Not RimWorld-sourced (a hand-built test fixture, not content) — small enough that a single
            // tick of research from even a level-0 pawn (~0.0033 points/tick) finishes it outright, so the
            // "current project completes mid-toil" branch in JobDriver_Research is exercised for real.
            var tinyProject = new ResearchProjectDef { defName = "TestTinyProject", baseCost = 0.001f, tab = ResearchTabDefOf.Main };
            Find.ResearchManager.CurrentProj = tinyProject;

            RunTicks(3000, pawn);

            Assert.True(Find.ResearchManager.IsFinished(tinyProject));
            Assert.Null(Find.ResearchManager.CurrentProj);
            // The pawn is not left standing at the bench forever with nothing current: its job tracker moved
            // on (to idle wandering, since nothing else is on this map) rather than repeating Research.
            Assert.NotEqual(ResearchWorkDefOf.Research, pawn.jobs.curJob?.def);
        }

        // ---- Scribe round trip, mid-research ----

        [Fact]
        public void A_research_job_in_progress_and_its_managers_progress_round_trip_through_Scribe()
        {
            ResearchProjectDef project = ResearchProjectDefOf.Agriculture;
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0), "Researcher");
            Thing bench = SpawnBench(map, new IntVec3(4, 0, 0));
            Find.ResearchManager.CurrentProj = project;

            pawn.jobs.StartJob(new Job(ResearchWorkDefOf.Research, bench));
            RunTicks(600, pawn);

            float progressBefore = Find.ResearchManager.GetProgress(project);
            Assert.True(progressBefore > 0f);
            Assert.Equal(ResearchWorkDefOf.Research, pawn.jobs.curJob?.def);

            string mapXml = Scribe.SaveToString(map, "map");
            string researchXml = Scribe.SaveToString(Find.ResearchManager, "research");

            Pawn.ResetThingIdCounter();
            CoreMap loadedMap = Scribe.Load<CoreMap>(mapXml, "map", out IReadOnlyList<string> mapErrors);
            ResearchManager loadedManager = Scribe.Load<ResearchManager>(researchXml, "research", out IReadOnlyList<string> researchErrors);

            Assert.Empty(mapErrors);
            Assert.Empty(researchErrors);

            Find.ResearchManager = loadedManager;
            Assert.Equal(project, loadedManager.CurrentProj);
            Assert.Equal(progressBefore, loadedManager.GetProgress(project));

            Pawn loadedPawn = (Pawn)loadedMap.mapPawns.AllPawns[0];
            Thing loadedBench = loadedMap.listerThings.ThingsOfDef(bench.def)[0];

            Assert.NotNull(loadedPawn.jobs.curJob);
            Assert.Equal(ResearchWorkDefOf.Research, loadedPawn.jobs.curJob!.def);
            Assert.Same(loadedBench, loadedPawn.jobs.curJob.GetTarget(TargetIndex.A).Thing);

            // The loaded job resumes (a freshly rebuilt driver) and keeps feeding the loaded manager rather
            // than sitting inert, same as a never-saved job would.
            RunTicks(600, loadedPawn);
            Assert.True(loadedManager.GetProgress(project) > progressBefore);
        }
    }
}
