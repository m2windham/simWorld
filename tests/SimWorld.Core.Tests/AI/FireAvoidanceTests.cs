using System.Collections.Generic;
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
    /// Pawns keep out of fire, RimWorld's way: a fire on a cell, and beside it, is a high <i>perceived</i>
    /// path cost (<c>PathGrid.CalculatedCostAt</c> with <c>perceivedStatic</c>), so routes go round it and a
    /// wanderer — including a burning pawn running about — does not choose to stop in or next to one
    /// (<c>RCellFinder.CanWanderToCell</c>'s perceived-cost test). Walking a cell still costs only what it
    /// really costs, so a pawn already in a fire is not slowed down inside it.
    /// <para/>
    /// Found tracing a flashstorm over a threat-free founding band (<c>docs/perf/storyteller-pressure</c>):
    /// nearly every burn on every citizen was taken mid-wander on a cell already alight, and citizens who had
    /// put themselves out ran straight back into the field and caught again.
    /// </summary>
    public class FireAvoidanceTests : ContentTestBase
    {
        public FireAvoidanceTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size = 30) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Fire Ignite(CoreMap map, IntVec3 cell)
        {
            // Floor-level fuel, so the cell stays walkable: a fire is something to route round, not a wall.
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog")), cell, map);
            Assert.True(FireUtility.TryStartFireIn(cell, map, 0.5f));
            return FireUtility.StaticFireIn(cell, map)!;
        }

        private Pawn SpawnCitizen(CoreMap map, IntVec3 cell)
        {
            var faction = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(faction);
            Pawn p = NewHuman("Walker");
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        [Fact]
        public void A_fire_is_perceived_on_its_cell_and_beside_it_but_walking_costs_the_same()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(10, 0, 10);
            var beside = new IntVec3(11, 0, 10);
            var away = new IntVec3(13, 0, 10);
            int besideBefore = map.pathGrid.PerceivedPathCostAt(beside);
            int awayBefore = map.pathGrid.PerceivedPathCostAt(away);

            Fire fire = Ignite(map, cell);
            int realOnCell = map.pathGrid.CalculatedCostAt(cell);

            Assert.True(map.pathGrid.PerceivedPathCostAt(cell) > realOnCell + JobGiver_WanderAnywhere.MaxPerceivedPathCostToWander);
            Assert.True(map.pathGrid.PerceivedPathCostAt(beside) > besideBefore + JobGiver_WanderAnywhere.MaxPerceivedPathCostToWander);
            Assert.True(map.pathGrid.PerceivedPathCostAt(cell) > map.pathGrid.PerceivedPathCostAt(beside), "on the fire is worse than beside it");
            Assert.Equal(awayBefore, map.pathGrid.PerceivedPathCostAt(away));
            Assert.True(map.pathGrid.Walkable(cell), "a fire is avoided, never a wall");

            fire.Destroy();
            Assert.Equal(besideBefore, map.pathGrid.PerceivedPathCostAt(beside));
        }

        [Fact]
        public void A_fire_riding_a_pawn_is_not_a_fire_on_the_ground()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnCitizen(map, new IntVec3(10, 0, 10));
            int before = map.pathGrid.PerceivedPathCostAt(new IntVec3(11, 0, 10));
            Assert.True(p.TryAttachFire(0.5f));
            Assert.Equal(before, map.pathGrid.PerceivedPathCostAt(new IntVec3(11, 0, 10)));
        }

        [Fact]
        public void A_path_goes_round_a_fire_when_there_is_a_way_round()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnCitizen(map, new IntVec3(5, 0, 10));
            for (int z = 8; z <= 12; z++) Ignite(map, new IntVec3(10, 0, z));

            PawnPath path = map.pathFinder.FindPath(p, p.Position, new IntVec3(15, 0, 10), PathEndMode.OnCell);
            Assert.True(path.Found);
            for (int i = 0; i < path.NodesLeftCount; i++)
            {
                Assert.Null(FireUtility.StaticFireIn(path.Peek(i), map));
            }
        }

        [Fact]
        public void A_wanderer_beside_a_fire_does_not_choose_to_stop_in_or_beside_it()
        {
            CoreMap map = NewMap();
            var centre = new IntVec3(15, 0, 15);
            // A burning strip two cells east of the pawn, most of the wander disc's east half.
            for (int x = 17; x <= 19; x++)
            {
                for (int z = 12; z <= 18; z++) Ignite(map, new IntVec3(x, 0, z));
            }
            Pawn p = SpawnCitizen(map, centre);
            var giver = new JobGiver_WanderAnywhere();

            int bad = 0;
            for (int i = 0; i < 200; i++)
            {
                ThinkResult r = giver.TryIssueJobPackage(p);
                Assert.True(r.IsValid);
                IntVec3 dest = r.Job!.targetA.Cell;
                if (map.pathGrid.PerceivedPathCostAt(dest) > JobGiver_WanderAnywhere.MaxPerceivedPathCostToWander) bad++;
            }
            Assert.True(bad <= 2, $"{bad} of 200 wander destinations were in or beside the fire");
        }

        [Fact]
        public void A_burning_bed_is_no_bed()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnCitizen(map, new IntVec3(5, 0, 5));
            Thing bed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bed"), DefDatabase<ThingDef>.GetNamed("WoodLog"));
            GenSpawn.Spawn(bed, new IntVec3(8, 0, 8), map);
            Assert.Same(bed, RestUtility.FindBedFor(p));

            Assert.True(FireUtility.TryStartFireIn(bed.Position, map, 0.5f));
            Assert.Null(RestUtility.FindBedFor(p));
        }
    }
}
