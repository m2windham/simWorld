using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Stats
{
    /// <summary>Exercises the stat pipeline's mechanics directly, independent of any one production call site.</summary>
    public class StatWorkerTests : ContentTestBase
    {
        public StatWorkerTests(CoreContentFixture content) : base(content)
        {
        }

        private static StatDef NewStat(float defaultBaseValue = 0f, float minValue = 0f, float maxValue = float.MaxValue) =>
            new StatDef { defName = "TestStat", defaultBaseValue = defaultBaseValue, minValue = minValue, maxValue = maxValue };

        // ---- base value ----

        [Fact]
        public void Base_value_is_the_stats_default_when_the_def_sets_no_statBases_entry()
        {
            StatDef stat = NewStat(defaultBaseValue: 5f);
            var def = new ThingDef { defName = "Bare" };

            Assert.Equal(5f, def.GetStatValue(stat));
        }

        [Fact]
        public void Base_value_reads_the_defs_statBases_entry_when_present()
        {
            StatDef stat = NewStat(defaultBaseValue: 5f);
            var def = new ThingDef { defName = "Set", statBases = new List<StatModifier> { new StatModifier(stat, 12f) } };

            Assert.Equal(12f, def.GetStatValue(stat));
        }

        // ---- clamp and post-process ----

        [Fact]
        public void FinalizeValue_clamps_to_minValue_and_maxValue()
        {
            StatDef stat = NewStat(defaultBaseValue: 100f, minValue: 0f, maxValue: 10f);
            var def = new ThingDef { defName = "TooHigh" };
            Assert.Equal(10f, def.GetStatValue(stat));

            StatDef floored = NewStat(defaultBaseValue: -100f, minValue: -1f, maxValue: 10f);
            Assert.Equal(-1f, def.GetStatValue(floored));
        }

        [Fact]
        public void PostProcessCurve_bends_the_finalized_value_but_not_the_unfinalized_one()
        {
            StatDef stat = NewStat(defaultBaseValue: 4f);
            stat.postProcessCurve = new SimpleCurve(new[] { new CurvePoint(0f, 0f), new CurvePoint(4f, 100f), new CurvePoint(10f, 100f) });
            var def = new ThingDef { defName = "Curved" };

            Assert.Equal(4f, def.GetStatValue(stat, applyPostProcess: false));
            Assert.Equal(100f, def.GetStatValue(stat, applyPostProcess: true));
        }

        // ---- stuff (abstract def+stuff requests) ----

        [Fact]
        public void Stuff_factor_and_offset_apply_to_an_abstract_def_plus_stuff_request()
        {
            StatDef stat = NewStat(defaultBaseValue: 10f);
            var def = new ThingDef { defName = "Blocks" };
            var stuff = new ThingDef
            {
                defName = "Steel",
                stuffProps = new StuffProperties
                {
                    statFactors = new List<StatModifier> { new StatModifier(stat, 2f) },
                    statOffsets = new List<StatModifier> { new StatModifier(stat, 3f) },
                },
            };

            // RimWorld order: factor first, then offset — (10 * 2) + 3 = 23.
            Assert.Equal(23f, stat.Worker.GetValue(StatRequest.For(def, stuff)));
            // No stuff at all falls back to the bare base value.
            Assert.Equal(10f, stat.Worker.GetValue(StatRequest.For(def)));
        }

        // ---- ShouldShowFor ----

        [Fact]
        public void ShouldShowFor_hides_an_undefined_stat_unless_showIfUndefined()
        {
            StatDef hidden = NewStat();
            hidden.showIfUndefined = false;
            var withoutIt = new ThingDef { defName = "NoStat" };
            var withIt = new ThingDef { defName = "HasStat", statBases = new List<StatModifier> { new StatModifier(hidden, 1f) } };

            Assert.False(hidden.Worker.ShouldShowFor(StatRequest.For(withoutIt)));
            Assert.True(hidden.Worker.ShouldShowFor(StatRequest.For(withIt)));

            StatDef alwaysShown = NewStat();
            Assert.True(alwaysShown.Worker.ShouldShowFor(StatRequest.For(withoutIt)));
        }

        // ---- stat parts ----

        private sealed class DoublingStatPart : StatPart
        {
            public override void TransformValue(StatRequest req, ref float val) => val *= 2f;
        }

        [Fact]
        public void FinalizeValue_runs_stat_parts_before_the_post_process_curve_and_clamp()
        {
            StatDef stat = NewStat(defaultBaseValue: 3f, maxValue: 100f);
            stat.parts = new List<StatPart> { new DoublingStatPart() };
            var def = new ThingDef { defName = "Parted" };

            Assert.Equal(6f, def.GetStatValue(stat));
        }

        // ---- trait offsets/factors (order: offset before factor) ----

        [Fact]
        public void Trait_statOffsets_and_statFactors_apply_in_order()
        {
            StatDef stat = NewStat(defaultBaseValue: 10f);
            var traitDef = new TraitDef
            {
                defName = "TestTrait",
                degreeDatas = new List<TraitDegreeData>
                {
                    new TraitDegreeData
                    {
                        degree = 0,
                        statOffsets = new List<StatModifier> { new StatModifier(stat, 5f) },
                        statFactors = new List<StatModifier> { new StatModifier(stat, 2f) },
                    },
                },
            };

            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(traitDef));

            // (10 + 5) * 2 = 30.
            Assert.Equal(30f, p.GetStatValue(stat));
        }

        // ---- hediff-stage offsets/factors ----

        [Fact]
        public void HediffStage_statOffsets_and_statFactors_apply()
        {
            StatDef stat = NewStat(defaultBaseValue: 10f);
            var hediffDef = new HediffDef
            {
                defName = "TestCondition",
                stages = new List<HediffStage>
                {
                    new HediffStage
                    {
                        statOffsets = new List<StatModifier> { new StatModifier(stat, 4f) },
                        statFactors = new List<StatModifier> { new StatModifier(stat, 0.5f) },
                    },
                },
            };

            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, hediffDef, 1f);

            // (10 + 4) * 0.5 = 7.
            Assert.Equal(7f, p.GetStatValue(stat));
        }
    }
}
