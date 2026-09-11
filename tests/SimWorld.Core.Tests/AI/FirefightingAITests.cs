using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
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
    /// <c>FightFires</c> wired to a real scanner (system: fire). The def has carried
    /// <c>emergency=true</c> since the work economy landed but had no worker and nothing it could target —
    /// there was no <c>Fire</c> class in the codebase at all. Same end-to-end shape as
    /// <c>RepairAITests</c>: the job comes from the giver through the think tree, not from a test calling the
    /// driver.
    /// </summary>
    public class FirefightingAITests : ContentTestBase
    {
        public FirefightingAITests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Firefighter")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A wooden wall with a fire on it — the shape an actual settlement fire takes.</summary>
        private static Fire LightAWallOnFire(CoreMap map, IntVec3 cell, float fireSize = 1f)
        {
            Thing wall = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(wall, cell, map);
            Assert.True(FireUtility.TryStartFireIn(cell, map, fireSize));
            return (Fire)FireUtility.AllFires(map).Single(f => f.Position == cell);
        }

        // ---- content ----

        [Fact]
        public void FightFires_is_wired_to_a_real_worker_and_its_job_exists()
        {
            Assert.Empty(Content.Result.Errors);
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamed("FightFires");
            Assert.IsType<WorkGiver_FightFires>(giver.Worker);
            Assert.NotNull(FireJobDefOf.BeatFire);
            Assert.Equal(typeof(JobDriver_BeatFire), FireJobDefOf.BeatFire.driverClass);
        }

        // ---- the predicate ----

        [Fact]
        public void A_fire_is_firefighting_work_and_the_thing_it_is_burning_is_not()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Fire fire = LightAWallOnFire(map, new IntVec3(5, 0, 5));
            Thing wall = map.edificeGrid[new IntVec3(5, 0, 5)]!;

            Assert.True(WorkGiver_FightFires.IsFireToFight(citizen, fire));
            Assert.False(WorkGiver_FightFires.IsFireToFight(citizen, wall));
        }

        [Fact]
        public void A_fire_riding_a_hostile_pawn_is_not_our_problem()
        {
            CoreMap map = NewMap(10, 10);
            FactionDef anyFaction = DefDatabase<FactionDef>.AllDefsListForReading.First();
            var ours = new Faction(anyFaction, "Ours", "Faction_Ours");
            var theirs = new Faction(anyFaction, "Theirs", "Faction_Theirs");
            ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);

            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1));
            citizen.faction = ours;
            Pawn raider = SpawnHuman(map, new IntVec3(6, 0, 6), "Raider");
            raider.faction = theirs;
            raider.TryAttachFire(Fire.MinFireSize);
            var onRaider = (Fire)FireUtility.AllFires(map).Single();

            Assert.False(WorkGiver_FightFires.IsFireToFight(citizen, onRaider));

            // The same fire on one of our own is work.
            raider.faction = ours;
            Assert.True(WorkGiver_FightFires.IsFireToFight(citizen, onRaider));
        }

        // ---- end to end ----

        [Fact]
        public void A_citizen_walks_to_a_fire_and_beats_it_out()
        {
            CoreMap map = NewMap(12, 12);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            Fire fire = LightAWallOnFire(map, new IntVec3(8, 0, 8));

            RunTicks(4000, citizen);

            Assert.True(fire.Destroyed, "The fire should have been beaten out.");
            Assert.Empty(FireUtility.AllFires(map));
        }

        [Fact]
        public void A_bigger_fire_takes_longer_to_put_out_than_a_small_one()
        {
            int TicksToExtinguish(float fireSize)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(7);
                Pawn.ResetThingIdCounter();

                CoreMap map = NewMap(8, 8);
                Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
                Fire fire = LightAWallOnFire(map, new IntVec3(1, 0, 0), fireSize);
                for (int i = 0; i < 6000; i++)
                {
                    Find.TickManager.DoSingleTick();
                    if (!Find.TickManager.TickListFor(TickerType.Normal)!.Contains(citizen))
                    {
                        Find.TickManager.RegisterAllTickabilityFor(citizen);
                    }
                    if (fire.Destroyed) return i;
                }
                return int.MaxValue;
            }

            int small = TicksToExtinguish(Fire.MinFireSize);
            int big = TicksToExtinguish(Fire.MaxFireSize);

            Assert.True(small < int.MaxValue && big < int.MaxValue, "Both fires should go out eventually.");
            Assert.True(big > small, $"A bigger fire should take more beating: {big} vs {small} ticks.");
        }

        [Fact]
        public void Two_citizens_do_not_both_claim_the_only_fire()
        {
            CoreMap map = NewMap(10, 10);
            Pawn a = SpawnHuman(map, new IntVec3(0, 0, 0), "A");
            Pawn b = SpawnHuman(map, new IntVec3(9, 0, 9), "B");
            Fire fire = LightAWallOnFire(map, new IntVec3(5, 0, 5));

            RunTicks(200, a, b);

            bool aHasIt = map.reservationManager.IsReservedBy(a, fire);
            bool bHasIt = map.reservationManager.IsReservedBy(b, fire);
            Assert.True(aHasIt ^ bHasIt, "Exactly one of the two should hold the only fire's reservation.");
        }

        // ---- ordering: firefighting is emergency work ----

        [Fact]
        public void FightFires_leads_the_emergency_giver_list_and_no_ordinary_work_comes_before_it()
        {
            Pawn p = NewHuman();
            Assert.Equal("FightFires", p.workSettings.WorkGiversInOrderEmergency[0].defName);
            Assert.DoesNotContain(p.workSettings.WorkGiversInOrderNormal, g => g.defName == "FightFires");
        }

        /// <summary>
        /// The behavioural half of the ordering claim: a citizen with ordinary work right under their nose
        /// and a fire on the far side of the map goes to the fire. Emergency givers are tried to exhaustion
        /// before normal ones, so distance never gets a vote.
        /// </summary>
        [Fact]
        public void A_citizen_drops_ordinary_work_for_a_fire_across_the_map()
        {
            CoreMap map = NewMap(14, 14);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));

            // Repair work one step away.
            Thing damaged = ThingMaker.MakeThing(Def("Wall"));
            GenSpawn.Spawn(damaged, new IntVec3(1, 0, 0), map);
            damaged.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 60f));

            Fire fire = LightAWallOnFire(map, new IntVec3(12, 0, 12));

            RunTicks(20, citizen);

            Assert.NotNull(citizen.jobs.curJob);
            Assert.Equal(FireJobDefOf.BeatFire, citizen.jobs.curJob!.def);
            Assert.Same(fire, citizen.jobs.curJob.GetTarget(TargetIndex.A).Thing);
        }

        /// <summary>
        /// RimWorld's <c>t.IsBurning()</c> guard on <see cref="WorkGiver_Repair"/>, which that class's own
        /// doc recorded as having no counterpart here because nothing in the codebase could burn. It has one
        /// now: a burning wall is firefighting work, not repair work, until the fire is out.
        /// </summary>
        [Fact]
        public void A_burning_building_is_not_repair_work_until_the_fire_is_out()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            var cell = new IntVec3(5, 0, 5);
            Fire fire = LightAWallOnFire(map, cell);
            Thing wall = map.edificeGrid[cell]!;
            wall.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 60f));

            var repair = new WorkGiver_Repair { def = DefDatabase<WorkGiverDef>.GetNamed("Repair") };
            Assert.True(WorkGiver_Repair.IsRepairable(wall), "The wall is damaged, so the predicate itself still says yes.");
            Assert.False(repair.HasJobOnThing(citizen, wall), "But nobody repairs a building while it is on fire.");

            fire.Destroy();
            Assert.True(repair.HasJobOnThing(citizen, wall));
        }

        [Fact]
        public void With_no_fire_on_the_map_the_giver_skips_without_scanning()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = SpawnHuman(map, new IntVec3(0, 0, 0));
            var giver = new WorkGiver_FightFires { def = DefDatabase<WorkGiverDef>.GetNamed("FightFires") };

            Assert.True(giver.ShouldSkip(citizen));

            LightAWallOnFire(map, new IntVec3(5, 0, 5));
            Assert.False(giver.ShouldSkip(citizen));
        }
    }
}
