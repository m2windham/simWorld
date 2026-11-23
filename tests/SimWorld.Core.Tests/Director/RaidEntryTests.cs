using System.Collections.Generic;
using SimWorld.Defs;
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
    /// director.raids: where a raid walks in. RimWorld's <c>RCellFinder.TryFindRandomPawnEntryCell</c> wants a
    /// standable edge cell that reaches the colony; the port used to take any edge cell, and a squad spawned
    /// into rock stood there searching for work every tick until it froze.
    /// </summary>
    public class RaidEntryTests : ContentTestBase
    {
        public RaidEntryTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static global::SimWorld.Director.IncidentWorker_RaidEnemy NewWorker()
        {
            var def = new global::SimWorld.Director.IncidentDef
            {
                defName = "TestRaidEntry",
                category = global::SimWorld.Director.IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(global::SimWorld.Director.IncidentWorker_RaidEnemy),
            };
            return (global::SimWorld.Director.IncidentWorker_RaidEnemy)def.Worker;
        }

        /// <summary>
        /// A 30x30 map of rock with three openings: a room in the middle where the town is, a corridor from
        /// the east edge into it, and a sealed pocket on the west edge that is standable but reaches nothing.
        /// </summary>
        private static CoreMap RockMapWithOneWayIn()
        {
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            ThingDef rock = DefDatabase<ThingDef>.GetNamed("Granite");
            for (int z = 0; z < 30; z++)
            {
                for (int x = 0; x < 30; x++)
                {
                    bool room = x >= 10 && x <= 19 && z >= 10 && z <= 19;
                    bool corridor = x >= 20 && z >= 14 && z <= 16;
                    bool pocket = x <= 2 && z >= 13 && z <= 17;
                    if (room || corridor || pocket) continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(rock), new IntVec3(x, 0, z), map);
                }
            }
            return map;
        }

        [Theory]
        [InlineData(3)]
        [InlineData(41)]
        [InlineData(977)]
        public void A_raid_walks_in_where_it_can_reach_the_town(int seed)
        {
            CoreMap map = RockMapWithOneWayIn();
            var town = new IntVec3(15, 0, 15);
            GenSpawn.Spawn(NewHuman("Citizen"), town, map);

            Faction rough = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough", "F_Rough");
            var target = new global::SimWorld.Director.CivilizationTarget { Map = map };
            Rand.Current = new RandomStream(seed);

            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 200f, faction = rough };
            global::SimWorld.Director.IncidentWorker_RaidEnemy worker = NewWorker();

            Assert.True(worker.TryExecute(parms));
            IReadOnlyList<Pawn> squad = worker.LastRaidPawns!;
            Assert.NotEmpty(squad);
            foreach (Pawn raider in squad)
            {
                Assert.True(GenGrid.Standable(raider.Position, map), raider.Position + " is not standable");
                Assert.True(map.reachability.CanReachCells(raider.Position, town),
                    raider.Position + " cannot reach the town");
            }
        }
    }
}
