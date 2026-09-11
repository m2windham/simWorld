using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// What a life stage does to the pawn living it (system: needs.pawn / pawngen).
    ///
    /// <para/>A <see cref="LifeStageDef"/> is content's way of saying that the same creature is a different
    /// animal at three months than at three years, and for a long time this port read three of its factors
    /// (body size, health scale, hunger rate) and ignored the rest. These tests pin the three that were
    /// wired here — <c>foodMaxFactor</c>, <c>alwaysDowned</c> and <c>reproductive</c> — and, as much as the
    /// wiring itself, <b>the growth edge</b>: a factor read once at spawn is not wired at all, because the
    /// pawn that read it does not stay the age it was. Every test below that can grow a pawn does.
    ///
    /// <para/>Nothing here asserts a factor's literal value. The numbers in <c>LifeStages.xml</c> are this
    /// port's own (RimWorld's per-stage tables were not available to source), so what is pinned is the shape
    /// they encode: an ordering across the stages, a direction of change across a boundary, or a round trip.
    /// </summary>
    public class LifeStageTests : ContentTestBase
    {
        public LifeStageTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new Storyteller();
            Find.FactionManager = new FactionManager();
            Find.FamilyManager = new FamilyManager();
        }

        private static ThingDef Chicken => DefDatabase<ThingDef>.GetNamed("Chicken");

        private static ThingDef Egg => DefDatabase<ThingDef>.GetNamed("EggAnimalUnfertilized");

        private static LifeStageDef LifeStage(string defName) => DefDatabase<LifeStageDef>.GetNamed(defName);

        /// <summary>One human age inside each of the four shipped humanlike stages, youngest first.</summary>
        private static readonly float[] AgeInEachHumanStage = { 1f, 5f, 15f, 30f };

        private static Pawn HumanAged(float years, string name = "Test")
        {
            Pawn pawn = NewHuman(name);
            pawn.ageTracker.DebugSetAge(years);
            return pawn;
        }

        // -----------------------------------------------------------------------------------------------
        // foodMaxFactor — how big a stomach the stage gives.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The seam this field exists for: a stage scales food capacity <i>on top of</i> body size, so the
        /// stomach is not simply a linear function of how big the creature is. Asserted as the ratio
        /// <c>MaxLevel / BodySize</c> differing between stages — if <c>foodMaxFactor</c> were ignored that
        /// ratio would be identical for every stage of every race, which is exactly the defect.
        /// </summary>
        [Fact]
        public void A_life_stage_sizes_the_stomach_by_more_than_body_size_alone()
        {
            Pawn baby = HumanAged(AgeInEachHumanStage[0], "Baby");
            Pawn adult = HumanAged(AgeInEachHumanStage[3], "Adult");

            float babyPerUnitOfBody = baby.needs.food!.MaxLevel / baby.BodySize;
            float adultPerUnitOfBody = adult.needs.food!.MaxLevel / adult.BodySize;

            Assert.True(babyPerUnitOfBody < adultPerUnitOfBody,
                "an infant's stomach is scaled by its life stage as well as by its size; per unit of body it "
                + "held " + babyPerUnitOfBody + " against an adult's " + adultPerUnitOfBody);
        }

        /// <summary>Growing up never shrinks the stomach, across every shipped humanlike stage in order.</summary>
        [Fact]
        public void The_food_ceiling_never_falls_as_a_human_grows_through_its_stages()
        {
            float previous = -1f;
            foreach (float age in AgeInEachHumanStage)
            {
                Pawn pawn = HumanAged(age, "Age" + age);
                float max = pawn.needs.food!.MaxLevel;
                Assert.True(max > previous, "the food ceiling fell going into age " + age + ": " + max + " after " + previous);
                previous = max;
            }
        }

        /// <summary>
        /// The growth edge for this field, and the behaviour it buys. A pawn's food need holds an absolute
        /// amount of nutrition under a stage-scaled ceiling, so crossing into a larger stage leaves the
        /// amount alone and makes it a smaller fraction of a bigger stomach — the newly-grown pawn is
        /// hungry, without anything having taken food away from it.
        /// </summary>
        [Fact]
        public void Growing_into_a_larger_stage_raises_the_ceiling_and_leaves_the_pawn_hungrier()
        {
            Pawn pawn = HumanAged(AgeInEachHumanStage[0], "Growing");
            Need_Food food = pawn.needs.food!;
            food.CurLevel = food.MaxLevel;

            float nutritionHeld = food.CurLevel;
            float ceilingAsBaby = food.MaxLevel;
            Assert.Equal(1f, food.CurLevelPercentage, 4);

            pawn.ageTracker.DebugSetAge(AgeInEachHumanStage[3]);

            Assert.True(food.MaxLevel > ceilingAsBaby, "growing up did not raise the food ceiling");
            Assert.Equal(nutritionHeld, food.CurLevel, 4);
            Assert.True(food.CurLevelPercentage < 1f,
                "the same nutrition in a bigger stomach should read as less than full, not as full");
        }

        /// <summary>
        /// The other direction, which is the one that can corrupt state rather than merely surprise: a
        /// ceiling that drops must take the level with it, or the need reports more than 100% of a stomach
        /// the pawn no longer has. No shipped race shrinks, so this rides in on a debug age change — which is
        /// precisely the case a clamp exists for.
        /// </summary>
        [Fact]
        public void Falling_back_into_a_smaller_stage_never_leaves_a_need_over_full()
        {
            Pawn pawn = HumanAged(AgeInEachHumanStage[3], "Shrinking");
            Need_Food food = pawn.needs.food!;
            food.CurLevel = food.MaxLevel;

            pawn.ageTracker.DebugSetAge(AgeInEachHumanStage[0]);

            Assert.True(food.CurLevel <= food.MaxLevel + 0.0001f,
                "level " + food.CurLevel + " left above the new ceiling " + food.MaxLevel);
            Assert.True(food.CurLevelPercentage <= 1.0001f, "need read " + food.CurLevelPercentage + " of full");
        }

        // -----------------------------------------------------------------------------------------------
        // alwaysDowned — the stage that cannot stand up.
        // -----------------------------------------------------------------------------------------------

        [Fact]
        public void A_newborn_is_downed_by_the_stage_it_is_in()
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            Assert.True(baby.ageTracker.CurLifeStage!.alwaysDowned, "fixture assumption: the baby stage is alwaysDowned");

            RunTicks(1, baby);

            Assert.True(baby.Downed);
        }

        /// <summary>
        /// The design decision, pinned so it cannot be quietly undone. Being too young to walk is <i>not</i>
        /// modelled as a broken body: the capacity system still reports an intact, fully mobile pawn, and the
        /// downing comes from the separate gate beside <c>ForceDowned</c>. If someone later moves this into
        /// <c>PawnCapacityWorker_Moving</c>, this test is what says why not — every consumer of that number,
        /// the lethal-capacity death check included, would start reasoning about an injury that is not there.
        /// </summary>
        [Fact]
        public void An_infant_is_downed_without_the_capacity_system_being_told_a_lie_about_its_body()
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            RunTicks(1, baby);

            Assert.True(baby.Downed);
            Assert.True(baby.health.LifeStageForcesDowned);
            Assert.True(baby.health.capacities.CapableOf(PawnCapacityDefOf.Moving),
                "the infant's legs are fine; only its life stage keeps it off them");
            Assert.Null(baby.health.ShouldBeDeadFromRequiredCapacity());
            Assert.False(baby.Dead);
        }

        /// <summary>
        /// The growth edge for this field, and the answer to "a human infant that can never recover": the
        /// stage is what holds the pawn down, so the pawn gets up by leaving it. No tick is needed — the age
        /// tracker raises <see cref="Pawn.Notify_LifeStageStarted"/> at the crossing and the downed state is
        /// re-derived there.
        /// </summary>
        [Fact]
        public void Growing_out_of_infancy_stands_the_pawn_back_up()
        {
            Pawn pawn = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            RunTicks(1, pawn);
            Assert.True(pawn.Downed);

            pawn.ageTracker.DebugSetAge(AgeInEachHumanStage[1]);

            Assert.False(pawn.health.LifeStageForcesDowned);
            Assert.False(pawn.Downed);
        }

        [Fact]
        public void An_adult_is_not_touched_by_the_gate_at_all()
        {
            Pawn adult = HumanAged(AgeInEachHumanStage[3], "Adult");

            RunTicks(1, adult);

            Assert.False(adult.health.LifeStageForcesDowned);
            Assert.False(adult.Downed);
        }

        /// <summary>
        /// A baby is not a casualty. The storyteller's adaptation clock counts colonists being knocked down —
        /// "how badly is this place being hurt" — and wiring <c>alwaysDowned</c> without this guard would
        /// have charged that clock for every child born. Asserted against the adult case rather than against
        /// the penalty constant: what matters is that one of these two moves the clock and the other does not.
        /// </summary>
        [Fact]
        public void An_infant_going_down_does_not_cost_the_storyteller_what_a_felled_adult_does()
        {
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            for (int i = 0; i < 4000; i++) Find.Storyteller.adaptation.AdaptationTick(medium);

            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            Pawn adult = HumanAged(AgeInEachHumanStage[3], "Adult");
            var civilization = new CivilizationTarget(Find.Storyteller);
            civilization.pawns.Add(baby);
            civilization.pawns.Add(adult);

            float quiet = Find.Storyteller.adaptation.AdaptDays;
            RunTicks(1, baby);
            Assert.True(baby.Downed);
            float afterTheBaby = Find.Storyteller.adaptation.AdaptDays;

            adult.health.ForceDowned = true;
            float afterTheAdult = Find.Storyteller.adaptation.AdaptDays;

            Assert.Equal(quiet, afterTheBaby, 4);
            Assert.True(afterTheAdult < afterTheBaby,
                "a citizen actually going down should still move the clock the infant did not");
        }

        // -----------------------------------------------------------------------------------------------
        // reproductive — the stage that may breed, and (this port's one flag for it) may produce.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// A humanlike race whose only life stage declares itself non-reproductive, built so the flag can be
        /// tested apart from age: every shipped human stage old enough to pass the fertility window is also
        /// flagged reproductive, so with stock content alone the flag and the age gate are indistinguishable.
        /// Everything else about the race is copied from Human so nothing but the life stage differs.
        /// </summary>
        private static ThingDef BarrenHumanlike()
        {
            RaceProperties human = Human.race!;
            return new ThingDef
            {
                defName = "TestBarrenHumanlike",
                label = "barren humanlike",
                race = new RaceProperties
                {
                    intelligence = human.intelligence,
                    body = human.body,
                    baseHealthScale = human.baseHealthScale,
                    baseBodySize = human.baseBodySize,
                    baseHungerRate = human.baseHungerRate,
                    foodLevelPercentageWantEat = human.foodLevelPercentageWantEat,
                    needsRest = human.needsRest,
                    isFlesh = human.isFlesh,
                    lifeExpectancy = human.lifeExpectancy,

                    // The whole point: one stage, spanning the entire life, that cannot breed. HumanlikeChild
                    // is borrowed for it because it is the shipped stage that says reproductive=false without
                    // also saying alwaysDowned.
                    lifeStageAges = new List<LifeStageAge>
                    {
                        new LifeStageAge { def = LifeStage("HumanlikeChild"), minAge = 0f },
                    },
                },
            };
        }

        private static Pawn Spouse(ThingDef race, Gender gender, float age, string name)
        {
            var pawn = new Pawn(race, name) { gender = gender };
            pawn.ageTracker.DebugSetAge(age);
            return pawn;
        }

        /// <summary>Cranks mood and food and retries the birth roll under fresh seeds, the same way
        /// <c>DemographyTests.TryForceBirth</c> does, so a "no birth" result means the gate refused rather
        /// than the dice did.</summary>
        private static bool AnyBirthIn(Pawn husband, Pawn wife, Family family, int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                Rand.Current = new RandomStream(i * 7919 + 13);
                husband.needs.mood!.CurLevel = husband.needs.mood.MaxLevel;
                wife.needs.mood!.CurLevel = wife.needs.mood.MaxLevel;
                husband.needs.food!.CurLevel = husband.needs.food.MaxLevel;
                wife.needs.food!.CurLevel = wife.needs.food.MaxLevel;
                family.lastBirthTick = Find.TickManager.TicksGame - DemographyTuning.MinBirthIntervalTicks;

                var population = new List<Pawn> { husband, wife };
                Find.FamilyManager.ProcessBirths(population);
                if (population.Count > 2) return true;
            }
            return false;
        }

        /// <summary>
        /// The birth sweep asks the life stage, not only the calendar. Both couples below are the same age,
        /// in the same fertility window, married into a household with the cooldown clear, rolling under the
        /// same forced-maximum mood and food — the only difference is what their content says their stage can
        /// do, and that is enough to decide it.
        /// </summary>
        [Fact]
        public void A_life_stage_that_cannot_reproduce_bars_a_birth_at_an_age_that_otherwise_allows_one()
        {
            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            float age = DemographyTuning.MinMarriageAgeYears + 7f;

            ThingDef barren = BarrenHumanlike();
            Pawn barrenHusband = Spouse(barren, Gender.Male, age, "BarrenHusband");
            Pawn barrenWife = Spouse(barren, Gender.Female, age, "BarrenWife");
            Family barrenFamily = Find.FamilyManager.FoundHousehold(barrenHusband, barrenWife, 0);
            Assert.False(barrenHusband.ageTracker.CurLifeStage!.reproductive, "fixture assumption");

            Pawn husband = Spouse(Human, Gender.Male, age, "Husband");
            Pawn wife = Spouse(Human, Gender.Female, age, "Wife");
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            Assert.True(husband.ageTracker.CurLifeStage!.reproductive, "fixture assumption");

            Assert.False(AnyBirthIn(barrenHusband, barrenWife, barrenFamily, 200),
                "a couple whose life stage cannot reproduce conceived anyway");
            Assert.True(AnyBirthIn(husband, wife, family, 200),
                "the control couple never conceived, so the test above proves nothing");
        }

        // ---- produce: the same flag, as this port's maturity gate ----

        private static CoreMap NewMap() => new CoreMap(5, 5, TerrainDefOf.Soil);

        private static Pawn SpawnHen(CoreMap map, float ageYears)
        {
            var hen = new Pawn(Chicken, "Hen") { gender = Gender.Female };
            hen.ageTracker.DebugSetAge(ageYears);
            GenSpawn.Spawn(hen, new IntVec3(2, 0, 2), map);
            return hen;
        }

        private static void AdvanceComp(CompHasGatherableBodyResource comp, int ticks)
        {
            int start = Find.TickManager.TicksGame;
            for (int i = 1; i <= ticks; i++)
            {
                Find.TickManager.DebugSetTicksGame(start + i);
                comp.CompTick();
            }
        }

        /// <summary>
        /// A chick does not lay. Before the life stage was consulted the produce comps knew only fullness and
        /// gender, so a chicken started laying on the day it hatched — the shipped content puts an adult hen
        /// three tenths of a year away and an egg one day away, so the gap was not theoretical.
        /// </summary>
        [Fact]
        public void A_chick_lays_nothing_however_long_it_is_left()
        {
            CoreMap map = NewMap();
            Pawn chick = SpawnHen(map, 0f);
            CompEggLayer comp = chick.GetComp<CompEggLayer>()!;
            Assert.False(chick.ageTracker.CurLifeStage!.reproductive, "fixture assumption: a chick's stage cannot breed");

            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);
            AdvanceComp(comp, fullTicks * 3);

            Assert.False(comp.Active);
            Assert.Null(comp.Gather(null));
        }

        /// <summary>
        /// The growth edge for the produce side: the same bird, left alone, starts laying once it is old
        /// enough — the gate delays production, it does not disable the animal forever.
        /// </summary>
        [Fact]
        public void The_same_bird_lays_once_it_has_grown_into_an_adult_stage()
        {
            CoreMap map = NewMap();
            Pawn hen = SpawnHen(map, 0f);
            CompEggLayer comp = hen.GetComp<CompEggLayer>()!;
            int fullTicks = (int)(comp.Properties.resourceIntervalDays * GenDate.TicksPerDay);

            AdvanceComp(comp, fullTicks * 2);
            Assert.False(comp.Active);

            hen.ageTracker.DebugSetAge(2f);
            Assert.True(hen.ageTracker.CurLifeStage!.reproductive);
            AdvanceComp(comp, fullTicks + HusbandryTuning.ProduceCheckIntervalTicks);

            Assert.True(comp.Active);
            Thing? produced = comp.Gather(null);
            Assert.NotNull(produced);
            Assert.Equal(Egg, produced!.def);
        }

        // -----------------------------------------------------------------------------------------------
        // Scribe
        // -----------------------------------------------------------------------------------------------

        private class LifeStageHolder : IExposable
        {
            public Pawn? pawn;

            public void ExposeData() => Scribe_Deep.Look(ref pawn, "pawn");
        }

        /// <summary>
        /// Everything wired here is derived from the pawn's age rather than stored beside it, so the round
        /// trip that matters is that age survives and the derived answers come back identical — a loaded
        /// infant is still held down, still has an infant's stomach, and still grows out of both.
        /// </summary>
        [Fact]
        public void A_pawn_held_down_by_its_life_stage_comes_back_that_way_and_still_grows_out_of_it()
        {
            Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);
            RunTicks(1, baby);
            Assert.True(baby.Downed);
            float ceilingAsBaby = baby.needs.food!.MaxLevel;

            var holder = new LifeStageHolder { pawn = baby };
            string xml = Scribe.SaveToString(holder, "game");
            LifeStageHolder loaded = Scribe.Load<LifeStageHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn reloaded = loaded.pawn!;
            Assert.Equal(baby.ageTracker.ageBiologicalTicks, reloaded.ageTracker.ageBiologicalTicks);
            Assert.Equal(baby.ageTracker.CurLifeStage!.defName, reloaded.ageTracker.CurLifeStage!.defName);
            Assert.True(reloaded.Downed);
            Assert.True(reloaded.health.LifeStageForcesDowned);
            Assert.Equal(ceilingAsBaby, reloaded.needs.food!.MaxLevel, 4);

            reloaded.ageTracker.DebugSetAge(AgeInEachHumanStage[3]);
            Assert.False(reloaded.Downed);
            Assert.True(reloaded.needs.food!.MaxLevel > ceilingAsBaby);
        }
    }
}
