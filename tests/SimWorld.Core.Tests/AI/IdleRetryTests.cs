using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
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
    /// A pawn whose whole think tree comes back empty stands a while before asking again, as RimWorld's
    /// <c>JobGiver_IdleError</c> wait makes it. Asking every tick ran every work scan on the map every tick for
    /// each such pawn.
    /// </summary>
    public class IdleRetryTests : ContentTestBase
    {
        public IdleRetryTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>A pawn at the centre of a 5x5 soil map, every other cell rock: nowhere to wander.</summary>
        private static (CoreMap map, Pawn pawn, List<Thing> walls) WalledIn()
        {
            var map = new CoreMap(5, 5, TerrainDefOf.Soil);
            ThingDef rock = DefDatabase<ThingDef>.GetNamed("Granite");
            var walls = new List<Thing>();
            var centre = new IntVec3(2, 0, 2);
            for (int z = 0; z < 5; z++)
            {
                for (int x = 0; x < 5; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (c == centre) continue;
                    Thing t = ThingMaker.MakeThing(rock);
                    GenSpawn.Spawn(t, c, map);
                    walls.Add(t);
                }
            }
            Pawn pawn = NewHuman("Walled");
            GenSpawn.Spawn(pawn, centre, map);
            return (map, pawn, walls);
        }

        [Fact]
        public void A_pawn_with_nothing_to_do_waits_before_asking_again()
        {
            (_, Pawn pawn, List<Thing> walls) = WalledIn();
            RunTicks(2, pawn);
            Assert.Null(pawn.jobs.curJob);

            foreach (Thing w in walls) w.Destroy();

            RunTicks(Pawn_JobTracker.IdleRetryTicks / 2, pawn);
            Assert.Null(pawn.jobs.curJob);

            RunTicks(Pawn_JobTracker.IdleRetryTicks, pawn);
            Assert.NotNull(pawn.jobs.curJob);
        }

        [Fact]
        public void The_idle_wait_survives_a_save_round_trip()
        {
            (_, Pawn pawn, _) = WalledIn();
            RunTicks(2, pawn);

            int until = pawn.jobs.IdleUntilTick;
            Assert.True(until > Find.TickManager.TicksGame, "expected the walled-in pawn to be waiting");

            string xml = Scribe.SaveToString(pawn, "pawn");
            Pawn loaded = Scribe.Load<Pawn>(xml, "pawn", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal(until, loaded.jobs.IdleUntilTick);
        }
    }
}
