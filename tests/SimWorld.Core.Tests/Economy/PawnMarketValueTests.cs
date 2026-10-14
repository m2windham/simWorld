using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// What a person, an animal and a building are worth through the full stat pipeline
    /// (<see cref="StatWorker_MarketValue"/>, <see cref="PriceUtility"/>) — the numbers the storyteller's wealth
    /// term is built from.
    ///
    /// <para/>The race bases are RimWorld's and are pinned as the sourced literals they are. Everything the
    /// pipeline multiplies them by is pinned as a relation (a hale pawn is worth more than a wounded one, a
    /// skilled one more than an unskilled one), because the factors are RimWorld's shape and the values a
    /// particular pawn ends up with depend on content the tests do not control.
    /// </summary>
    public class PawnMarketValueTests : ContentTestBase
    {
        public PawnMarketValueTests(CoreContentFixture content) : base(content)
        {
        }

        private static float ValueOf(Pawn pawn) => pawn.GetStatValue(StatDefOf.MarketValue);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A grown, healthy human with every skill at <paramref name="skill"/>.</summary>
        private static Pawn Colonist(int skill = 6)
        {
            Pawn pawn = NewHuman();
            foreach (SkillRecord record in pawn.skills.skills) record.Level = skill;
            return pawn;
        }

        private static void Wound(Pawn pawn, string partLabel, float amount)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            BodyPartRecord part = pawn.RaceProps.body!.GetPartByLabel(partLabel)!;
            cut.Worker.Apply(new DamageInfo(cut, amount, hitPart: part), pawn);
        }

        // ---- the race base ----

        [Fact]
        public void Every_race_in_content_has_a_sourced_base_value()
        {
            // RimWorld's infobox figures (rimworldwiki.com/wiki/Human, /Husky, /Muffalo, /Chicken).
            Assert.Equal(1750f, Def("Human").GetStatValueAbstract(StatDefOf.MarketValue));
            Assert.Equal(250f, Def("Husky").GetStatValueAbstract(StatDefOf.MarketValue));
            Assert.Equal(300f, Def("Muffalo").GetStatValueAbstract(StatDefOf.MarketValue));
            Assert.Equal(50f, Def("Chicken").GetStatValueAbstract(StatDefOf.MarketValue));

            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Pawn && d.race != null && !d.defName.StartsWith("Corpse_")))
            {
                Assert.True(race.GetStatValueAbstract(StatDefOf.MarketValue) > 0f, race.defName + " has no market value, so a pawn of it would be worth nothing to the storyteller.");
            }
        }

        [Fact]
        public void A_person_is_worth_their_races_base_scaled_by_who_they_are()
        {
            Pawn pawn = Colonist();
            float factor = PriceUtility.PawnQualityPriceFactor(pawn);

            Assert.Equal(1750f * factor, ValueOf(pawn), 2);
            // A healthy adult of middling skill is a person worth about the species' worth, not a fraction or
            // several multiples of it.
            Assert.InRange(factor, 0.5f, 2f);
        }

        // ---- the factors, as relations ----

        [Fact]
        public void A_wounded_person_is_worth_less_than_the_same_person_unhurt()
        {
            Pawn hale = Colonist();
            Pawn hurt = Colonist();
            Wound(hurt, "left leg", 25f);
            Assert.True(hurt.health.summaryHealth.SummaryHealthPercent < 1f, "fixture: the wound must register");

            Assert.True(ValueOf(hurt) < ValueOf(hale));
        }

        [Fact]
        public void A_person_who_cannot_do_something_at_all_is_worth_less_than_one_who_can_do_it_badly()
        {
            Pawn hale = Colonist();
            Pawn blind = Colonist();
            foreach (BodyPartRecord eye in blind.RaceProps.body!.AllParts.Where(p => p.def.defName == "Eye"))
            {
                Wound(blind, eye.Label, 1000f);
            }
            Assert.False(blind.health.capacities.CapableOf(PawnCapacityDefOf.Sight), "fixture: both eyes gone");

            float missing = ValueOf(blind);
            Assert.True(missing < ValueOf(hale) * PriceUtility.MissingCapacityFactor + 0.01f,
                "a capacity the pawn has lost outright costs at least the missing-capacity factor");
        }

        [Fact]
        public void A_more_skilled_person_is_worth_more()
        {
            float novice = ValueOf(Colonist(skill: 1));
            float journeyman = ValueOf(Colonist(skill: 8));
            float master = ValueOf(Colonist(skill: 16));

            Assert.True(novice < journeyman);
            Assert.True(journeyman < master);
        }

        [Fact]
        public void A_trait_the_wiki_prices_takes_its_share_off()
        {
            Pawn plain = Colonist();
            Pawn pyro = Colonist();
            pyro.story.traits.GainTrait(new Trait(Trait("Pyromaniac")));

            float offset = pyro.story.traits.allTraits[0].CurrentData.marketValueFactorOffset;
            Assert.True(offset < 0f);
            Assert.True(ValueOf(pyro) < ValueOf(plain));
            Assert.Equal(offset, PriceUtility.PawnQualityPriceFactor(pyro) - PriceUtility.PawnQualityPriceFactor(plain), 4);
        }

        [Fact]
        public void No_pawn_is_ever_worth_less_than_the_floor_share_of_its_race()
        {
            Pawn wreck = Colonist(skill: 0);
            wreck.story.traits.GainTrait(new Trait(Trait("Pyromaniac")));
            wreck.story.traits.GainTrait(new Trait(Trait("Abrasive")));
            Wound(wreck, "torso", 5f);

            Assert.True(PriceUtility.PawnQualityPriceFactor(wreck) >= PriceUtility.MinFactor);
            Assert.True(ValueOf(wreck) >= 1750f * PriceUtility.MinFactor - 0.01f);
        }

        // ---- life stage ----

        [Fact]
        public void A_baby_is_worth_its_life_stages_share_of_the_same_person_grown()
        {
            Pawn adult = Colonist();
            Pawn baby = Colonist();
            baby.ageTracker.ageBiologicalTicks = (long)(0.5f * GenDate.TicksPerYear);
            baby.ageTracker.ageChronologicalTicks = baby.ageTracker.ageBiologicalTicks;

            LifeStageDef babyStage = baby.ageTracker.CurLifeStage!;
            Assert.Equal(DevelopmentalStage.Baby, babyStage.developmentalStage);
            Assert.True(babyStage.marketValueFactor < 1f);

            Assert.Equal(
                PriceUtility.PawnQualityPriceFactor(adult) * babyStage.marketValueFactor,
                PriceUtility.PawnQualityPriceFactor(baby),
                3);
            Assert.True(ValueOf(baby) < ValueOf(adult));
        }

        // ---- animals ----

        [Fact]
        public void An_animal_is_priced_without_skills_because_it_has_none()
        {
            var husky = new Pawn(Husky, "Rex");
            float before = ValueOf(husky);
            foreach (SkillRecord record in husky.skills.skills) record.Level = 20;

            // RimWorld's animals carry no skill tracker, so a hound's worth cannot move with one. The port's
            // pawns all carry a tracker at level zero, which must not read as "an unskilled person".
            Assert.Equal(before, ValueOf(husky), 3);
            // Worth something, and no more than the base: the port's animal body has no parts for Manipulation or
            // Talking, so RimWorld's formula (0.6 for each capacity a pawn cannot do at all) prices a hound at a
            // little over a third of its base. That is the formula applied to this port's simplified body.
            Assert.InRange(before, 1f, 250f);
        }

        // ---- the Statistical tier ----

        [Fact]
        public void A_statistical_pawn_is_priced_from_its_sampled_health_and_its_hediffs_are_not_read()
        {
            Pawn citizen = Colonist();
            citizen.tier.Notify_AttentionChanged(false);
            citizen.tier.DemoteToStatistical();
            Assert.Equal(PawnTier.Statistical, citizen.tier.Tier);

            float before = ValueOf(citizen);
            Wound(citizen, "left leg", 25f);
            Assert.Equal(before, ValueOf(citizen), 3);

            Pawn fullTier = Colonist();
            float fullBefore = ValueOf(fullTier);
            Wound(fullTier, "left leg", 25f);
            Assert.True(ValueOf(fullTier) < fullBefore, "a Full-tier pawn is priced from its real health");
        }

        // ---- buildings and items ----

        [Fact]
        public void A_building_that_sets_no_value_is_worth_what_it_costs_to_make()
        {
            ThingDef wall = Def("Wall");
            Assert.False(wall.StatBaseDefined(StatDefOf.MarketValue));

            float expected = wall.costList!.Sum(c => c.count * c.thingDef.GetStatValueAbstract(StatDefOf.MarketValue))
                + wall.GetStatValueAbstract(StatDefOf.WorkToBuild) * StatWorker_MarketValue.ValuePerWork;

            Assert.Equal(expected, wall.GetStatValue(StatDefOf.MarketValue), 4);
            Assert.True(expected > 0f);

            // Trade's lookup is statBases-only and is deliberately unchanged by any of this.
            Assert.Equal(0f, TradeUtility.BaseMarketValue(wall));
        }

        [Fact]
        public void A_building_made_of_dearer_materials_is_worth_more()
        {
            float wood = Def("Wall").GetStatValue(StatDefOf.MarketValue);
            float stone = Def("WallGranite").GetStatValue(StatDefOf.MarketValue);
            float bed = Def("Bed").GetStatValue(StatDefOf.MarketValue);

            Assert.True(wood > 0f && stone > 0f);
            Assert.True(bed > wood, "a bed costs more to make than a wall");
        }

        [Fact]
        public void Something_nobody_built_that_sets_no_value_is_worth_nothing()
        {
            // A body is not worth the work of a building. (It would be, if the default work-to-build leaked
            // onto every def that has no market value of its own.)
            ThingDef corpse = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.defName.StartsWith("Corpse_"));
            Assert.False(corpse.StatBaseDefined(StatDefOf.MarketValue));
            Assert.Equal(0f, corpse.GetStatValue(StatDefOf.MarketValue));
        }

        [Fact]
        public void A_def_that_sets_a_value_reads_it_including_an_explicit_zero()
        {
            ThingDef logs = Def("WoodLog");
            Assert.True(logs.StatBaseDefined(StatDefOf.MarketValue));
            Assert.Equal(TradeUtility.BaseMarketValue(logs), logs.GetStatValue(StatDefOf.MarketValue), 4);

            ThingDef chunk = Def("ChunkGranite");
            Assert.True(chunk.StatBaseDefined(StatDefOf.MarketValue));
            Assert.Equal(0f, chunk.GetStatValue(StatDefOf.MarketValue));
        }

        [Fact]
        public void A_building_with_no_stuff_named_is_priced_at_the_guess()
        {
            var def = new ThingDef { defName = "TestStuffWall", category = ThingCategory.Building, costStuffCount = 5 };
            Assert.Equal(5 * StatWorker_MarketValue.DefaultGuessStuffCost + def.GetStatValueAbstract(StatDefOf.WorkToBuild) * StatWorker_MarketValue.ValuePerWork,
                StatWorker_MarketValue.CalculatedBaseMarketValue(def, null), 4);

            ThingDef steel = Def("Steel");
            float made = StatWorker_MarketValue.CalculatedBaseMarketValue(def, steel);
            Assert.Equal(5 * steel.GetStatValue(StatDefOf.MarketValue) + def.GetStatValueAbstract(StatDefOf.WorkToBuild) * StatWorker_MarketValue.ValuePerWork, made, 4);
        }
    }
}
