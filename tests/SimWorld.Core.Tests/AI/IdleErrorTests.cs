using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// A think tree always ends in a job (system 9: AI — RimWorld's <c>Verse.AI.JobGiver_IdleError</c>, the last
    /// node of its main tree).
    ///
    /// <para/><b>The bug.</b> <see cref="Pawn_JobTracker.JobTrackerTick"/> runs the pawn's whole tree on every
    /// tick it has no job, as RimWorld's does — RimWorld never pays for that because its tree cannot come back
    /// empty. This port's could: <see cref="JobGiver_WanderAnywhere"/> at the bottom gives up after eight random
    /// offsets all land somewhere a pawn can't stand or can't reach. The storyteller bench had three raiders in
    /// that state for four in-game days — each asked every tier of the tree, 60 times a second, to produce
    /// nothing — and the run took five times as long as the same days without them.
    ///
    /// <para/>What is counted here is the number of times the tree's root is asked, over a thousand ticks, for a
    /// pawn nothing can find a job for. The bound is <see cref="JobGiver_IdleError.WaitTime"/> and the thousand,
    /// never a literal count: the claim is that it is asked about once per wait, not once per tick.
    /// </summary>
    public class IdleErrorTests : ContentTestBase
    {
        public IdleErrorTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private static CoreMap NewMap(int size) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static void Rock(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), c, map);

        /// <summary>A human with eight walls round it: a real region of one cell, and nowhere to go.</summary>
        private static Pawn BoxedIn(CoreMap map, IntVec3 at, ThingDef? def = null)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx != 0 || dz != 0) Rock(map, at + new IntVec3(dx, dz));
                }
            }
            Pawn pawn = new Pawn(def ?? Human, "Boxed");
            GenSpawn.Spawn(pawn, at, map);
            return pawn;
        }

        /// <summary>Counts how often a think tree's root is asked for a job, and changes nothing about the
        /// answer. Swapped in over the shipped tree for one test and swapped back out.</summary>
        private sealed class CountingRoot : ThinkNode
        {
            private readonly ThinkNode inner;
            public int Runs;

            public CountingRoot(ThinkNode inner) => this.inner = inner;

            public override ThinkResult TryIssueJobPackage(Pawn pawn)
            {
                Runs++;
                return inner.TryIssueJobPackage(pawn);
            }
        }

        private static int CountThinkRuns(ThinkTreeDef tree, Action body)
        {
            ThinkNode original = tree.thinkRoot;
            var counter = new CountingRoot(original);
            tree.thinkRoot = counter;
            try
            {
                body();
            }
            finally
            {
                tree.thinkRoot = original;
            }
            return counter.Runs;
        }

        private static bool IsAttackJob(Job? job) =>
            job != null && (job.def == CombatAIDefOf.AttackMelee || job.def == CombatAIDefOf.AttackStatic);

        // ---- content ----

        [Fact]
        public void Both_main_trees_end_in_the_idle_wait()
        {
            Assert.Empty(Content.Result.Errors);
            foreach (ThinkTreeDef tree in new[] { ThinkTreeDefOf.Humanlike, ThinkTreeDefOf.Animal })
            {
                var root = Assert.IsType<ThinkNode_Priority>(tree.thinkRoot);
                Assert.IsType<JobGiver_IdleError>(root.subNodes[root.subNodes.Count - 1]);
            }
        }

        [Fact]
        public void The_wait_is_a_real_job_the_constant_tree_may_interrupt()
        {
            Assert.NotNull(WaitJobDefOf.Wait);
            Assert.Equal(typeof(JobDriver_Wait), WaitJobDefOf.Wait.driverClass);
            Assert.True(WaitJobDefOf.Wait.casualInterruptible, "an idle wait the constant tree could not break into would be a pawn that ignores a fight");
            Assert.True(JobGiver_IdleError.WaitTime > 1);
        }

        // ---- the pawn nothing can use ----

        [Fact]
        public void A_pawn_the_tree_cannot_use_gets_a_wait_not_nothing()
        {
            CoreMap map = NewMap(20);
            Pawn pawn = BoxedIn(map, new IntVec3(10, 0, 10));

            // The tier that answers is the last one, which is the whole of the claim: everything above it,
            // wander included, found nothing for this pawn.
            ThinkResult result = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn);
            Assert.True(result.IsValid);
            Assert.IsType<JobGiver_IdleError>(result.SourceNode);
            Assert.Equal(WaitJobDefOf.Wait, result.Job!.def);
            Assert.Equal(JobGiver_IdleError.WaitTime, result.Job.expiryInterval);

            RunTicks(2, pawn);
            Assert.Equal(WaitJobDefOf.Wait, pawn.jobs.curJob?.def);
        }

        [Fact]
        public void A_pawn_the_tree_cannot_use_is_not_asked_again_until_its_wait_is_up()
        {
            CoreMap map = NewMap(20);
            Pawn pawn = BoxedIn(map, new IntVec3(10, 0, 10));
            const int ticks = 1000;

            int runs = CountThinkRuns(ThinkTreeDefOf.Humanlike, () => RunTicks(ticks, pawn));

            Assert.True(runs >= ticks / JobGiver_IdleError.WaitTime - 1, "it should still look again when the wait is up; asked " + runs + " times");
            Assert.True(runs <= ticks / JobGiver_IdleError.WaitTime + 2,
                "a pawn with nothing to do was asked the whole tree " + runs + " times in " + ticks + " ticks");
        }

        [Fact]
        public void A_pawn_placed_inside_rock_is_not_asked_every_tick_either()
        {
            // The case that found it: not walled in but embedded, standing on an impassable cell and so in no
            // region at all, which makes every reachability question in the tree false before it starts.
            CoreMap map = NewMap(20);
            var cell = new IntVec3(10, 0, 10);
            Rock(map, cell);
            var pawn = new Pawn(Human, "Embedded");
            GenSpawn.Spawn(pawn, cell, map);
            const int ticks = 1000;

            int runs = CountThinkRuns(ThinkTreeDefOf.Humanlike, () => RunTicks(ticks, pawn));

            Assert.True(runs >= 2, "it should look again when the wait is up; asked " + runs + " times");
            Assert.True(runs <= ticks / JobGiver_IdleError.WaitTime + 2,
                "a pawn inside rock was asked the whole tree " + runs + " times in " + ticks + " ticks");
            Assert.Equal(WaitJobDefOf.Wait, pawn.jobs.curJob?.def);
        }

        [Fact]
        public void An_animal_the_tree_cannot_use_waits_the_same_way()
        {
            CoreMap map = NewMap(20);
            Pawn husky = BoxedIn(map, new IntVec3(10, 0, 10), Husky);
            const int ticks = 1000;

            int runs = CountThinkRuns(ThinkTreeDefOf.Animal, () => RunTicks(ticks, husky));

            Assert.True(runs <= ticks / JobGiver_IdleError.WaitTime + 2, "asked " + runs + " times in " + ticks + " ticks");
            Assert.Equal(WaitJobDefOf.Wait, husky.jobs.curJob?.def);
        }

        [Fact]
        public void A_pawn_with_somewhere_to_go_still_wanders_the_idle_wait_is_only_the_last_resort()
        {
            CoreMap map = NewMap(40);
            var pawn = new Pawn(Human, "Free");
            GenSpawn.Spawn(pawn, new IntVec3(20, 0, 20), map);

            RunTicks(3, pawn);

            Assert.NotEqual(WaitJobDefOf.Wait, pawn.jobs.curJob?.def);
            Assert.NotNull(pawn.jobs.curJob);
        }

        // ---- the wait is not a trap ----

        [Fact]
        public void A_waiting_pawn_breaks_off_to_fight_when_an_enemy_appears()
        {
            CoreMap map = NewMap(16);
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(ours);
            Find.FactionManager.Add(theirs);
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);

            Pawn citizen = NewHuman("Waiter");
            citizen.faction = ours;
            citizen.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Revolver")));
            GenSpawn.Spawn(citizen, new IntVec3(2, 0, 2), map);
            citizen.jobs.StartJob(new Job(WaitJobDefOf.Wait) { expiryInterval = JobGiver_IdleError.WaitTime * 50 });
            Assert.Equal(WaitJobDefOf.Wait, citizen.jobs.curJob?.def);

            Pawn raider = NewHuman("Raider");
            raider.faction = theirs;
            raider.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Revolver")));
            GenSpawn.Spawn(raider, new IntVec3(6, 0, 2), map);

            RunTicks(ConstantThinkTreeTuning.IntervalTicks * 2, citizen);

            Assert.True(IsAttackJob(citizen.jobs.curJob), "a pawn idling in a wait kept idling with an enemy in front of it");
            Assert.Same(raider, citizen.jobs.curJob!.GetTarget(TargetIndex.A).Thing);
        }

        // ---- Scribe ----

        [Fact]
        public void A_pawn_in_the_middle_of_a_wait_round_trips_through_Scribe()
        {
            CoreMap map = NewMap(20);
            Pawn pawn = BoxedIn(map, new IntVec3(10, 0, 10));
            RunTicks(30, pawn);
            Assert.Equal(WaitJobDefOf.Wait, pawn.jobs.curJob?.def);
            int startTick = pawn.jobs.curJob!.startTick;

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn loadedPawn = Assert.Single(loaded.mapPawns.AllPawns);
            Assert.NotNull(loadedPawn.jobs.curJob);
            Assert.Equal(WaitJobDefOf.Wait, loadedPawn.jobs.curJob!.def);
            Assert.Equal(JobGiver_IdleError.WaitTime, loadedPawn.jobs.curJob.expiryInterval);
            Assert.Equal(startTick, loadedPawn.jobs.curJob.startTick);
        }
    }
}
