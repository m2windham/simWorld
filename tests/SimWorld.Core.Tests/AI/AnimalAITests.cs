using System.Collections.Generic;
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
    /// <summary>The animal think tree, taming and the flee job giver (system: ai.animals).</summary>
    public class AnimalAITests : ContentTestBase
    {
        public AnimalAITests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Muffalo => DefDatabase<ThingDef>.GetNamed("Muffalo");
        private static ThingDef Chicken => DefDatabase<ThingDef>.GetNamed("Chicken");

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), name, "F_" + name);

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Animal")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Human")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // ---- content ----

        [Fact]
        public void Animal_content_loads_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ThinkTreeDefOf.Animal);
            Assert.NotNull(ThinkTreeDefOf.Animal.thinkRoot);
            Assert.IsType<ThinkNode_Priority>(ThinkTreeDefOf.Animal.thinkRoot);
            Assert.NotNull(JobDefOf.Tame);
            Assert.NotNull(JobDefOf.Train);
        }

        [Fact]
        public void Animal_think_tree_is_a_real_second_tree_not_humanlike_with_branches_skipped()
        {
            Assert.NotSame(ThinkTreeDefOf.Humanlike, ThinkTreeDefOf.Animal);

            // The combat tier (system 9: AI — combat) sits between the angry tier and fleeing: a tamed animal
            // defends its faction rather than running, and a wild one finds nothing hostile to it and still
            // flees on the next tier down.
            var root = (ThinkNode_Priority)ThinkTreeDefOf.Animal.thinkRoot;
            Assert.IsType<ThinkNode_ConditionalAngryAtHandler>(root.subNodes[0]);
            Assert.IsType<JobGiver_AIFightEnemies>(root.subNodes[1]);
            Assert.IsType<JobGiver_AnimalFlee>(root.subNodes[2]);
            Assert.IsType<ThinkNode_ConditionalHungry>(root.subNodes[3]);
            Assert.IsType<ThinkNode_ConditionalTired>(root.subNodes[4]);
            Assert.IsType<JobGiver_WanderAnywhere>(root.subNodes[5]);

            // No colonist-work tiers at all: an animal never does routine work or takes a directed order.
            Assert.DoesNotContain(root.subNodes, n => n is JobGiver_Work || n is JobGiver_DirectedOrder || n is global::SimWorld.AI.JobGiver_Edicts);
        }

        [Fact]
        public void A_pawns_race_picks_its_think_tree()
        {
            // Separate maps: on a shared one a jobless human would rather tame a nearby wild husky (real
            // WorkGiver_TameAnimals behaviour, exercised by its own tests below) than wander, which would
            // muddy what this test is actually checking — that the tree itself is selected per race.
            Pawn human = SpawnHuman(NewMap(6, 6), new IntVec3(1, 0, 1));
            Pawn husky = SpawnAnimal(NewMap(6, 6), Husky, new IntVec3(4, 0, 4));

            // Both idle with nothing else to do: each should fall through to its own tree's wander tier —
            // proof the tree is actually selected per pawn rather than shared or absent.
            human.jobs.TryFindAndStartJob();
            husky.jobs.TryFindAndStartJob();
            Assert.NotNull(human.jobs.curJob);
            Assert.NotNull(husky.jobs.curJob);
            Assert.Equal(JobDefOf.GotoWander, human.jobs.curJob!.def);
            Assert.Equal(JobDefOf.GotoWander, husky.jobs.curJob!.def);
        }

        [Fact]
        public void Hungry_animal_eats_via_the_shared_JobGiver_GetFood()
        {
            CoreMap map = NewMap(6, 6);
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(1, 0, 1));
            husky.needs.food!.CurLevel = 0f;
            Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Meat_Generic"));
            GenSpawn.Spawn(food, new IntVec3(4, 0, 4), map);

            husky.jobs.TryFindAndStartJob();
            Assert.Equal(JobDefOf.Ingest, husky.jobs.curJob!.def);
        }

        // ---- JobGiver_AnimalFlee ----

        [Fact]
        public void Wild_wildness_appropriate_animal_flees_a_nearby_human()
        {
            CoreMap map = NewMap(20, 20);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(10, 0, 10)); // wildness 0.6, no faction: wild.
            SpawnHuman(map, new IntVec3(11, 0, 10));

            var giver = new JobGiver_AnimalFlee();
            ThinkResult result = giver.TryIssueJobPackage(muffalo);

            Assert.True(result.IsValid);
            Assert.Equal(JobDefOf.GotoWander, result.Job!.def);
            IntVec3 dest = result.Job.GetTarget(TargetIndex.A).Cell;
            int distBefore = (muffalo.Position - new IntVec3(11, 0, 10)).LengthHorizontalSquared;
            int distAfter = (dest - new IntVec3(11, 0, 10)).LengthHorizontalSquared;
            Assert.True(distAfter > distBefore);
        }

        [Fact]
        public void A_tamed_animal_never_flees_its_own_colonists()
        {
            CoreMap map = NewMap(20, 20);
            Faction f = NewFaction("Colony");
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(10, 0, 10));
            muffalo.faction = f;
            SpawnHuman(map, new IntVec3(11, 0, 10));

            Assert.False(JobGiver_AnimalFlee.ShouldFlee(muffalo));
            Assert.False(new JobGiver_AnimalFlee().TryIssueJobPackage(muffalo).IsValid);
        }

        [Fact]
        public void A_barely_wild_animal_below_the_flee_threshold_does_not_flee()
        {
            CoreMap map = NewMap(20, 20);
            // Husky's wildness (0.3) sits under AnimalTuning.FleeWildnessThreshold (0.4).
            Pawn husky = SpawnAnimal(map, Husky, new IntVec3(10, 0, 10));
            SpawnHuman(map, new IntVec3(11, 0, 10));

            Assert.False(JobGiver_AnimalFlee.ShouldFlee(husky));
        }

        // ---- TameUtility ----

        [Fact]
        public void TameChance_trend_a_better_handler_tames_more_and_a_wilder_animal_resists_more()
        {
            float lowSkillChance = TameUtility.TameChance(wildness: 0.5f, animalsSkill: 0);
            float highSkillChance = TameUtility.TameChance(wildness: 0.5f, animalsSkill: 20);
            Assert.True(highSkillChance > lowSkillChance);

            float tameWildnessChance = TameUtility.TameChance(wildness: 0.1f, animalsSkill: 5);
            float wildWildnessChance = TameUtility.TameChance(wildness: 0.9f, animalsSkill: 5);
            Assert.True(tameWildnessChance > wildWildnessChance);

            // Always a probability, whatever the inputs.
            Assert.InRange(TameUtility.TameChance(wildness: 1f, animalsSkill: 0), 0f, 1f);
            Assert.InRange(TameUtility.TameChance(wildness: 0f, animalsSkill: 20), 0f, 1f);
        }

        [Fact]
        public void TryTame_measured_success_rate_rises_with_handler_skill()
        {
            const int trials = 400;
            int successesLowSkill = CountTameSuccesses(skill: 0, seedBase: 1000, trials);
            int successesHighSkill = CountTameSuccesses(skill: 18, seedBase: 2000, trials);
            Assert.True(successesHighSkill > successesLowSkill,
                $"expected a level-18 handler to tame more often than a level-0 one over {trials} trials " +
                $"(got {successesHighSkill} vs {successesLowSkill})");
        }

        [Fact]
        public void TryTame_measured_success_rate_falls_with_animal_wildness()
        {
            const int trials = 400;
            int successesTame = CountTameSuccessesByRace(Husky, skill: 10, seedBase: 3000, trials); // wildness 0.3
            int successesWild = CountTameSuccessesByRace(Muffalo, skill: 10, seedBase: 4000, trials); // wildness 0.6
            Assert.True(successesTame > successesWild,
                $"expected the tamer race to tame more often than the wilder one over {trials} trials " +
                $"(got {successesTame} vs {successesWild})");
        }

        [Fact]
        public void TryTame_success_joins_the_tamers_faction_and_maxes_tameness()
        {
            Faction f = NewFaction("Handlers");
            Pawn tamer = NewHuman("Handler");
            tamer.faction = f;
            tamer.skills.GetSkill(SkillDefOf.Animals)!.Level = 20;

            // A high-skill handler against a low-wildness Husky (chance well above even odds) — search a
            // bounded number of seeds for a success rather than assume any one roll, since the roll is real.
            Pawn? husky = null;
            for (int seed = 1; seed <= 50 && husky == null; seed++)
            {
                Pawn candidate = new Pawn(Husky, "Rex");
                Rand.Current = new RandomStream(seed);
                if (TameUtility.TryTame(candidate, tamer)) husky = candidate;
            }

            Assert.NotNull(husky);
            Assert.Same(f, husky!.faction);
            Assert.Equal(1f, husky.mindState.tameness);
        }

        [Fact]
        public void Failed_taming_can_turn_the_animal_against_the_tamer_but_not_always()
        {
            Pawn tamer = NewHuman("Handler");
            tamer.skills.GetSkill(SkillDefOf.Animals)!.Level = 0;

            bool everAngry = false;
            bool everNotAngry = false;
            for (int seed = 1; seed <= 200 && !(everAngry && everNotAngry); seed++)
            {
                Pawn muffalo = new Pawn(Muffalo, "Wild"); // wildness 0.6: chance = 0.5 - 0.54 < 0, always fails.
                Rand.Current = new RandomStream(seed);
                bool success = TameUtility.TryTame(muffalo, tamer);
                Assert.False(success);
                if (muffalo.mindState.angryAt != null)
                {
                    everAngry = true;
                    Assert.Same(tamer, muffalo.mindState.angryAt);
                    Assert.True(muffalo.mindState.angryUntilTick > Find.TickManager.TicksGame);
                }
                else
                {
                    everNotAngry = true;
                }
            }

            Assert.True(everAngry, "expected at least one failed attempt, across 200 seeds, to anger the animal");
            Assert.True(everNotAngry, "expected at least one failed attempt, across 200 seeds, to NOT anger the animal");
        }

        [Fact]
        public void ThinkNode_ConditionalAngryAtHandler_pre_empts_the_rest_of_the_animal_tree_until_it_expires()
        {
            CoreMap map = NewMap(10, 10);
            Pawn muffalo = SpawnAnimal(map, Muffalo, new IntVec3(5, 0, 5));
            Pawn tamer = SpawnHuman(map, new IntVec3(1, 0, 1));
            muffalo.mindState.angryAt = tamer;
            muffalo.mindState.angryUntilTick = Find.TickManager.TicksGame + 1000;

            var conditional = new ThinkNode_ConditionalAngryAtHandler { subNodes = new List<ThinkNode> { new JobGiver_WanderAnywhere() } };
            Assert.True(conditional.TryIssueJobPackage(muffalo).IsValid);

            // Past the anger window, the tier no longer fires at all.
            Find.TickManager.DebugSetTicksGame(muffalo.mindState.angryUntilTick + 1);
            Assert.False(conditional.TryIssueJobPackage(muffalo).IsValid);
        }

        // ---- helpers ----

        private static int CountTameSuccesses(int skill, int seedBase, int trials)
        {
            Pawn tamer = NewHuman("Handler");
            tamer.skills.GetSkill(SkillDefOf.Animals)!.Level = skill;
            int successes = 0;
            for (int i = 0; i < trials; i++)
            {
                Pawn husky = new Pawn(Husky, "Test"); // fixed wildness (0.3): isolates the skill variable.
                Rand.Current = new RandomStream(seedBase + i);
                if (TameUtility.TryTame(husky, tamer)) successes++;
            }
            return successes;
        }

        private static int CountTameSuccessesByRace(ThingDef race, int skill, int seedBase, int trials)
        {
            Pawn tamer = NewHuman("Handler");
            tamer.skills.GetSkill(SkillDefOf.Animals)!.Level = skill;
            int successes = 0;
            for (int i = 0; i < trials; i++)
            {
                Pawn animal = new Pawn(race, "Test");
                Rand.Current = new RandomStream(seedBase + i);
                if (TameUtility.TryTame(animal, tamer)) successes++;
            }
            return successes;
        }
    }
}
