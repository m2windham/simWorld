using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreFilth = SimWorld.Filth.Filth;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Filth
{
    /// <summary>
    /// A cleaning job takes a patch of piles, as RimWorld's does (<see cref="WorkGiver_CleanFilth.JobOnThing"/>
    /// and <see cref="JobDriver_CleanFilth"/>): the piles near the first one, from the same room only, up to a cap,
    /// cleaned one after another in one job, with a pile somebody else took skipped rather than ending it.
    /// </summary>
    public class CleanFilthQueueTests : ContentTestBase
    {
        public CleanFilthQueueTests(CoreContentFixture content) : base(content)
        {
            FilthMaker.ResetCache();
        }

        private static TerrainDef StoneRoad => DefDatabase<TerrainDef>.GetNamed("StreetStoneRoad");

        private static WorkGiver_CleanFilth Giver =>
            (WorkGiver_CleanFilth)DefDatabase<global::SimWorld.Work.WorkGiverDef>.GetNamed("CleanFilth").Worker;

        /// <summary>A walled, roofed, stone-floored room over <paramref name="rect"/> (the floor makes no filth
        /// of its own while the test watches), and one map tick so the rooms flood.</summary>
        private static void BuildRoom(CoreMap map, CellRect rect)
        {
            foreach (IntVec3 c in rect.EdgeCells)
            {
                if (map.edificeGrid[c] != null) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), c, map);
            }
            foreach (IntVec3 c in rect.Cells)
            {
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                map.terrainGrid.SetTerrain(c, StoneRoad);
            }
        }

        private static CoreFilth Dirty(CoreMap map, IntVec3 cell)
        {
            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            foreach (Thing t in map.thingGrid.ThingsListAt(cell))
            {
                if (t is CoreFilth f) return f;
            }
            throw new Xunit.Sdk.XunitException($"No filth was made at {cell}.");
        }

        private static void Settle()
        {
            for (int i = 0; i <= WorkGiver_CleanFilth.MinTicksSinceThickened; i++) Find.TickManager.DoSingleTick();
        }

        private static List<Thing> PilesOf(Job job)
        {
            var piles = new List<Thing> { job.targetA.Thing! };
            if (job.targetQueueA != null)
            {
                foreach (LocalTargetInfo t in job.targetQueueA) piles.Add(t.Thing!);
            }
            return piles;
        }

        private Pawn SpawnHuman(CoreMap map, IntVec3 cell)
        {
            Pawn p = NewHuman("Cleaner");
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        [Fact]
        public void One_job_takes_the_whole_patch_of_filth_in_the_room()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            map.MapTick();
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4));
            var piles = new List<CoreFilth>();
            foreach (IntVec3 c in new[] { new IntVec3(8, 0, 8), new IntVec3(9, 0, 8), new IntVec3(10, 0, 9), new IntVec3(7, 0, 10), new IntVec3(9, 0, 11), new IntVec3(6, 0, 7) })
            {
                piles.Add(Dirty(map, c));
            }
            Settle();

            Job job = Giver.JobOnThing(citizen, piles[0])!;

            List<Thing> taken = PilesOf(job);
            Assert.Equal(piles.Count, taken.Count);
            foreach (CoreFilth f in piles) Assert.Contains(f, taken);
        }

        [Fact]
        public void Filth_behind_a_wall_in_the_next_room_is_left_for_its_own_job()
        {
            var map = new CoreMap(24, 16, TerrainDefOf.Soil);
            // Two rooms sharing the wall at x = 10, with a door through it so the cleaner can reach both.
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Door")), new IntVec3(10, 0, 10), map);
            BuildRoom(map, new CellRect(2, 2, 9, 10));
            BuildRoom(map, new CellRect(10, 2, 9, 10));
            map.MapTick();
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 6));
            CoreFilth here = Dirty(map, new IntVec3(9, 0, 6));
            CoreFilth alsoHere = Dirty(map, new IntVec3(8, 0, 7));
            CoreFilth nextDoor = Dirty(map, new IntVec3(11, 0, 6));
            Settle();
            Assert.True(Giver.HasJobOnThing(citizen, nextDoor), "The next room's filth is work in its own right.");

            List<Thing> taken = PilesOf(Giver.JobOnThing(citizen, here)!);

            Assert.Contains(alsoHere, taken);
            Assert.DoesNotContain(nextDoor, taken);
        }

        [Fact]
        public void A_job_takes_at_most_fifteen_piles_and_leaves_the_rest_as_work()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            BuildRoom(map, new CellRect(2, 2, 14, 14));
            map.MapTick();
            Pawn citizen = SpawnHuman(map, new IntVec3(3, 0, 3));
            CoreFilth first = Dirty(map, new IntVec3(9, 0, 9));
            foreach (IntVec3 c in new CellRect(6, 6, 7, 7).Cells)
            {
                if (c != first.Position) Dirty(map, c);
            }
            Settle();

            List<Thing> taken = PilesOf(Giver.JobOnThing(citizen, first)!);

            // One pile per cell here, so the cap lands exactly.
            Assert.Equal(WorkGiver_CleanFilth.MaxPilesPerJob, taken.Count);
            Assert.Equal(taken.Count, new HashSet<Thing>(taken).Count);
        }

        [Fact]
        public void The_cleaner_scrubs_the_whole_patch_in_one_job_and_skips_a_pile_somebody_else_took()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            BuildRoom(map, new CellRect(2, 2, 12, 12));
            map.MapTick();
            Pawn citizen = SpawnHuman(map, new IntVec3(4, 0, 4));
            var piles = new List<CoreFilth>();
            foreach (IntVec3 c in new[] { new IntVec3(8, 0, 8), new IntVec3(9, 0, 8), new IntVec3(10, 0, 9), new IntVec3(7, 0, 10), new IntVec3(9, 0, 11) })
            {
                piles.Add(Dirty(map, c));
            }

            var cleaningJobs = new HashSet<Job>();
            CoreFilth? takenAway = null;
            for (int t = 0; t < 12000 && piles.Exists(f => !f.Destroyed); t++)
            {
                RunTicks(1, citizen);
                Job? job = citizen.jobs.curJob;
                if (job?.def != CleaningJobDefOf.Clean) continue;
                cleaningJobs.Add(job);
                // Once the job holds its patch, somebody else cleans one of the piles it has queued.
                if (takenAway == null && job.targetQueueA is { Count: > 0 } queue)
                {
                    takenAway = (CoreFilth)queue[queue.Count - 1].Thing!;
                    takenAway.Destroy();
                }
            }

            Assert.NotNull(takenAway);
            Assert.All(piles, f => Assert.True(f.Destroyed));
            Assert.Single(cleaningJobs);
        }
    }
}
