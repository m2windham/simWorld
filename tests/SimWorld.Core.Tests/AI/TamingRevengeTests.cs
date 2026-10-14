using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
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
    /// Who starts the fight between a citizen and a calm animal (lane: post-raid bleed). The port's taming
    /// turned a third of every failed muffalo attempt into a day-long mauling, let the next citizen try the
    /// same animal an hour later, left a freshly tamed animal angry at the person it had just been tamed by,
    /// and sent handlers up to animals that had already turned on somebody. Measured over 60 days of seed
    /// 12345: 89 grudges, every one of them set by a <c>Tame</c> job, and about 160 of the settlement's 246
    /// citizen injuries were bites from animals holding one — none from a hunt. Each test below is written to
    /// fail on that behaviour; the numbers they assert are RimWorld's (the 12-hour wait is
    /// <c>TameUtility.MinTameInterval</c>; the chance is the race's own <c>manhunterOnTameFailChance</c>) or a
    /// property, never a tuned literal.
    /// </summary>
    public class TamingRevengeTests : ContentTestBase
    {
        public TamingRevengeTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Muffalo => Def("Muffalo");

        private static ThingDef Chicken => Def("Chicken");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static WorkGiver_TameAnimals TameGiver =>
            (WorkGiver_TameAnimals)DefDatabase<WorkGiverDef>.GetNamed("TameAnimals").Worker;

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Beast")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnHandler(CoreMap map, IntVec3 cell, string name = "Handler", int animalsSkill = 0)
        {
            Pawn p = NewHuman(name);
            p.skills.GetSkill(SkillDefOf.Animals)!.Level = animalsSkill;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn TamerOf(int animalsSkill)
        {
            Pawn tamer = NewHuman("Tamer");
            tamer.skills.GetSkill(SkillDefOf.Animals)!.Level = animalsSkill;
            return tamer;
        }

        /// <summary>Runs <paramref name="body"/> with the race's manhunterOnTameFailChance set, and puts the
        /// shared def back afterwards — the defs are the fixture's, shared by every test in the collection.</summary>
        private static void WithTameFailChance(ThingDef race, float chance, System.Action body)
        {
            float original = race.race!.manhunterOnTameFailChance;
            race.race.manhunterOnTameFailChance = chance;
            try { body(); }
            finally { race.race.manhunterOnTameFailChance = original; }
        }

        // ---- the rule: revenge is the race's own number, rolled on every attempt ----

        [Fact]
        public void Failing_to_tame_a_muffalo_a_husky_or_a_chicken_never_turns_it_on_the_tamer()
        {
            // RimWorld gives all three a manhunterOnTameFailChance of 0, so a handler can fail as often as
            // they like and is never bitten for it. The port rolled 0.5 x wildness instead: for the muffalo
            // (wildness 0.6, tamed by a skill-0 handler essentially never) that was about 30% of attempts.
            foreach (ThingDef race in new[] { Muffalo, Chicken, Husky })
            {
                Pawn tamer = TamerOf(animalsSkill: 0);
                for (int seed = 1; seed <= 300; seed++)
                {
                    var animal = new Pawn(race, "Wild");
                    Rand.Current = new RandomStream(seed);
                    TameUtility.TryTame(animal, tamer);
                    Assert.Null(animal.mindState.angryAt);
                }
            }
        }

        [Fact]
        public void The_revenge_roll_is_the_races_own_chance_and_is_made_on_every_attempt_before_the_success_roll()
        {
            // RimWorld's CheckStartMentalStateBecauseRecruitAttempted runs first and ends the attempt: a
            // handler good enough to tame this husky nine times in ten is still turned on when the race says
            // so. With the chance at 1 every attempt must end in a grudge and none in a tame, however high
            // the skill; at 0 the same handler tames it.
            WithTameFailChance(Husky, 1f, () =>
            {
                Pawn tamer = TamerOf(animalsSkill: 20);
                for (int seed = 1; seed <= 50; seed++)
                {
                    var husky = new Pawn(Husky, "Wild");
                    Rand.Current = new RandomStream(seed);
                    Assert.False(TameUtility.TryTame(husky, tamer));
                    Assert.Same(tamer, husky.mindState.angryAt);
                    Assert.True(husky.mindState.angryUntilTick > Find.TickManager.TicksGame);
                    Assert.Null(husky.faction);
                }
            });

            WithTameFailChance(Husky, 0f, () =>
            {
                Pawn tamer = TamerOf(animalsSkill: 20);
                tamer.faction = new SimWorld.Factions.Faction(
                    DefDatabase<SimWorld.Factions.FactionDef>.GetNamed("OutlanderCivilization"), "Handlers", "F_Handlers");
                int tamed = 0;
                for (int seed = 1; seed <= 50; seed++)
                {
                    var husky = new Pawn(Husky, "Wild");
                    Rand.Current = new RandomStream(seed);
                    if (TameUtility.TryTame(husky, tamer)) tamed++;
                }
                Assert.True(tamed > 0);
            });
        }

        [Fact]
        public void A_race_with_a_chance_in_between_turns_on_the_tamer_about_that_often()
        {
            WithTameFailChance(Muffalo, 0.25f, () =>
            {
                Pawn tamer = TamerOf(animalsSkill: 0);
                const int trials = 2000;
                int angry = 0;
                for (int seed = 1; seed <= trials; seed++)
                {
                    var muffalo = new Pawn(Muffalo, "Wild");
                    Rand.Current = new RandomStream(seed);
                    TameUtility.TryTame(muffalo, tamer);
                    if (muffalo.mindState.angryAt != null) angry++;
                }
                // A band around the race's chance, not an equality: the roll is real.
                Assert.InRange(angry / (float)trials, 0.20f, 0.30f);
            });
        }

        // ---- the twelve-hour wait ----

        [Fact]
        public void An_attempt_wins_or_loses_starts_the_wait_and_the_work_giver_honours_it()
        {
            CoreMap map = NewMap(12, 12);
            Pawn handler = SpawnHandler(map, new IntVec3(1, 0, 1));
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(6, 0, 6));

            Assert.False(TameUtility.TriedToTameTooRecently(muffalo));
            Assert.True(TameGiver.HasJobOnThing(handler, muffalo), "a never-tried animal is offered");

            TameUtility.TryTame(muffalo, handler); // skill 0 vs wildness 0.6: it fails
            Assert.Null(muffalo.faction);

            Assert.True(TameUtility.TriedToTameTooRecently(muffalo));
            Assert.False(TameGiver.HasJobOnThing(handler, muffalo), "an animal tried a moment ago is left alone");

            int justBefore = muffalo.mindState.lastAssignedInteractTime + AnimalTuning.MinTameIntervalTicks - 1;
            Find.TickManager.DebugSetTicksGame(justBefore);
            Assert.False(TameGiver.HasJobOnThing(handler, muffalo));

            Find.TickManager.DebugSetTicksGame(justBefore + 1);
            Assert.False(TameUtility.TriedToTameTooRecently(muffalo));
            Assert.True(TameGiver.HasJobOnThing(handler, muffalo), "twelve hours on, anyone may try again");
        }

        [Fact]
        public void The_wait_is_the_number_in_the_decompile()
        {
            // Not a tuned figure: TameUtility.MinTameInterval = 30000 in the RimWorld 1.0 decompile, which
            // is twelve of this port's hours. Pinned because it is sourced; a change should be deliberate.
            Assert.Equal(30000, AnimalTuning.MinTameIntervalTicks);
            Assert.Equal(12 * GenDate.TicksPerHour, AnimalTuning.MinTameIntervalTicks);
        }

        // ---- who may be walked up to ----

        [Fact]
        public void A_handler_is_not_sent_to_an_animal_that_has_already_turned_on_somebody()
        {
            CoreMap map = NewMap(12, 12);
            Pawn handler = SpawnHandler(map, new IntVec3(1, 0, 1));
            Pawn victim = SpawnHandler(map, new IntVec3(2, 0, 1), "Victim");
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(6, 0, 6));
            Assert.True(TameGiver.HasJobOnThing(handler, muffalo));

            muffalo.mindState.angryAt = victim;
            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame + 1000;
            Assert.False(TameGiver.HasJobOnThing(handler, muffalo), "angry at someone: not a tame target");

            Find.TickManager.DebugSetTicksGame(muffalo.mindState.angryUntilTick + 1);
            Assert.True(TameGiver.HasJobOnThing(handler, muffalo), "the grudge has lapsed");

            muffalo.mindState.manhunterUntilTick = Find.TickManager.TicksGame + 1000;
            Assert.False(TameGiver.HasJobOnThing(handler, muffalo), "manhunting: not a tame target");
        }

        [Fact]
        public void A_handler_is_not_sent_to_a_downed_or_sleeping_animal()
        {
            CoreMap map = NewMap(12, 12);
            Pawn handler = SpawnHandler(map, new IntVec3(1, 0, 1));
            Pawn chicken = SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6));
            Assert.True(TameGiver.HasJobOnThing(handler, chicken));

            chicken.Asleep = true;
            Assert.False(TameGiver.HasJobOnThing(handler, chicken));
            chicken.Asleep = false;
            Assert.True(TameGiver.HasJobOnThing(handler, chicken));
        }

        // ---- success ends the grudge ----

        [Fact]
        public void A_tamed_animal_is_calm_from_the_moment_it_is_tamed()
        {
            // RimWorld's Pawn.SetFaction ends in ClearMind. The port set the faction and nothing else, so
            // a muffalo that had turned on one citizen and was then tamed by another went on mauling the
            // first as livestock — the log shows a trainer bitten fourteen times by a tamed muffalo.
            WithTameFailChance(Husky, 0f, () =>
            {
                Pawn tamer = TamerOf(animalsSkill: 20);
                tamer.faction = new SimWorld.Factions.Faction(
                    DefDatabase<SimWorld.Factions.FactionDef>.GetNamed("OutlanderCivilization"), "Handlers", "F_Handlers");
                Pawn victim = NewHuman("Victim");

                Pawn? tamed = null;
                for (int seed = 1; seed <= 50 && tamed == null; seed++)
                {
                    var husky = new Pawn(Husky, "Wild");
                    husky.mindState.angryAt = victim;
                    husky.mindState.angryUntilTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
                    husky.mindState.manhunterUntilTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
                    Rand.Current = new RandomStream(seed);
                    if (TameUtility.TryTame(husky, tamer)) tamed = husky;
                }

                Assert.NotNull(tamed);
                Assert.Null(tamed!.mindState.angryAt);
                Assert.False(HuntUtility.IsAngry(tamed));
                Assert.False(AttackTargetsUtility.IsManhunting(tamed));
                Assert.False(AttackTargetsUtility.HostileTo(tamed, victim));
            });
        }

        [Fact]
        public void A_failed_attempt_leaves_an_existing_grudge_alone()
        {
            // Only success clears the mind; a failure on an animal that is somehow still angry does not.
            Pawn tamer = TamerOf(animalsSkill: 0);
            Pawn victim = NewHuman("Victim");
            var muffalo = new Pawn(Muffalo, "Wild");
            muffalo.mindState.angryAt = victim;
            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame + 1000;
            Assert.False(TameUtility.TryTame(muffalo, tamer));
            Assert.Same(victim, muffalo.mindState.angryAt);
        }

        // ---- end to end: the settlement that tames ----

        [Fact]
        public void A_citizen_who_keeps_trying_to_tame_a_wild_muffalo_never_ends_up_in_a_fight_with_it()
        {
            // The behaviour the whole fix is for, through the real work-giver path: one unskilled handler,
            // one wild muffalo, a fed map. A skill-0 handler essentially never tames a muffalo, so the
            // attempts go on for the whole run — before the fix a grudge came up within a few of them and
            // the handler spent the rest of the run in an AttackStatic/AttackMelee exchange with a "calm"
            // animal, bitten and (in the log) down for days. Now: the attempts are twelve hours apart,
            // revenge is the race's 0, and the pair never fights.
            CoreMap map = NewMap(14, 14);
            Pawn handler = SpawnHandler(map, new IntVec3(1, 0, 1));
            handler.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Bow_Short")));
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(6, 0, 6));

            float per = Def("MealSimple").ingestible!.nutrition;
            float wanted = HuntingInitiative.NutritionWanted(null, map) + TamingInitiative.FeedCostPerHead(map) * 6f;
            Thing meals = ThingMaker.MakeThing(Def("MealSimple"));
            meals.stackCount = (int)(wanted / per) + 1;
            GenSpawn.Spawn(meals, new IntVec3(0, 0, 0), map);
            Assert.True(TamingInitiative.WantsLivestock(map), "the map is arranged so that taming is wanted");

            RunTicks(1, handler, muffalo);
            int fightTicks = 0;
            bool everAngry = false;
            int attempts = 0;
            int lastSeen = muffalo.mindState.lastAssignedInteractTime;
            for (int t = 0; t < 3 * GenDate.TicksPerDay; t++)
            {
                Find.TickManager.DoSingleTick();
                string? job = handler.jobs.curJob?.def.defName;
                if (job == "AttackStatic" || job == "AttackMelee") fightTicks++;
                if (muffalo.mindState.angryAt != null) everAngry = true;
                if (muffalo.mindState.lastAssignedInteractTime != lastSeen)
                {
                    attempts++;
                    lastSeen = muffalo.mindState.lastAssignedInteractTime;
                }
                if (muffalo.faction != null) break; // tamed: fine, and the grudge is the thing under test
            }

            Assert.False(everAngry, "a muffalo that fails to be tamed does not turn on the handler");
            Assert.Equal(0, fightTicks);
            Assert.DoesNotContain(handler.health.hediffSet.hediffs, h => h is SimWorld.Health.Hediff_Injury);
            Assert.True(attempts >= 1, "the handler really was trying");
            // Three days at one attempt per twelve hours is at most six; the old code made one every few hundred ticks.
            Assert.InRange(attempts, 1, 7);
        }

        // ---- save / load ----

        [Fact]
        public void The_time_of_the_last_attempt_survives_a_save_and_load()
        {
            CoreMap map = NewMap(10, 10);
            Pawn handler = SpawnHandler(map, new IntVec3(1, 0, 1));
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(5, 0, 5));
            Find.TickManager.DebugSetTicksGame(40000);
            TameUtility.TryTame(muffalo, handler);
            int stamped = muffalo.mindState.lastAssignedInteractTime;
            Assert.Equal(40000, stamped);

            var root = new SaveRoot { map = map };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            SaveRoot loadedRoot = Scribe.Load<SaveRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedMuffalo = loadedRoot.map!.mapPawns.AllPawnsSpawned.Single(p => p.RaceProps.Animal);
            Assert.Equal(stamped, loadedMuffalo.mindState.lastAssignedInteractTime);
            Assert.True(TameUtility.TriedToTameTooRecently(loadedMuffalo));
        }

        private sealed class SaveRoot : IExposable
        {
            public CoreMap? map;

            public void ExposeData()
            {
                CoreMap? m = map;
                Scribe_Deep.Look(ref m, "map");
                map = m;
            }
        }
    }
}
