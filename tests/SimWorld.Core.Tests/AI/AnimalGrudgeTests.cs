using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
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
    /// A fight with an animal ends when the animal stops being an enemy — and being tamed is one of the ways
    /// it stops.
    ///
    /// <para/>Found by <c>tools/bench --suite storyteller --threats false</c>: with every storyteller threat
    /// switched off, a founding band of twenty took 118 wounds in twelve days, 34 of them from animals that
    /// carried the settlement's own faction (<c>docs/perf/storyteller-pressure</c>). An animal angry at one
    /// handler was tamed by the next; taming changed its faction and nothing else, so the grudge ran on and
    /// so did both sides' attack jobs, which never asked again whether their target was still an enemy.
    /// </summary>
    public class AnimalGrudgeTests : ContentTestBase
    {
        public AnimalGrudgeTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static CoreMap NewMap() => new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction Settlers()
        {
            var f = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            Find.FactionManager.Add(f);
            return f;
        }

        private static Pawn SpawnCitizen(CoreMap map, IntVec3 cell, Faction faction, string name, string? weapon = null)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            if (weapon != null) p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def(weapon)));
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnMuffalo(CoreMap map, IntVec3 cell)
        {
            var p = new Pawn(Def("Muffalo"), "Muffalo");
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static void Anger(Pawn animal, Pawn at)
        {
            animal.mindState.angryAt = at;
            animal.mindState.angryUntilTick = Find.TickManager.TicksGame + AnimalTuning.TameFailAngerDurationTicks;
        }

        private static bool IsAttackOn(Job? job, Pawn target) =>
            job != null
            && (job.def == CombatAIDefOf.AttackMelee || job.def == CombatAIDefOf.AttackStatic)
            && ReferenceEquals(job.GetTarget(TargetIndex.A).Thing, target);

        /// <summary>Keeps trying until one attempt lands; the roll is real, so a bounded search stands in for
        /// assuming any one seed. A failure may re-anger the animal at the handler, which only makes the
        /// case under test harder.</summary>
        private static void TameEventually(Pawn animal, Pawn handler)
        {
            for (int i = 0; i < 400 && animal.faction == null; i++) TameUtility.TryTame(animal, handler);
            Assert.Same(handler.faction, animal.faction);
        }

        [Fact]
        public void Taming_an_animal_that_had_turned_on_somebody_ends_its_grudge_and_its_attack()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn victim = SpawnCitizen(map, new IntVec3(5, 0, 5), settlers, "Victim");
            Pawn handler = SpawnCitizen(map, new IntVec3(7, 0, 5), settlers, "Handler");
            handler.skills.GetSkill(SkillDefOf.Animals)!.Level = 20;
            Pawn muffalo = SpawnMuffalo(map, new IntVec3(6, 0, 5));

            Anger(muffalo, victim);
            muffalo.jobs.TryFindAndStartJob();
            Assert.True(IsAttackOn(muffalo.jobs.curJob, victim), "an animal with a grudge goes for the pawn it names");

            TameEventually(muffalo, handler);

            Assert.Null(muffalo.mindState.angryAt);
            Assert.False(HuntUtility.IsAngry(muffalo));
            Assert.False(AttackTargetsUtility.HostileTo(muffalo, victim));
            Assert.False(AttackTargetsUtility.HostileTo(muffalo, handler));
            Assert.False(IsAttackOn(muffalo.jobs.curJob, victim), "the settlement's own animal must not go on attacking its people");
            Assert.False(IsAttackOn(muffalo.jobs.curJob, handler));
        }

        [Fact]
        public void A_citizen_stops_attacking_an_animal_that_is_no_longer_an_enemy()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn citizen = SpawnCitizen(map, new IntVec3(5, 0, 5), settlers, "Citizen", weapon: "MeleeWeapon_Knife");
            Pawn handler = SpawnCitizen(map, new IntVec3(7, 0, 5), settlers, "Handler");
            handler.skills.GetSkill(SkillDefOf.Animals)!.Level = 20;
            Pawn muffalo = SpawnMuffalo(map, new IntVec3(6, 0, 5));
            Anger(muffalo, citizen);

            citizen.jobs.StartJob(new Job(CombatAIDefOf.AttackMelee, muffalo));
            RunTicks(3, citizen);
            Assert.True(IsAttackOn(citizen.jobs.curJob, muffalo), "while the grudge runs the fight is real");

            TameEventually(muffalo, handler);
            RunTicks(3, citizen);

            Assert.False(IsAttackOn(citizen.jobs.curJob, muffalo), "nobody goes on stabbing the settlement's new livestock");
        }

        [Fact]
        public void A_grudge_that_runs_out_ends_the_fight_on_both_sides()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn citizen = SpawnCitizen(map, new IntVec3(5, 0, 5), settlers, "Citizen", weapon: "MeleeWeapon_Knife");
            Pawn muffalo = SpawnMuffalo(map, new IntVec3(6, 0, 5));
            Anger(muffalo, citizen);

            citizen.jobs.StartJob(new Job(CombatAIDefOf.AttackMelee, muffalo));
            muffalo.jobs.TryFindAndStartJob();
            Assert.True(IsAttackOn(muffalo.jobs.curJob, citizen));

            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame;
            RunTicks(3, citizen, muffalo);

            Assert.False(IsAttackOn(citizen.jobs.curJob, muffalo));
            Assert.False(IsAttackOn(muffalo.jobs.curJob, citizen));
        }

        [Fact]
        public void An_animal_that_has_turned_on_somebody_is_not_offered_for_taming()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn victim = SpawnCitizen(map, new IntVec3(3, 0, 3), settlers, "Victim");
            Pawn handler = SpawnCitizen(map, new IntVec3(7, 0, 5), settlers, "Handler");
            Pawn muffalo = SpawnMuffalo(map, new IntVec3(6, 0, 5));
            var giver = new WorkGiver_TameAnimals();

            Assert.True(giver.HasJobOnThing(handler, muffalo), "a calm wild animal is taming work");

            Anger(muffalo, victim);
            Assert.False(giver.HasJobOnThing(handler, muffalo));

            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame;
            Assert.True(giver.HasJobOnThing(handler, muffalo), "and is again once it has calmed down");
        }

        [Fact]
        public void A_hunt_breaks_off_when_the_prey_becomes_somebodys_animal()
        {
            CoreMap map = NewMap();
            Faction settlers = Settlers();
            Pawn hunter = SpawnCitizen(map, new IntVec3(1, 0, 1), settlers, "Hunter", weapon: "Bow_Short");
            Pawn muffalo = SpawnMuffalo(map, new IntVec3(12, 0, 12));

            hunter.jobs.StartJob(new Job(HuntingDefOf.Hunt, muffalo));
            RunTicks(3, hunter);
            Assert.Equal(HuntingDefOf.Hunt, hunter.jobs.curJob?.def);

            muffalo.faction = settlers;
            RunTicks(3, hunter);

            Assert.False(
                hunter.jobs.curJob?.def == HuntingDefOf.Hunt && ReferenceEquals(hunter.jobs.curJob.GetTarget(TargetIndex.A).Thing, muffalo),
                "a hunter does not go on shooting an animal the settlement now owns");
        }
    }
}
