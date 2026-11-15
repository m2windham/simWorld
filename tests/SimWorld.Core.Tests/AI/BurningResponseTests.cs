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
    /// A pawn on fire runs about and puts itself out (RimWorld's BurningResponse).
    ///
    /// <para/>Found tracing a flashstorm over a threat-free founding band
    /// (<c>docs/perf/storyteller-pressure</c>): a citizen set alight by a strike went back to harvesting and
    /// burned until he fell, and nobody alight ever put it out.
    /// </summary>
    public class BurningResponseTests : ContentTestBase
    {
        public BurningResponseTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int size = 30) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Faction Settlers()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnCitizen(CoreMap map, IntVec3 cell, Faction faction, string name)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static bool IsBurningResponse(Job? job) =>
            job != null && (job.def == BurningJobDefOf.ExtinguishSelf || job.def == JobDefOf.GotoWander);

        [Fact]
        public void A_citizen_on_fire_does_nothing_but_run_or_put_it_out()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(10, 0, 10), Settlers(), "Alight");
            Assert.True(citizen.TryAttachFire(0.5f));

            for (int i = 0; i < 400 && citizen.GetAttachedFire() != null && !citizen.Downed; i++)
            {
                RunTicks(1, citizen);
                if (citizen.jobs.curJob != null)
                {
                    Assert.True(IsBurningResponse(citizen.jobs.curJob), "a burning citizen took up " + citizen.jobs.curJob.def.defName);
                }
            }
        }

        [Fact]
        public void Putting_yourself_out_takes_a_moment_and_then_the_fire_is_gone()
        {
            CoreMap map = NewMap();
            Pawn citizen = SpawnCitizen(map, new IntVec3(10, 0, 10), Settlers(), "Alight");
            Assert.True(citizen.TryAttachFire(0.5f));
            Fire fire = citizen.GetAttachedFire()!;

            citizen.jobs.StartJob(new Job(BurningJobDefOf.ExtinguishSelf, fire));
            RunTicks(JobDriver_ExtinguishSelf.ExtinguishTicks / 2, citizen);
            Assert.NotNull(citizen.GetAttachedFire());

            RunTicks(JobDriver_ExtinguishSelf.ExtinguishTicks, citizen);
            Assert.Null(citizen.GetAttachedFire());
            Assert.True(fire.Destroyed);
        }

        /// <summary>The roll is one in ten per stop, so the outcome is a share, not a certainty — some
        /// burn too long. Before this tier existed the share was nil.</summary>
        [Fact]
        public void Most_citizens_set_alight_put_themselves_out_and_walk_away()
        {
            CoreMap map = NewMap(60);
            Faction settlers = Settlers();
            var citizens = new List<Pawn>();
            for (int i = 0; i < 12; i++)
            {
                Pawn p = SpawnCitizen(map, new IntVec3(6 + (i % 4) * 14, 0, 8 + (i / 4) * 18), settlers, "Alight" + i);
                Assert.True(p.TryAttachFire(0.5f));
                citizens.Add(p);
            }

            for (int tick = 0; tick < 6000; tick++)
            {
                RunTicks(1, citizens.Where(c => !c.Dead).ToArray());
            }

            int walkedAway = citizens.Count(c => !c.Dead && !c.Downed && c.GetAttachedFire() == null);
            Assert.InRange(walkedAway, 7, 12);
        }
    }
}
