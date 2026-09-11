using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Needs;
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
    /// The warden work givers and job drivers that close the capture loop (system 12: Factions — capture):
    /// a colonist finding a prisoner through the ordinary work-giver scan and actually doing the recruit
    /// interaction or the feeding <c>Pawn_GuestTracker.cs</c>'s <c>WardenUtility</c> already implements.
    /// </summary>
    public class WardenAITests : ContentTestBase
    {
        public WardenAITests(CoreContentFixture content) : base(content)
        {
            // FactionManager is thread-static like TickManager/Storyteller but ContentTestBase (Sim/**, out
            // of this lane's reach) does not reset it — see CaptureTests' own identical constructor comment.
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Faction NewFaction(string name)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static (Faction captor, Faction hostileOther) HostileFactions()
        {
            Faction captor = NewFaction("Captors");
            Faction other = NewFaction("Raiders");
            captor.SetRelationDirect(other, FactionRelationKind.Hostile, -100);
            return (captor, other);
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, string defName = "RawPotatoes")
        {
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        // ---- content ----

        [Fact]
        public void Warden_JobDefs_load_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(JobDefOf.PrisonerAttemptRecruit);
            Assert.NotNull(JobDefOf.FeedPatient);
        }

        [Fact]
        public void WardenAttemptRecruit_and_WardenFeed_WorkGiverDefs_are_wired_to_real_scanners()
        {
            WorkGiverDef recruit = DefDatabase<WorkGiverDef>.GetNamed("WardenAttemptRecruit");
            WorkGiverDef feed = DefDatabase<WorkGiverDef>.GetNamed("WardenFeed");
            Assert.IsType<WorkGiver_Warden_AttemptRecruit>(recruit.Worker);
            Assert.IsType<WorkGiver_Warden_Feed>(feed.Worker);
        }

        [Fact]
        public void WardenDeliverFood_is_the_other_side_of_this_givers_own_Downed_split()
        {
            // It was the pending placeholder for two passes, on the grounds recorded in this file's own doc —
            // no room system, therefore no distinct case. Building.RoomTracker landed and it has one: the
            // ambulatory prisoner whose room holds nothing to eat. The two givers still never share a
            // patient, which is what this assertion is really about. See WardenDeliverFoodTests.
            WorkGiverDef deliverFood = DefDatabase<WorkGiverDef>.GetNamed("WardenDeliverFood");
            Assert.IsType<WorkGiver_Warden_DeliverFood>(deliverFood.Worker);
        }

        // ---- WorkGiver_Warden_AttemptRecruit ----

        [Fact]
        public void Recruit_giver_has_no_job_when_the_prisoner_is_not_set_to_be_recruited()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim); // interactionMode defaults to NoInteraction

            var giver = new WorkGiver_Warden_AttemptRecruit();
            Assert.False(giver.HasJobOnThing(warden, victim));
        }

        [Fact]
        public void Recruit_giver_has_no_job_on_someone_elses_prisoner()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Faction rival = NewFaction("Rivals");
            rival.SetRelationDirect(other, FactionRelationKind.Hostile, -100);

            Pawn captorPawn = SpawnHuman(map, new IntVec3(0, 0, 0), "Captor");
            captorPawn.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(captorPawn, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;

            Pawn strangerWarden = SpawnHuman(map, new IntVec3(1, 0, 1), "StrangerWarden");
            strangerWarden.faction = rival;

            var giver = new WorkGiver_Warden_AttemptRecruit();
            Assert.False(giver.HasJobOnThing(strangerWarden, victim));
        }

        [Fact]
        public void Recruit_giver_finds_a_reachable_prisoner_set_to_be_recruited()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(warden, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;

            var giver = new WorkGiver_Warden_AttemptRecruit();
            Assert.Contains(victim, giver.PotentialWorkThingsGlobal(warden));
            Assert.True(giver.HasJobOnThing(warden, victim));

            Job? job = giver.JobOnThing(warden, victim);
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.PrisonerAttemptRecruit, job!.def);
            Assert.Same(victim, job.GetTarget(TargetIndex.A).Thing);
        }

        // ---- JobDriver_Warden_AttemptRecruit ----

        [Fact]
        public void One_completed_recruit_job_reduces_resistance_by_exactly_the_base_amount()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(2, 0, 2), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(warden, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
            tracker.resistance = 5f;

            warden.jobs.StartJob(new Job(JobDefOf.PrisonerAttemptRecruit, victim));

            // A completed job immediately chains into a fresh one (still eligible, still AttemptRecruit) —
            // stop ticking the instant resistance first moves, rather than guessing how many ticks one visit
            // (goto + wait) takes, so a second visit's wait beat never gets a chance to start counting down.
            float before = tracker.resistance;
            int ticks = 0;
            while (tracker.resistance >= before && ticks < 2000)
            {
                RunTicks(1, warden);
                ticks++;
            }

            Assert.True(ticks < 2000, "Resistance never moved.");
            Assert.Equal(before - WardenUtility.BaseResistanceReductionPerInteraction, tracker.resistance);
            Assert.True(tracker.IsPrisoner, "One visit off a resistance of 5 should reduce, not yet recruit.");
        }

        [Fact]
        public void Recruit_job_fails_cleanly_when_the_prisoner_is_released_mid_job()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            Pawn_GuestTracker tracker = CaptureUtility.Capture(warden, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;

            warden.jobs.StartJob(new Job(JobDefOf.PrisonerAttemptRecruit, victim));
            victim.Destroy();

            // Must end the job, not throw.
            RunTicks(300, warden);
            Assert.NotEqual(JobDefOf.PrisonerAttemptRecruit, warden.jobs.curJob?.def);
        }

        // ---- WorkGiver_Warden_Feed ----

        [Fact]
        public void Feed_giver_has_no_job_for_a_prisoner_that_is_not_downed()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.health.ForceDowned = false;
            victim.needs.food!.CurLevelPercentage = 0.1f;
            SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_Warden_Feed();
            Assert.False(giver.HasJobOnThing(warden, victim));
        }

        [Fact]
        public void Feed_giver_has_no_job_for_a_downed_prisoner_that_is_not_hungry()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_Warden_Feed();
            Assert.Equal(HungerCategory.Fed, victim.needs.food!.CurCategory);
            Assert.False(giver.HasJobOnThing(warden, victim));
        }

        [Fact]
        public void Feed_giver_has_no_job_when_no_food_is_reachable()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.needs.food!.CurLevelPercentage = 0.1f;

            var giver = new WorkGiver_Warden_Feed();
            Assert.False(giver.HasJobOnThing(warden, victim));
        }

        [Fact]
        public void Feed_giver_finds_a_downed_hungry_prisoner_with_food_reachable()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.needs.food!.CurLevelPercentage = 0.1f;
            Thing food = SpawnFood(map, new IntVec3(1, 0, 1));

            var giver = new WorkGiver_Warden_Feed();
            Assert.True(giver.HasJobOnThing(warden, victim));

            Job? job = giver.JobOnThing(warden, victim);
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.FeedPatient, job!.def);
            Assert.Same(food, job.GetTarget(TargetIndex.A).Thing);
            Assert.Same(victim, job.GetTarget(TargetIndex.B).Thing);
        }

        // ---- JobDriver_Warden_Feed ----

        [Fact]
        public void Feed_job_raises_the_prisoners_hunger_and_consumes_the_food()
        {
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(2, 0, 2), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.needs.food!.CurLevelPercentage = 0.1f;
            float hungerBefore = victim.needs.food.CurLevel;
            Thing food = SpawnFood(map, new IntVec3(4, 0, 4));

            warden.jobs.StartJob(new Job(JobDefOf.FeedPatient, food, victim));
            RunTicks(1000, warden);

            Assert.True(victim.needs.food.CurLevel > hungerBefore, "Feeding should have raised the prisoner's food need.");
            Assert.True(food.Destroyed || food.stackCount < 1 || food.stackCount == food.def.stackLimit - 1 || food.Destroyed,
                "The single food item should have been (at least partially) consumed.");
        }

        // ---- Scribe round trip ----

        /// <summary>Same purpose as <c>CaptureTests.CaptureRoot</c>: a minimal composite root so a saved
        /// Pawn's cross-referenced <see cref="Pawn.faction"/> — deep-saved only via <see cref="Faction"/>,
        /// not via the Map — actually resolves (see that class's own doc for why nothing yet deep-saves a
        /// whole game to make this unnecessary).</summary>
        private sealed class WardenRoot : IExposable
        {
            public CoreMap? map;
            public List<Faction> factions = new List<Faction>();

            public void ExposeData()
            {
                CoreMap? m = map;
                Scribe_Deep.Look(ref m, "map");
                map = m;
                List<Faction>? f = factions;
                Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
                factions = f ?? new List<Faction>();
            }
        }

        [Fact]
        public void A_feed_job_targeting_a_prisoner_pawn_round_trips_through_scribe()
        {
            // JobDriver itself carries no save data of its own (see JobDriver's own doc — a fresh one is
            // rebuilt from the deep-saved Job on load), and neither WorkGiver_Warden_Feed nor
            // JobDriver_Warden_Feed add any new persisted field; what is new here, versus the generic
            // Job/Pawn_JobTracker round trip already pinned in AITests, is a second target (B) that is itself
            // a Pawn rather than a plain Thing or bare cell — exactly the shape WardenAttemptRecruit and
            // WardenFeed both need resolved correctly after a load.
            CoreMap map = NewMap(6, 6);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(4, 0, 4), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.needs.food!.CurLevelPercentage = 0.1f;
            Thing food = SpawnFood(map, new IntVec3(2, 0, 2));

            warden.jobs.StartJob(new Job(JobDefOf.FeedPatient, food, victim));

            var root = new WardenRoot { map = map, factions = new List<Faction> { captor, other } };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            WardenRoot loaded = Scribe.Load<WardenRoot>(xml, "root", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            var loadedWarden = (Pawn)loaded.map!.mapPawns.AllPawns[0];
            var loadedVictim = (Pawn)loaded.map.mapPawns.AllPawns[1];
            Thing loadedFood = loaded.map.listerThings.ThingsOfDef(food.def)[0];

            Assert.NotNull(loadedWarden.jobs.curJob);
            Assert.Equal(JobDefOf.FeedPatient, loadedWarden.jobs.curJob!.def);
            Assert.Same(loadedFood, loadedWarden.jobs.curJob.GetTarget(TargetIndex.A).Thing);
            Assert.Same(loadedVictim, loadedWarden.jobs.curJob.GetTarget(TargetIndex.B).Thing);

            // The loaded job resumes (via a freshly rebuilt driver) and finishes the same as a never-saved
            // one would.
            float hungerBefore = loadedVictim.needs.food!.CurLevel;
            RunTicks(1000, loadedWarden);
            Assert.True(loadedVictim.needs.food.CurLevel > hungerBefore);
        }

        // ---- end to end: the point of the whole module ----

        [Fact]
        public void A_downed_hostile_is_captured_then_a_colonist_recruits_it_entirely_through_the_job_system()
        {
            CoreMap map = NewMap(8, 8);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(5, 0, 5), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;

            // Capture is a bare state transition, not yet job-driven in this pass (see CaptureUtility's own
            // doc) — everything from here on is the colonist doing its own work, not a test calling into
            // WardenUtility directly.
            Assert.True(CaptureUtility.CanCapture(warden, victim));
            Pawn_GuestTracker tracker = CaptureUtility.Capture(warden, victim)!;
            tracker.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
            float resistanceAtCapture = tracker.resistance;

            // Nothing tells the warden to do this — TryFindAndStartJob runs the ordinary think tree, which
            // reaches JobGiver_Work, which scans WardenAttemptRecruit like any other WorkGiverDef.
            warden.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.PrisonerAttemptRecruit, warden.jobs.curJob?.def);

            // Generous: worst-case starting resistance (14) needs 15 separate visits (goto + wait + interact
            // each), the work-giver scan restarting a fresh one every time the last one ends.
            RunTicks(50000, warden);

            bool recruited = ReferenceEquals(victim.faction, captor) && CaptureUtility.FindHostFaction(victim) == null;
            bool resistanceFell = !recruited && tracker.resistance < resistanceAtCapture;
            Assert.True(recruited || resistanceFell, "The prisoner's resistance should have fallen, or it should have been recruited.");
            // With this much headroom the loop should always actually finish.
            Assert.True(recruited, "A downed hostile captured then repeatedly visited by its own warden should end up recruited.");
        }

        [Fact]
        public void A_downed_hungry_prisoner_is_fed_by_a_colonist_entirely_through_the_job_system()
        {
            CoreMap map = NewMap(8, 8);
            (Faction captor, Faction other) = HostileFactions();
            Pawn warden = SpawnHuman(map, new IntVec3(0, 0, 0), "Warden");
            warden.faction = captor;
            Pawn victim = SpawnHuman(map, new IntVec3(5, 0, 5), "Victim");
            victim.faction = other;
            victim.health.ForceDowned = true;
            CaptureUtility.Capture(warden, victim);
            victim.needs.food!.CurLevelPercentage = 0.1f;
            float hungerBefore = victim.needs.food.CurLevel;
            SpawnFood(map, new IntVec3(7, 0, 0));

            RunTicks(2000, warden);

            Assert.True(victim.needs.food.CurLevel > hungerBefore, "The warden should have found and fed its own hungry, downed prisoner unprompted.");
        }
    }
}
