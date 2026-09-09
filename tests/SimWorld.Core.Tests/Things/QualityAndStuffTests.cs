using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// stats.stuff-quality-parts: CompQuality gives QualityCategory a home on a live Thing, StatPart_Quality
    /// reads it back into the stat pipeline, and Thing.Stuff finally makes the stuff factor/offset half of
    /// StatWorker.GetValueUnfinalized (already real, already tested against the abstract (def, stuff) request)
    /// fire for a live Thing too.
    /// </summary>
    public class QualityAndStuffTests : ContentTestBase
    {
        public QualityAndStuffTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef QualityDef() => new ThingDef
        {
            defName = "Test_QualityThing",
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            hasQuality = true,
            comps = new List<CompProperties> { new CompProperties_Quality() },
        };

        // ---- CompQuality ----

        [Fact]
        public void CompQuality_defaults_to_Normal_and_can_be_set()
        {
            var thing = (ThingWithComps)ThingMaker.MakeThing(QualityDef());
            CompQuality comp = thing.GetComp<CompQuality>()!;

            Assert.Equal(QualityCategory.Normal, comp.Quality);
            comp.SetQuality(QualityCategory.Excellent);
            Assert.Equal(QualityCategory.Excellent, comp.Quality);
        }

        [Fact]
        public void ThingMaker_applies_the_quality_argument_to_a_freshly_made_things_CompQuality()
        {
            Thing thing = ThingMaker.MakeThing(QualityDef(), quality: QualityCategory.Legendary);
            var twc = Assert.IsType<ThingWithComps>(thing);
            Assert.Equal(QualityCategory.Legendary, twc.GetComp<CompQuality>()!.Quality);
        }

        [Fact]
        public void A_def_with_no_CompQuality_ignores_the_quality_argument_without_erroring()
        {
            var def = new ThingDef { defName = "Test_NoQuality", category = ThingCategory.Item, thingClass = typeof(Thing) };
            Thing thing = ThingMaker.MakeThing(def, quality: QualityCategory.Masterwork);
            Assert.NotNull(thing);
        }

        [Fact]
        public void CompQuality_survives_a_save_round_trip()
        {
            // A real, loaded content def (Apparel_Shirt) rather than a throwaway one: Scribe_Defs.Look
            // resolves defs back by defName against DefDatabase.Global on load, which only ever holds what
            // content actually registered.
            var thing = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Shirt"));
            thing.GetComp<CompQuality>()!.SetQuality(QualityCategory.Poor);

            string xml = Scribe.SaveToString(thing, "thing");
            var loaded = Scribe.Load<ThingWithComps>(xml, "thing", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            Assert.Equal(QualityCategory.Poor, loaded.GetComp<CompQuality>()!.Quality);
        }

        // ---- StatPart_Quality ----

        private static StatDef QualityStat()
        {
            var stat = new StatDef { defName = "Test_QualityStat", defaultBaseValue = 10f, minValue = 0f };
            stat.parts = new List<StatPart>
            {
                new StatPart_Quality
                {
                    curve = new SimpleCurve(new[]
                    {
                        new CurvePoint(0f, 0.5f),  // Awful
                        new CurvePoint(2f, 1f),    // Normal
                        new CurvePoint(6f, 4f),    // Legendary
                    }),
                },
            };
            return stat;
        }

        [Fact]
        public void StatPart_Quality_multiplies_by_the_curve_at_the_things_own_quality()
        {
            StatDef stat = QualityStat();
            var thing = (ThingWithComps)ThingMaker.MakeThing(QualityDef());
            CompQuality comp = thing.GetComp<CompQuality>()!;

            comp.SetQuality(QualityCategory.Normal);
            Assert.Equal(10f, thing.GetStatValue(stat));

            comp.SetQuality(QualityCategory.Awful);
            Assert.Equal(5f, thing.GetStatValue(stat));

            comp.SetQuality(QualityCategory.Legendary);
            Assert.Equal(40f, thing.GetStatValue(stat));
        }

        [Fact]
        public void StatPart_Quality_does_nothing_for_a_thing_with_no_CompQuality()
        {
            StatDef stat = QualityStat();
            var def = new ThingDef { defName = "Test_Unquantified", category = ThingCategory.Item, thingClass = typeof(Thing) };
            Thing plain = ThingMaker.MakeThing(def);

            Assert.Equal(10f, plain.GetStatValue(stat));
        }

        [Fact]
        public void Higher_quality_orders_MarketValue_upward_on_real_apparel_content()
        {
            // MarketValue's own StatPart_Quality (Stats_Economy.xml) is real content, not a test double.
            ThingDef shirtDef = DefDatabase<ThingDef>.GetNamed("Apparel_Shirt");
            var awful = (ThingWithComps)ThingMaker.MakeThing(shirtDef, quality: QualityCategory.Awful);
            var normal = (ThingWithComps)ThingMaker.MakeThing(shirtDef, quality: QualityCategory.Normal);
            var legendary = (ThingWithComps)ThingMaker.MakeThing(shirtDef, quality: QualityCategory.Legendary);

            Assert.True(awful.GetStatValue(StatDefOf.MarketValue) < normal.GetStatValue(StatDefOf.MarketValue));
            Assert.True(normal.GetStatValue(StatDefOf.MarketValue) < legendary.GetStatValue(StatDefOf.MarketValue));
        }

        // ---- Thing.Stuff / the stuff-derived half of the pipeline ----

        [Fact]
        public void A_things_own_Stuff_feeds_the_stuff_factor_and_offset_pass_StatRequest_For_Thing_used_to_drop()
        {
            StatDef stat = new StatDef { defName = "Test_StuffStat", defaultBaseValue = 10f, minValue = 0f };
            var stuff = new ThingDef
            {
                defName = "Test_Stuff",
                stuffProps = new StuffProperties
                {
                    statFactors = new List<StatModifier> { new StatModifier(stat, 2f) },
                    statOffsets = new List<StatModifier> { new StatModifier(stat, 3f) },
                },
            };
            var def = new ThingDef { defName = "Test_MadeFromStuffThing", category = ThingCategory.Item, thingClass = typeof(Thing) };

            Thing thing = ThingMaker.MakeThing(def, stuff);
            Assert.Same(stuff, thing.Stuff);

            // (10 * 2) + 3 = 23 — the exact math StatWorkerTests already proves for the abstract (def, stuff)
            // request; this is the same math now reachable from a live Thing via StatRequest.For(Thing).
            Assert.Equal(23f, thing.GetStatValue(stat));
        }

        [Fact]
        public void A_thing_made_with_no_stuff_has_a_null_Stuff_and_is_unaffected_by_the_stuff_pass()
        {
            StatDef stat = new StatDef { defName = "Test_NoStuffStat", defaultBaseValue = 10f, minValue = 0f };
            var def = new ThingDef { defName = "Test_PlainThing", category = ThingCategory.Item, thingClass = typeof(Thing) };

            Thing thing = ThingMaker.MakeThing(def);
            Assert.Null(thing.Stuff);
            Assert.Equal(10f, thing.GetStatValue(stat));
        }

        [Fact]
        public void Stuff_survives_a_save_round_trip()
        {
            // Real, loaded content defs on both sides (see CompQuality's own round-trip test for why).
            ThingDef shirtDef = DefDatabase<ThingDef>.GetNamed("Apparel_Shirt");
            ThingDef cloth = DefDatabase<ThingDef>.GetNamed("Cloth");

            var thing = (ThingWithComps)ThingMaker.MakeThing(shirtDef, cloth);
            string xml = Scribe.SaveToString(thing, "thing");
            ThingWithComps loaded = Scribe.Load<ThingWithComps>(xml, "thing", out IReadOnlyList<string> errors, Content.Database);

            Assert.Empty(errors);
            Assert.Equal("Cloth", loaded.Stuff?.defName);
        }
    }
}
