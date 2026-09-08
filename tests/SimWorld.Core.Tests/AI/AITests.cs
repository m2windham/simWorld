using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>Think tree, jobs, reservations and pathing (RimWorld's AI / Job / Pathing layer, system 9).</summary>
    public class AITests : ContentTestBase
    {
        public AITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ, TerrainDef? fill = null) =>
            new CoreMap(sizeX, sizeZ, fill ?? TerrainDefOf.Soil);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            Thing rock = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, string defName = "RawPotatoes")
        {
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        // ---- content ----

        [Fact]
        public void AI_content_loads_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(JobDefOf.Ingest);
            Assert.NotNull(JobDefOf.LayDown);
            Assert.NotNull(JobDefOf.GotoWander);
            Assert.NotNull(JobDefOf.Mine);
            Assert.NotNull(ThinkTreeDefOf.Humanlike);
            Assert.NotNull(ThinkTreeDefOf.Humanlike.thinkRoot);
            Assert.IsType<ThinkNode_Priority>(ThinkTreeDefOf.Humanlike.thinkRoot);
        }

        [Fact]
        public void Humanlike_think_tree_orders_danger_over_needs_over_orders_over_edicts_over_work()
        {
            // The edict tier (system 12: the god layer) was inserted between directed orders and routine work
            // — see JobGiver_Edicts's own doc for why that position is §10's "citizens keep full agency"
            // clause made concrete: a need or a queued order still pre-empts a standing edict, and an edict
            // still only ever pre-empts routine work, never the other way around.
            var root = (ThinkNode_Priority)ThinkTreeDefOf.Humanlike.thinkRoot;
            Assert.IsType<ThinkNode_ConditionalInMentalState>(root.subNodes[0]);
            Assert.IsType<ThinkNode_ConditionalHungry>(root.subNodes[1]);
            Assert.IsType<ThinkNode_ConditionalTired>(root.subNodes[2]);
            Assert.IsType<JobGiver_DirectedOrder>(root.subNodes[3]);
            Assert.IsType<global::SimWorld.AI.JobGiver_Edicts>(root.subNodes[4]);
            Assert.IsType<JobGiver_Work>(root.subNodes[5]);
        }

        [Fact]
        public void Mine_WorkGiverDef_is_wired_to_a_real_scanner()
        {
            WorkGiverDef mine = DefDatabase<WorkGiverDef>.GetNamed("Mine");
            Assert.IsType<WorkGiver_Miner>(mine.Worker);
        }

        // ---- LocalTargetInfo ----

        [Fact]
        public void LocalTargetInfo_default_is_invalid_and_targets_compare_by_identity_or_cell()
        {
            Assert.False(default(LocalTargetInfo).IsValid);
            Assert.False(LocalTargetInfo.Invalid.IsValid);

            var a = new LocalTargetInfo(new IntVec3(1, 0, 1));
            var b = new LocalTargetInfo(new IntVec3(1, 0, 1));
            var c = new LocalTargetInfo(new IntVec3(2, 0, 1));
            Assert.True(a.IsValid);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);

            CoreMap map = NewMap(3, 3);
            Thing rock = SpawnRock(map, new IntVec3(0, 0, 0));
            LocalTargetInfo t1 = rock;
            LocalTargetInfo t2 = rock;
            Assert.True(t1.HasThing);
            Assert.Equal(t1, t2);
            Assert.Equal(new IntVec3(0, 0, 0), t1.Cell);
        }

        // ---- Reservations ----

        [Fact]
        public void ReservationManager_blocks_a_second_claimant_and_frees_on_release()
        {
            CoreMap map = NewMap(5, 5);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(4, 0, 4), "B");
            Thing rock = SpawnRock(map, new IntVec3(2, 0, 2));

            Assert.True(map.reservationManager.CanReserve(a, rock));
            Assert.True(map.reservationManager.Reserve(a, rock));
            Assert.True(map.reservationManager.IsReservedBy(a, rock));

            // A second pawn cannot claim the same target while A holds it.
            Assert.False(map.reservationManager.CanReserve(b, rock));
            Assert.False(map.reservationManager.Reserve(b, rock));

            // Reserving again for the same pawn is a harmless no-op, not a second claim.
            Assert.True(map.reservationManager.Reserve(a, rock));

            map.reservationManager.Release(a, rock);
            Assert.False(map.reservationManager.IsReservedBy(a, rock));
            Assert.True(map.reservationManager.CanReserve(b, rock));
        }

        [Fact]
        public void ReservationManager_ReleaseAllClaimedBy_drops_every_claim_for_that_pawn()
        {
            CoreMap map = NewMap(5, 5);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing rock1 = SpawnRock(map, new IntVec3(1, 0, 1));
            Thing rock2 = SpawnRock(map, new IntVec3(2, 0, 2), "Granite");

            map.reservationManager.Reserve(a, rock1);
            map.reservationManager.Reserve(a, rock2);
            map.reservationManager.ReleaseAllClaimedBy(a);

            Assert.False(map.reservationManager.IsReserved(rock1));
            Assert.False(map.reservationManager.IsReserved(rock2));
        }

        // ---- Pathing ----

        [Fact]
        public void PathFinder_finds_a_direct_diagonal_path_on_open_ground()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, new IntVec3(2, 0, 2), PathEndMode.OnCell);
            Assert.True(path.Found);
            Assert.Equal(2, path.NodesLeftCount);
            path.ReleaseToPool();
        }

        [Fact]
        public void PathFinder_reports_NotFound_for_a_fully_enclosed_target_instead_of_throwing()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            var trapped = new IntVec3(3, 0, 3);
            foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(trapped))
            {
                SpawnRock(map, n);
            }

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, trapped, PathEndMode.OnCell);
            Assert.False(path.Found);
        }

        [Fact]
        public void PathFinder_does_not_cut_a_diagonal_corner_between_two_impassable_cells()
        {
            CoreMap map = NewMap(3, 3);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            // Blocks both cardinal approaches to (1,0,1) from (0,0,0); only the diagonal step remains, and
            // it must be refused since neither flanking cardinal cell is walkable.
            SpawnRock(map, new IntVec3(1, 0, 0));
            SpawnRock(map, new IntVec3(0, 0, 1));

            PawnPath blocked = map.pathFinder.FindPath(pawn, pawn.Position, new IntVec3(1, 0, 1), PathEndMode.OnCell);
            Assert.False(blocked.Found);
        }

        [Fact]
        public void PathFinder_takes_the_cardinal_detour_when_only_one_flank_is_blocked()
        {
            CoreMap map = NewMap(3, 3);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            // Only the east flank is blocked; the corner still can't be cut (both flanks must be walkable),
            // so the pawn must go north then east — two cardinal steps, not one diagonal step.
            SpawnRock(map, new IntVec3(1, 0, 0));

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, new IntVec3(1, 0, 1), PathEndMode.OnCell);
            Assert.True(path.Found);
            Assert.Equal(2, path.NodesLeftCount);
            path.ReleaseToPool();
        }

        [Fact]
        public void PathFinder_Touch_mode_paths_adjacent_to_an_impassable_thing()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing rock = SpawnRock(map, new IntVec3(3, 0, 3));

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, rock, PathEndMode.Touch);
            Assert.True(path.Found);
            Assert.True(path.Peek(int.MaxValue).AdjacentTo8Way(rock.Position));
            path.ReleaseToPool();
        }

        [Fact]
        public void Reachability_matches_PathFinder_and_updates_when_the_grid_changes()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            var dest = new IntVec3(4, 0, 4);

            Assert.True(Reachability.CanReach(pawn, dest));

            // Wall off the whole map from that corner; reachability must notice without a full map's worth
            // of TerrainDef churn required to prove it — a single edifice change is enough.
            for (int z = 0; z < 5; z++)
            {
                SpawnRock(map, new IntVec3(3, 0, z));
            }
            Assert.False(Reachability.CanReach(pawn, dest));

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            Assert.False(path.Found);
        }

        [Fact]
        public void Pathing_is_deterministic_for_the_same_map_and_target()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnRock(map, new IntVec3(3, 0, 1));
            SpawnRock(map, new IntVec3(3, 0, 2));
            SpawnRock(map, new IntVec3(3, 0, 3));
            var dest = new IntVec3(6, 0, 2);

            PawnPath first = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            var firstCells = new List<IntVec3>();
            while (!first.Finished) firstCells.Add(first.ConsumeNextNode());
            first.ReleaseToPool();

            PawnPath second = map.pathFinder.FindPath(pawn, pawn.Position, dest, PathEndMode.OnCell);
            var secondCells = new List<IntVec3>();
            while (!second.Finished) secondCells.Add(second.ConsumeNextNode());
            second.ReleaseToPool();

            Assert.Equal(firstCells, secondCells);
        }

        [Fact]
        public void Job_choice_is_deterministic_for_the_same_seed_and_map()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(5, 0, 5));
            var wander = new JobGiver_WanderAnywhere();

            Rand.Current = new RandomStream(999);
            Job? first = wander.TryIssueJobPackage(pawn).Job;

            Rand.Current = new RandomStream(999);
            Job? second = wander.TryIssueJobPackage(pawn).Job;

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.GetTarget(TargetIndex.A).Cell, second!.GetTarget(TargetIndex.A).Cell);
        }

        [Fact]
        public void MoveSpeed_stat_matches_the_documented_vanilla_default_and_drives_move_ticks()
        {
            CoreMap map = NewMap(3, 3);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            Assert.Equal(4.6f, pawn.GetStatValue(StatDefOf.MoveSpeed), 2);
            // 60 ticks/second ÷ 4.6 cells/second, rounded.
            Assert.Equal(13, pawn.pather.TicksPerMoveCardinal);
        }

        [Fact]
        public void A_pawn_moving_does_not_perturb_the_path_grid_or_the_reachability_cache()
        {
            // A pawn's own cell never contributes to path cost (PathGrid skips ThingCategory.Pawn), so
            // walking must not churn PathGrid.Version — if it did, every step by every pawn on the map would
            // force AI.Reachability's whole flood-fill cache to recompute, defeating the point of caching it.
            CoreMap map = NewMap(20, 20);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            var dest = new IntVec3(19, 0, 19);
            Assert.True(Reachability.CanReach(pawn, dest)); // forces the first flood-fill
            int versionBeforeMoving = map.pathGrid.Version;

            // Driven through the job system (not a bare pather.StartPath call) so nothing else — in
            // particular the idle-wander fallback — starts a competing job on top of it.
            pawn.jobs.StartJob(new Job(JobDefOf.GotoWander, dest));
            RunTicks(400, pawn);

            Assert.NotEqual(new IntVec3(0, 0, 0), pawn.Position);
            Assert.Equal(versionBeforeMoving, map.pathGrid.Version);
        }

        [Fact]
        public void Steady_state_pather_ticking_allocates_nothing()
        {
            CoreMap map = NewMap(300, 300);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            pawn.pather.StartPath(new IntVec3(299, 0, 299), PathEndMode.OnCell);
            // Generous warm-up: JIT tiering settling in the test host is otherwise indistinguishable from a
            // real regression in the measurement below.
            for (int i = 0; i < 2000; i++) pawn.pather.PatherTick();

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            const int measuredTicks = 500;
            for (int i = 0; i < measuredTicks; i++) pawn.pather.PatherTick();
            long after = System.GC.GetAllocatedBytesForCurrentThread();

            Assert.True(pawn.pather.Moving, "The path should still be in progress this far in on a 300x300 map.");
            // A standalone (non-test-host) run of this exact scenario measures exactly 0 bytes; a generous
            // tolerance here absorbs test-host JIT/GC noise without masking the ~24 bytes/tick this caught
            // before ThingGrid.MoveSingleCell existed (a CellRect.Cells iterator allocation on every move).
            Assert.True(after - before < measuredTicks * 4, $"Expected near-zero allocation, got {after - before} bytes over {measuredTicks} ticks.");
        }

        // ---- Toils / JobDriver state machine ----

        [Fact]
        public void JobDriver_fails_cleanly_when_its_target_is_destroyed_mid_job()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing food = SpawnFood(map, new IntVec3(0, 0, 0));

            pawn.jobs.StartJob(new Job(JobDefOf.Ingest, food));
            Assert.NotNull(pawn.jobs.curJob);

            food.Destroy();

            // Must end the job, not throw, the next time the driver ticks — whatever the pawn does next
            // (including immediately picking up a fresh idle-wander job) is not the point here.
            RunTicks(1, pawn);
            Assert.NotEqual(JobDefOf.Ingest, pawn.jobs.curJob?.def);
        }

        [Fact]
        public void Toils_Reserve_ends_the_job_immediately_if_the_target_is_already_claimed()
        {
            CoreMap map = NewMap(5, 5);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(4, 0, 4), "B");
            Thing rock = SpawnRock(map, new IntVec3(2, 0, 2));

            map.reservationManager.Reserve(a, rock);

            b.jobs.StartJob(new Job(JobDefOf.Mine, rock));

            Assert.Null(b.jobs.curJob);
        }

        // ---- End-to-end: the point of the whole module ----

        [Fact]
        public void Hungry_pawn_finds_food_paths_to_it_eats_and_hunger_falls()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnFood(map, new IntVec3(9, 0, 9));

            pawn.needs.food!.CurLevelPercentage = 0.1f;
            float hungerBefore = pawn.needs.food.CurLevel;

            RunTicks(3000, pawn);

            Assert.True(pawn.needs.food.CurLevel > hungerBefore, "Eating should have raised the food need.");
        }

        [Fact]
        public void Two_hungry_pawns_do_not_both_claim_the_only_food_item()
        {
            CoreMap map = NewMap(10, 10);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(9, 0, 9), "B");
            a.needs.food!.CurLevelPercentage = 0.1f;
            b.needs.food!.CurLevelPercentage = 0.1f;
            Thing food = SpawnFood(map, new IntVec3(5, 0, 5));

            a.jobs.TryFindAndStartJob();
            b.jobs.TryFindAndStartJob();

            bool aHasIt = a.jobs.curJob != null && a.jobs.curJob.GetTarget(TargetIndex.A).Thing == food;
            bool bHasIt = b.jobs.curJob != null && b.jobs.curJob.GetTarget(TargetIndex.A).Thing == food;
            Assert.True(aHasIt ^ bHasIt, "Exactly one pawn should have claimed the only food item.");
        }

        [Fact]
        public void Idle_colonist_mines_a_reachable_rock()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            Thing rock = SpawnRock(map, new IntVec3(2, 0, 0));

            RunTicks(2000, pawn);

            Assert.True(rock.Destroyed);
        }

        [Fact]
        public void Tired_pawn_lies_down_and_rests()
        {
            CoreMap map = NewMap(5, 5);
            Pawn pawn = SpawnHuman(map, new IntVec3(2, 0, 2));
            pawn.needs.rest!.CurLevel = 0.05f;

            RunTicks(10, pawn);

            Assert.True(pawn.Asleep);
            Assert.Equal(JobDefOf.LayDown, pawn.jobs.curJob?.def);
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Job_tracker_current_job_queue_and_reservations_round_trip_through_Scribe()
        {
            CoreMap map = NewMap(6, 6);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0), "Miner");
            Thing rock = SpawnRock(map, new IntVec3(2, 0, 0));

            pawn.jobs.StartJob(new Job(JobDefOf.Mine, rock));
            Assert.NotNull(pawn.jobs.curJob);
            Assert.True(map.reservationManager.IsReservedBy(pawn, rock));

            var orderedWait = new Job(JobDefOf.GotoWander, new IntVec3(1, 0, 1)) { playerForced = true };
            pawn.jobs.QueueJob(orderedWait);

            string xml = Scribe.SaveToString(map, "map");
            Pawn.ResetThingIdCounter();
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn loadedPawn = (Pawn)loaded.mapPawns.AllPawns[0];
            Thing loadedRock = loaded.listerThings.ThingsOfDef(rock.def)[0];

            Assert.NotNull(loadedPawn.jobs.curJob);
            Assert.Equal(JobDefOf.Mine, loadedPawn.jobs.curJob!.def);
            Assert.Same(loadedRock, loadedPawn.jobs.curJob.GetTarget(TargetIndex.A).Thing);
            Assert.True(loaded.reservationManager.IsReservedBy(loadedPawn, loadedRock));

            Job? dequeued = loadedPawn.jobs.DequeueDirectedOrder();
            Assert.NotNull(dequeued);
            Assert.Equal(JobDefOf.GotoWander, dequeued!.def);
            Assert.Equal(new IntVec3(1, 0, 1), dequeued.GetTarget(TargetIndex.A).Cell);
            Assert.True(dequeued.playerForced);

            // The loaded job resumes (via a freshly rebuilt driver) rather than sitting inert: ticking it
            // eventually mines the rock out, same as a never-saved job would.
            RunTicks(2000, loadedPawn);
            Assert.True(loadedRock.Destroyed);
        }
    }
}
