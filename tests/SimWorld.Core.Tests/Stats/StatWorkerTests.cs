using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
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

        // ---- skill-need offsets/factors ----

        [Fact]
        public void SkillNeed_Direct_reads_the_table_entry_for_the_pawns_level_and_clamps_past_the_end()
        {
            var need = new SkillNeed_Direct { skill = SkillDefOf.Mining, valuesPerLevel = new List<float> { 1f, 2f, 3f } };
            Pawn p = NewHuman();

            p.skills.GetSkill(SkillDefOf.Mining)!.Level = 0;
            Assert.Equal(1f, need.ValueFor(p));
            p.skills.GetSkill(SkillDefOf.Mining)!.Level = 2;
            Assert.Equal(3f, need.ValueFor(p));

            // Level 20 is well past the 3-entry table; clamps to the last entry rather than throwing.
            p.skills.GetSkill(SkillDefOf.Mining)!.Level = 20;
            Assert.Equal(3f, need.ValueFor(p));

            Assert.Equal(0f, new SkillNeed_Direct { skill = SkillDefOf.Mining }.ValueFor(p));
        }

        [Fact]
        public void SkillNeed_BaseBonus_is_base_plus_bonus_times_level()
        {
            var need = new SkillNeed_BaseBonus { skill = SkillDefOf.Cooking, baseValue = 0.5f, bonusPerLevel = 0.1f };
            Pawn p = NewHuman();

            p.skills.GetSkill(SkillDefOf.Cooking)!.Level = 0;
            Assert.Equal(0.5f, need.ValueFor(p), 4);
            p.skills.GetSkill(SkillDefOf.Cooking)!.Level = 10;
            Assert.Equal(1.5f, need.ValueFor(p), 4);
            p.skills.GetSkill(SkillDefOf.Cooking)!.Level = 20;
            Assert.Equal(2.5f, need.ValueFor(p), 4);
        }

        [Fact]
        public void StatWorker_applies_skillNeedFactors_then_skillNeedOffsets_before_trait_and_hediff_terms()
        {
            StatDef stat = NewStat(defaultBaseValue: 10f);
            stat.skillNeedFactors = new List<SkillNeed> { new SkillNeed_BaseBonus { skill = SkillDefOf.Mining, baseValue = 2f } };
            stat.skillNeedOffsets = new List<SkillNeed> { new SkillNeed_BaseBonus { skill = SkillDefOf.Mining, baseValue = 3f } };
            var traitDef = new TraitDef
            {
                defName = "TestSkillNeedOrderTrait",
                degreeDatas = new List<TraitDegreeData>
                {
                    new TraitDegreeData { degree = 0, statFactors = new List<StatModifier> { new StatModifier(stat, 2f) } },
                },
            };

            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(traitDef));

            // ((10 * 2) + 3) * 2 = 46 — skill-need factor first, then skill-need offset, then the trait factor;
            // a different order (e.g. offset before factor) would give a different number, so this pins the shape.
            Assert.Equal(46f, p.GetStatValue(stat));
        }

        [Fact]
        public void Higher_skill_level_yields_a_higher_stat_value_and_level_zero_yields_the_plain_base()
        {
            StatDef stat = NewStat(defaultBaseValue: 1f);
            stat.skillNeedFactors = new List<SkillNeed> { new SkillNeed_BaseBonus { skill = SkillDefOf.Construction, baseValue = 0.5f, bonusPerLevel = 0.05f } };

            Pawn p = NewHuman();
            p.skills.GetSkill(SkillDefOf.Construction)!.Level = 0;
            Assert.Equal(0.5f, p.GetStatValue(stat), 4); // base(1) * (0.5 + 0.05*0)

            float previous = p.GetStatValue(stat);
            for (int level = 1; level <= SkillRecord.MaxLevel; level++)
            {
                p.skills.GetSkill(SkillDefOf.Construction)!.Level = level;
                float current = p.GetStatValue(stat);
                Assert.True(current > previous, "expected level " + level + "'s value (" + current + ") to exceed level " + (level - 1) + "'s (" + previous + ")");
                previous = current;
            }
        }

        [Fact]
        public void A_totally_disabled_skill_reads_as_level_zero_for_a_skill_need_too()
        {
            StatDef stat = NewStat(defaultBaseValue: 1f);
            stat.skillNeedFactors = new List<SkillNeed> { new SkillNeed_BaseBonus { skill = SkillDefOf.Shooting, baseValue = 0.5f, bonusPerLevel = 0.05f } };

            Pawn p = NewHuman();
            p.skills.GetSkill(SkillDefOf.Shooting)!.Level = 15;
            float beforeBan = p.GetStatValue(stat);
            Assert.True(beforeBan > 0.5f);

            var violentBan = new TraitDef
            {
                defName = "TestSkillNeedViolentBan",
                degreeDatas = new List<TraitDegreeData> { new TraitDegreeData { degree = 0 } },
                disabledWorkTags = WorkTags.Violent,
            };
            p.story.traits.GainTrait(new Trait(violentBan, 0));
            p.Notify_TraitsChanged();

            Assert.True(p.skills.GetSkill(SkillDefOf.Shooting)!.TotallyDisabled);
            Assert.Equal(0.5f, p.GetStatValue(stat), 4); // level 0 value, even though levelInt is still 15 underneath
        }
    }
}
