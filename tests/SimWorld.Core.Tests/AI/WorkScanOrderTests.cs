using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <see cref="WorkGiverScanUtility.TryFindJobOnScanner"/> picks what RimWorld's list-order nearest scan picks
    /// (the nearest candidate the giver accepts, the earliest listed on a tie) but asks the giver about the
    /// candidates nearest first, so a giver offering the whole map is not asked about all of it.
    /// </summary>
    public class WorkScanOrderTests : ContentTestBase
    {
        public WorkScanOrderTests(CoreContentFixture content) : base(content)
        {
        }

        private sealed class ListScanner : WorkGiver_Scanner
        {
            public readonly List<Thing> Offered = new List<Thing>();
            public readonly HashSet<Thing> Refused = new HashSet<Thing>();
            public readonly List<Thing> Asked = new List<Thing>();

            public ListScanner() => def = new WorkGiverDef { defName = "TestListScanner" };

            public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => Offered;

            public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
            {
                Asked.Add(thing);
                return !Refused.Contains(thing);
            }

            public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Mine, thing);
        }

        private static Thing Wall(CoreMap map, int x, int z) =>
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), new IntVec3(x, 0, z), map);

        [Fact]
        public void The_scan_takes_the_nearest_accepted_candidate_and_asks_about_nothing_farther()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(0, 0, 0), map);
            var scanner = new ListScanner();
            Thing far = Wall(map, 9, 0), mid = Wall(map, 6, 0), near = Wall(map, 3, 0), nearest = Wall(map, 1, 0);
            scanner.Offered.AddRange(new[] { far, mid, near, nearest }); // listed far to near
            scanner.Refused.Add(nearest);

            Job? job = WorkGiverScanUtility.TryFindJobOnScanner(pawn, scanner);

            Assert.Same(near, job!.targetA.Thing);
            Assert.Equal(new[] { nearest, near }, scanner.Asked);
        }

        [Fact]
        public void On_a_tie_the_scan_takes_the_earliest_listed_candidate()
        {
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            Pawn pawn = NewHuman();
            GenSpawn.Spawn(pawn, new IntVec3(5, 0, 5), map);
            var scanner = new ListScanner();
            Thing east = Wall(map, 8, 5), north = Wall(map, 5, 8), west = Wall(map, 2, 5);
            scanner.Offered.AddRange(new[] { north, west, east });

            Job? job = WorkGiverScanUtility.TryFindJobOnScanner(pawn, scanner);

            Assert.Same(north, job!.targetA.Thing);
        }
    }
}
