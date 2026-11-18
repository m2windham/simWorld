using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.Thoughts;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// What being hurt leaves behind, through the one damage funnel (<see cref="DamageWorker.Apply"/> to
    /// <see cref="Pawn_HealthTracker.PostApplyDamage"/>): the "harmed me" opinion memory between people, and a
    /// wild animal's revenge on whoever hurt it, its herd included. Also the hunt's own give-up limit.
    /// </summary>
    public class HarmAndRevengeTests : ContentTestBase
    {
        public HarmAndRevengeTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap(int x, int z) => new CoreMap(x, z, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction NewFaction(string name, string defName)
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed(defName), name, "F_" + name);
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, Faction? faction, string name, string? weapon = null)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon)));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        /// <summary>A person whose Shooting and Animals are both 0, so its hunting stealth is 0 and the
        /// revenge chance is the race's alone (times the distance factor).</summary>
        private static Pawn SpawnClumsy(CoreMap map, IntVec3 cell, string name = "Clumsy")
        {
            Pawn p = SpawnHuman(map, cell, null, name);
            p.skills.GetSkill(SkillDefOf.Shooting)!.Level = 0;
            p.skills.GetSkill(SkillDefOf.Animals)!.Level = 0;
            return p;
        }

        private static Pawn SpawnAnimal(CoreMap map, string race, IntVec3 cell, string name)
        {
            var p = new Pawn(Def(race), name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static void Hit(Pawn victim, Pawn? instigator, float amount = 1f)
        {
            DamageDef blunt = DamageDefOf.Blunt;
            blunt.Worker.Apply(new DamageInfo(blunt, amount, 0f, instigator), victim);
        }

        private static int HarmedMeAbout(Pawn pawn, Pawn other) =>
            pawn.needs.mood!.thoughts.memories.Memories.Count(m => m.def == HarmThoughtDefOf.HarmedMe && ReferenceEquals(m.otherPawn, other));

        private static void Calm(params Pawn[] animals)
        {
            foreach (Pawn a in animals)
            {
                a.mindState.angryAt = null;
                a.mindState.angryUntilTick = -1;
            }
        }

        // ---- "harmed me" ----

        [Fact]
        public void Being_hit_by_a_neighbour_costs_fifteen_opinion_of_them_for_ten_days()
        {
            CoreMap map = NewMap(10, 10);
            Faction town = NewFaction("Town", "PlayerCivilization");
            Pawn victim = SpawnHuman(map, new IntVec3(2, 0, 2), town, "Victim");
            Pawn hitter = SpawnHuman(map, new IntVec3(3, 0, 2), town, "Hitter");
            int before = victim.relations.OpinionOf(hitter);

            Hit(victim, hitter);

            Assert.Equal(1, HarmedMeAbout(victim, hitter));
            Assert.Equal(0, HarmedMeAbout(hitter, victim));
            Assert.Equal(System.Math.Max(before - 15, -100), victim.relations.OpinionOf(hitter));
            Assert.Equal(10f, HarmThoughtDefOf.HarmedMe.durationDays);
            Assert.Equal(0f, HarmThoughtDefOf.HarmedMe.stages[0].baseMoodEffect);
        }

        [Fact]
        public void A_long_fight_weighs_more_than_one_blow_but_stops_growing()
        {
            CoreMap map = NewMap(10, 10);
            Pawn victim = SpawnHuman(map, new IntVec3(2, 0, 2), null, "Victim");
            Pawn hitter = SpawnHuman(map, new IntVec3(3, 0, 2), null, "Hitter");

            Hit(victim, hitter, 0.5f);
            int afterOne = victim.relations.OpinionOf(hitter);
            for (int i = 0; i < 9; i++) Hit(victim, hitter, 0.5f);

            Assert.Equal(3, HarmedMeAbout(victim, hitter));
            Assert.True(victim.relations.OpinionOf(hitter) < afterOne);
        }

        [Fact]
        public void An_enemy_or_an_animal_leaves_no_harmed_me_memory()
        {
            CoreMap map = NewMap(10, 10);
            Faction town = NewFaction("Town", "PlayerCivilization");
            Faction raiders = NewFaction("Raiders", "TribalCivilization");
            town.SetRelationDirect(raiders, FactionRelationKind.Hostile, -100);
            Pawn victim = SpawnHuman(map, new IntVec3(2, 0, 2), town, "Victim");
            Pawn raider = SpawnHuman(map, new IntVec3(3, 0, 2), raiders, "Raider");
            Pawn husky = SpawnAnimal(map, "Husky", new IntVec3(2, 0, 3), "Husky");

            Hit(victim, raider);
            Hit(victim, husky);
            Hit(victim, null);

            Assert.Equal(0, victim.needs.mood!.thoughts.memories.NumMemoriesOfDef(HarmThoughtDefOf.HarmedMe));
        }

        // ---- revenge on any harm ----

        /// <summary>
        /// RimWorld rolls revenge in <c>Pawn_MindState.Notify_DamageTaken</c>, on any injury a person deals.
        /// It used to be rolled only by <see cref="JobDriver_Hunt"/>, so a muffalo hurt by anyone not on a
        /// hunt job never turned.
        /// </summary>
        [Fact]
        public void A_wild_animal_hurt_by_a_person_outside_any_hunt_can_turn_on_them()
        {
            CoreMap map = NewMap(10, 10);
            Pawn person = SpawnClumsy(map, new IntVec3(2, 0, 2));
            Pawn muffalo = SpawnAnimal(map, "Muffalo", new IntVec3(3, 0, 2), "Muffalo");
            Pawn husky = SpawnAnimal(map, "Husky", new IntVec3(2, 0, 3), "Husky");
            Assert.Null(person.jobs.curJob);

            for (int i = 0; i < 40 && muffalo.mindState.angryAt == null; i++) Hit(muffalo, person, 0.1f);
            for (int i = 0; i < 40; i++) Hit(husky, person, 0.1f);

            Assert.Same(person, muffalo.mindState.angryAt);
            Assert.Null(husky.mindState.angryAt); // a husky's manhunterOnDamageChance is 0
        }

        [Fact]
        public void An_animal_hurt_by_another_animal_does_not_take_revenge()
        {
            CoreMap map = NewMap(10, 10);
            Pawn husky = SpawnAnimal(map, "Husky", new IntVec3(2, 0, 2), "Husky");
            Pawn muffalo = SpawnAnimal(map, "Muffalo", new IntVec3(3, 0, 2), "Muffalo");

            for (int i = 0; i < 60; i++) Hit(muffalo, husky, 0.1f);

            Assert.Null(muffalo.mindState.angryAt);
        }

        /// <summary>RimWorld's <c>PawnUtility.GetManhunterOnDamageChance</c> scales the race's chance by
        /// <c>LerpDoubleClamped(1, 30, 3, 1, distance)</c> (recalled): three times at point-blank, once at 30
        /// cells and beyond.</summary>
        [Fact]
        public void Revenge_is_likelier_the_closer_the_attacker_stands()
        {
            CoreMap map = NewMap(10, 10);
            Pawn person = SpawnClumsy(map, new IntVec3(2, 0, 2));
            Pawn muffalo = SpawnAnimal(map, "Muffalo", new IntVec3(3, 0, 2), "Muffalo");
            float race = muffalo.RaceProps.manhunterOnDamageChance;

            Assert.Equal(race * 3f, PawnUtility.GetManhunterOnDamageChance(muffalo, 1f, person), 4);
            Assert.Equal(race, PawnUtility.GetManhunterOnDamageChance(muffalo, 30f, person), 4);
            Assert.Equal(race, PawnUtility.GetManhunterOnDamageChance(muffalo, 60f, person), 4);
        }

        // ---- herd revenge ----

        private static (Pawn person, Pawn target, Pawn near, Pawn far, Pawn chicken) Herd(CoreMap map)
        {
            Pawn person = SpawnClumsy(map, new IntVec3(9, 0, 5));
            Pawn target = SpawnAnimal(map, "Muffalo", new IntVec3(10, 0, 5), "Target");
            Pawn near = SpawnAnimal(map, "Muffalo", new IntVec3(30, 0, 5), "Near"); // 20 cells
            Pawn far = SpawnAnimal(map, "Muffalo", new IntVec3(40, 0, 5), "Far"); // 30 cells
            Pawn chicken = SpawnAnimal(map, "Chicken", new IntVec3(12, 0, 5), "Chicken");
            return (person, target, near, far, chicken);
        }

        /// <summary>RimWorld's <c>StartManhunterBecauseOfPawnAction</c>: half the time, the provoked animal's
        /// packmates (same race and faction, within 24 cells, same district; recalled values) turn too.</summary>
        [Fact]
        public void A_provoked_animals_nearby_kin_sometimes_join_it()
        {
            CoreMap map = NewMap(50, 10);
            (Pawn person, Pawn target, Pawn near, Pawn far, Pawn chicken) = Herd(map);

            int provoked = 0, nearJoined = 0;
            for (int i = 0; i < 4000 && provoked < 60; i++)
            {
                Calm(target, near, far, chicken);
                if (!HuntUtility.TryProvokeRevenge(target, person)) continue;
                provoked++;
                if (ReferenceEquals(near.mindState.angryAt, person)) nearJoined++;
                Assert.Null(far.mindState.angryAt);
                Assert.Null(chicken.mindState.angryAt);
            }

            Assert.Equal(60, provoked);
            Assert.InRange(nearJoined, 15, 45);
        }

        [Fact]
        public void No_herd_joins_on_a_difficulty_without_big_threats()
        {
            CoreMap map = NewMap(50, 10);
            (Pawn person, Pawn target, Pawn near, Pawn _, Pawn _) = Herd(map);
            Find.Storyteller.difficulty = new DifficultyDef { defName = "TestPeaceful", allowBigThreats = false };

            int provoked = 0;
            for (int i = 0; i < 4000 && provoked < 30; i++)
            {
                Calm(target, near);
                if (!HuntUtility.TryProvokeRevenge(target, person)) continue;
                provoked++;
                Assert.Null(near.mindState.angryAt);
            }
            Assert.Equal(30, provoked);
        }

        // ---- the hunt's time limit ----

        /// <summary>RimWorld's <c>JobDriver_Hunt</c> fails its approach and cast toils once 5000 ticks have
        /// passed since the job started. This port's hunt used to carry an invented half-day expiry.</summary>
        [Fact]
        public void A_hunt_gives_up_five_thousand_ticks_after_it_started()
        {
            CoreMap map = NewMap(80, 5);
            Pawn hunter = SpawnHuman(map, new IntVec3(1, 0, 2), null, "Hunter", "Bow_Short");
            Pawn muffalo = SpawnAnimal(map, "Muffalo", new IntVec3(78, 0, 2), "Muffalo");

            hunter.jobs.StartJob(new Job(HuntingDefOf.Hunt, muffalo));
            RunTicks(2, hunter);
            Job hunt = hunter.jobs.curJob!;
            Assert.Equal(HuntingDefOf.Hunt, hunt.def);

            Find.TickManager.DebugSetTicksGame(hunt.startTick + JobDriver_Hunt.MaxHuntTicks - 1);
            RunTicks(1, hunter);
            Assert.Same(hunt, hunter.jobs.curJob);

            RunTicks(2, hunter);
            Assert.NotSame(hunt, hunter.jobs.curJob);
            Assert.False(muffalo.Dead);
            Assert.Equal(5000, JobDriver_Hunt.MaxHuntTicks);
        }
    }
}
