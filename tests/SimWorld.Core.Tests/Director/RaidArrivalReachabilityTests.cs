using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.AI.Group;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A raid lands where it can reach the colony (system: director.raids — RimWorld's
    /// <c>PawnsArrivalModeWorker_EdgeWalkIn.TryResolveRaidSpawnCenter</c>, <c>RCellFinder.TryFindRandomPawnEntryCell</c>
    /// and <c>CellFinder.RandomClosewalkCellNear</c>).
    ///
    /// <para/><b>The bug.</b> On the storyteller bench (seed 777, populated world) the day-14 raid took its entry
    /// cell from <c>rand.Range</c> along a random edge with no question asked, got (199, 0, 49) — a cell inside a
    /// limestone face — and its closewalk, which tried thirty offsets that were all rock, fell back to that
    /// cell. Three raiders were spawned inside the rock, belonged to no region, were given no job by a think tree
    /// asked on every tick, and stood there for four in-game days. The maps here are cut off on purpose, on every
    /// side but one, in each of the ways a real interior is: rock, deep water, and a perfectly good strip of open
    /// ground that nobody in the colony can walk to.
    /// </summary>
    public class RaidArrivalReachabilityTests : ContentTestBase
    {
        public RaidArrivalReachabilityTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        // ---- fixtures ----

        private const int Size = 50;

        /// <summary>First column of the sealed-off strip's wall, and so the width of the strip beside it.</summary>
        private const int WallX = 6;

        private static (Faction ours, Faction raiders) Factions()
        {
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            var theirs = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
            Find.FactionManager.Add(ours);
            Find.FactionManager.Add(theirs);
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            return (ours, theirs);
        }

        private static void Rock(CoreMap map, IntVec3 c) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Limestone")), c, map);

        private static Pawn Colonist(CoreMap map, Faction ours, IntVec3 at, string name = "Colonist")
        {
            Pawn pawn = NewHuman(name);
            pawn.faction = ours;
            GenSpawn.Spawn(pawn, at, map);
            return pawn;
        }

        /// <summary>
        /// A 50x50 interior whose colony is in the middle and whose edges are, one way and another, not places to
        /// land: the north edge is rock, the east edge is deep water, the whole west edge is open ground behind a
        /// wall the colony cannot cross — standable, unroofed, on the edge, and unreachable — and what is left is
        /// the south edge between the wall and the water.
        /// </summary>
        private static CoreMap CutOffMap(Faction ours, out Pawn colonist)
        {
            var map = new CoreMap(Size, Size, TerrainDefOf.Soil);
            for (int x = 0; x < Size; x++)
            {
                for (int z = Size - 3; z < Size; z++) Rock(map, new IntVec3(x, 0, z));
            }
            for (int x = Size - 6; x < Size; x++)
            {
                for (int z = 0; z < Size; z++) map.terrainGrid.SetTerrain(new IntVec3(x, 0, z), TerrainDefOf.WaterDeep);
            }
            for (int z = 0; z < Size; z++) Rock(map, new IntVec3(WallX, 0, z));

            colonist = Colonist(map, ours, new IntVec3(25, 0, 25));
            return map;
        }

        private static IncidentWorker_RaidEnemy NewWorker() =>
            (IncidentWorker_RaidEnemy)new IncidentDef
            {
                defName = "TestRaidArrival",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            }.Worker;

        private static IncidentWorker_RaidEnemy Raid(CoreMap map, Faction raiders, int seed)
        {
            Rand.Current = new RandomStream(seed);
            IncidentWorker_RaidEnemy worker = NewWorker();
            var parms = new IncidentParms { target = new CivilizationTarget { Map = map }, points = 400f, faction = raiders };
            Assert.True(worker.TryExecute(parms));
            Assert.NotEmpty(worker.LastRaidPawns!);
            return worker;
        }

        // ---- the arrival ----

        [Fact]
        public void A_raid_lands_on_the_one_side_of_the_map_that_reaches_the_colony()
        {
            (Faction ours, Faction raiders) = Factions();

            // Thirty fresh maps, thirty seeds: with three of the four edges unusable and one of them a trap
            // that only reachability can tell from the real thing, a landing that ignored it would land in the
            // west strip about half the time.
            for (int seed = 1; seed <= 30; seed++)
            {
                CoreMap map = CutOffMap(ours, out Pawn colonist);
                IncidentWorker_RaidEnemy worker = Raid(map, raiders, seed);

                foreach (Pawn raider in worker.LastRaidPawns!)
                {
                    Assert.True(raider.Spawned, "seed " + seed + ": raider not spawned");
                    Assert.True(GenGrid.Standable(raider.Position, map), "seed " + seed + ": a raider was put on " + raider.Position + ", which nothing can stand on");
                    Assert.True(raider.Position.x > WallX, "seed " + seed + ": a raider was put on " + raider.Position + ", in the strip the colony cannot reach");
                    Assert.True(
                        map.reachability.CanReachTarget(colonist, raider.Position, PathEndMode.OnCell),
                        "seed " + seed + ": " + raider.Position + " is not reachable from the colony");
                }
            }
        }

        [Fact]
        public void The_whole_squad_arrives_in_one_connected_area_with_a_lord_and_a_duty()
        {
            (Faction ours, Faction raiders) = Factions();
            CoreMap map = CutOffMap(ours, out _);

            IncidentWorker_RaidEnemy worker = Raid(map, raiders, 7);

            Lord lord = Assert.Single(map.lordManager.lords);
            Assert.Equal(worker.LastRaidPawns!.Count, lord.ownedPawns.Count);
            Assert.All(worker.LastRaidPawns!, r => Assert.Same(DutyDefOf.AssaultSettlement, r.mindState.duty));
            Pawn first = worker.LastRaidPawns![0];
            Assert.All(worker.LastRaidPawns!, r => Assert.True(map.reachability.CanReachTarget(first, r.Position, PathEndMode.OnCell)));
        }

        [Fact]
        public void A_map_with_no_edge_cell_that_reaches_the_colony_gets_no_raid()
        {
            // RimWorld: TryResolveRaidSpawnCenter false, the incident fails. Here that is the worker returning
            // false before it spawns anybody, makes a lord, or tells the chronicle a raid happened.
            (Faction ours, Faction raiders) = Factions();
            var map = new CoreMap(Size, Size, TerrainDefOf.Soil);
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (x < 2 || z < 2 || x >= Size - 2 || z >= Size - 2) Rock(map, new IntVec3(x, 0, z));
                }
            }
            Colonist(map, ours, new IntVec3(25, 0, 25));

            Rand.Current = new RandomStream(3);
            IncidentWorker_RaidEnemy worker = NewWorker();
            var target = new CivilizationTarget { Map = map };
            var parms = new IncidentParms { target = target, points = 400f, faction = raiders };

            Assert.False(worker.TryExecute(parms));
            Assert.Empty(map.lordManager.lords);
            Assert.DoesNotContain(map.mapPawns.AllPawnsSpawned, p => p.faction == raiders);
            Assert.False(target.StoryState.HasFired(worker.def));
        }

        [Fact]
        public void A_single_usable_edge_cell_is_found_however_many_unusable_ones_there_are()
        {
            // The random tries find nothing on a map like this one almost every time, so this is the shuffled
            // sweep of every edge cell that RimWorld falls back to; without it a map with one gap in its rock
            // would never be raided.
            (Faction ours, _) = Factions();
            var map = new CoreMap(Size, Size, TerrainDefOf.Soil);
            var gap = new IntVec3(Size - 1, 0, 17);
            for (int x = 0; x < Size; x++)
            {
                for (int z = 0; z < Size; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    if ((x == 0 || z == 0 || x == Size - 1 || z == Size - 1) && c != gap) Rock(map, c);
                }
            }
            Colonist(map, ours, new IntVec3(25, 0, 25));
            Rand.Current = new RandomStream(5);

            Assert.True(RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, ours, Rand.Current));
            Assert.Equal(gap, entry);
        }

        [Fact]
        public void The_entry_cell_is_a_function_of_the_seed()
        {
            (Faction ours, _) = Factions();
            CoreMap map = CutOffMap(ours, out _);

            Rand.Current = new RandomStream(99);
            Assert.True(RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 a, map, ours, Rand.Current));
            Rand.Current = new RandomStream(99);
            Assert.True(RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 b, map, ours, Rand.Current));

            Assert.Equal(a, b);
        }

        // ---- the closewalk ----

        [Fact]
        public void A_closewalk_cell_is_in_the_roots_own_area_not_merely_near_it()
        {
            // Root on the east side of a wall that cuts the map in two; the west side is inside the radius. A
            // search that only asks "standable, near" picks it about half the time.
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            for (int z = 0; z < 30; z++) Rock(map, new IntVec3(15, 0, z));
            var root = new IntVec3(16, 0, 15);

            var rand = new RandomStream(11);
            var seen = new HashSet<IntVec3>();
            for (int i = 0; i < 300; i++)
            {
                IntVec3 c = CellFinder.RandomClosewalkCellNear(root, map, IncidentWorker_RaidEnemy.ClosewalkRadius, rand);
                Assert.True(c.x >= 16, c + " is on the far side of the wall from " + root);
                Assert.True((c - root).LengthHorizontal <= IncidentWorker_RaidEnemy.ClosewalkRadius + 0.01f, c + " is outside the radius");
                seen.Add(c);
            }
            Assert.True(seen.Count > 20, "the search should spread a squad out, not stack it; saw " + seen.Count + " distinct cells");
        }

        [Fact]
        public void A_closewalk_from_an_unwalkable_root_finds_nothing_and_falls_back_to_the_root()
        {
            // Not the case a raid can reach any more (its root has been shown walkable), but the function's own
            // contract, and RimWorld's: no region at the root, no cell, the root handed back.
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            var root = new IntVec3(10, 0, 10);
            Rock(map, root);

            Assert.False(CellFinder.TryRandomClosewalkCellNear(root, map, 8, new RandomStream(1), out _));
            Assert.Equal(root, CellFinder.RandomClosewalkCellNear(root, map, 8, new RandomStream(1)));
        }

        // ---- CanReachColony ----

        [Fact]
        public void CanReachColony_is_true_where_a_colonist_can_walk_and_false_behind_the_wall_and_in_rock()
        {
            (Faction ours, _) = Factions();
            CoreMap map = CutOffMap(ours, out _);

            Assert.True(map.reachability.CanReachColony(new IntVec3(30, 0, 1)));
            Assert.False(map.reachability.CanReachColony(new IntVec3(2, 0, 2)), "open ground, but behind a wall the colony cannot cross");
            Assert.False(map.reachability.CanReachColony(new IntVec3(30, 0, Size - 1)), "inside rock");
            Assert.False(map.reachability.CanReachColony(new IntVec3(Size - 1, 0, 10)), "deep water");
        }

        [Fact]
        public void With_no_colonist_able_to_walk_the_biggest_open_area_on_the_edge_still_counts()
        {
            // RimWorld's last leg, CanReachBiggestMapEdgeRoom: a colony whose people are all down or dead (or
            // walled in) is still raided, on the open ground, rather than the raid being lost.
            (Faction ours, _) = Factions();
            CoreMap map = CutOffMap(ours, out Pawn colonist);
            colonist.health.ForceDowned = true;

            Assert.True(map.reachability.CanReachColony(new IntVec3(30, 0, 1)));
            Assert.False(map.reachability.CanReachColony(new IntVec3(2, 0, 2)), "the strip behind the wall is the smaller area");
        }

        [Fact]
        public void A_colonist_in_a_pocket_makes_the_pocket_part_of_the_colony()
        {
            // The first leg is not "the biggest area": where somebody of the colony can walk to is reachable
            // whichever area is larger, and the big open ground stays reachable through the third leg.
            (Faction ours, _) = Factions();
            CoreMap map = CutOffMap(ours, out Pawn colonist);
            colonist.DeSpawn();
            Colonist(map, ours, new IntVec3(2, 0, 25), "Hermit");

            Assert.True(map.reachability.CanReachColony(new IntVec3(2, 0, 2)));
            Assert.True(map.reachability.CanReachColony(new IntVec3(30, 0, 1)));
        }
    }
}
