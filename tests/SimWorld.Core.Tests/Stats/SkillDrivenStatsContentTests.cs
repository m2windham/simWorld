using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Stats
{
    /// <summary>
    /// The shipped content for the work.stats skill-need pipeline: every stat this module gave a
    /// skillNeedOffsets/skillNeedFactors list to actually reads through it, and behaves the way
    /// <c>docs/status.json</c>'s work.stats item promises — a trend (higher skill, higher value), never a
    /// literal RimWorld number this sandbox could not source (see Stats_Work.xml's own remarks).
    /// </summary>
    public class SkillDrivenStatsContentTests : ContentTestBase
    {
        public SkillDrivenStatsContentTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void DefOfs_for_the_new_skill_driven_stats_are_bound()
        {
            Assert.NotNull(StatDefOf.WorkSpeedGlobal);
            Assert.NotNull(StatDefOf.MedicalTendQuality);
            Assert.NotNull(StatDefOf.MiningSpeed);
            Assert.NotNull(StatDefOf.ConstructionSpeed);
            Assert.NotNull(StatDefOf.CookSpeed);
            Assert.NotNull(StatDefOf.ResearchSpeed);
            Assert.Equal("MedicalTendQuality", StatDefOf.MedicalTendQuality.defName);
        }

        private static void AssertLevelZeroIsBaseAndTrendIsUpward(Pawn p, StatDef stat, SkillDef skill, float expectedAtZero)
        {
            p.skills.GetSkill(skill)!.Level = 0;
            Assert.Equal(expectedAtZero, p.GetStatValue(stat), 3);

            float previous = p.GetStatValue(stat);
            for (int level = 1; level <= SkillRecord.MaxLevel; level++)
            {
                p.skills.GetSkill(skill)!.Level = level;
                float current = p.GetStatValue(stat);
                Assert.True(current >= previous, stat.defName + " fell from level " + (level - 1) + " (" + previous + ") to level " + level + " (" + current + ")");
                previous = current;
            }
            Assert.True(previous > expectedAtZero, stat.defName + " never rose above its level-0 value");
        }

        [Fact]
        public void WorkSpeedGlobal_rises_with_Intellectual_and_starts_at_its_documented_level_zero_value()
        {
            Pawn p = NewHuman();
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.WorkSpeedGlobal, SkillDefOf.Intellectual, 0.95f);
        }

        [Fact]
        public void MedicalTendQuality_rises_with_Medicine_and_starts_at_its_documented_level_zero_value()
        {
            Pawn p = NewHuman();
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.MedicalTendQuality, SkillDefOf.Medicine, 0.30f);
        }

        [Fact]
        public void MiningSpeed_ConstructionSpeed_CookSpeed_ResearchSpeed_all_rise_with_their_skill()
        {
            Pawn p = NewHuman();
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.MiningSpeed, SkillDefOf.Mining, 0.6f);
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.ConstructionSpeed, SkillDefOf.Construction, 0.4f);
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.CookSpeed, SkillDefOf.Cooking, 0.6f);
            AssertLevelZeroIsBaseAndTrendIsUpward(p, StatDefOf.ResearchSpeed, SkillDefOf.Intellectual, 0.4f);
        }

        [Fact]
        public void ShootingAccuracyPawn_MeleeHitChance_MeleeDodgeChance_all_rise_with_their_skill()
        {
            StatDef shootingAccuracy = DefDatabase<StatDef>.GetNamed("ShootingAccuracyPawn");
            StatDef meleeHitChance = DefDatabase<StatDef>.GetNamed("MeleeHitChance");
            StatDef meleeDodgeChance = DefDatabase<StatDef>.GetNamed("MeleeDodgeChance");

            Pawn p = NewHuman();
            AssertLevelZeroIsBaseAndTrendIsUpward(p, shootingAccuracy, SkillDefOf.Shooting, 0.9f);
            AssertLevelZeroIsBaseAndTrendIsUpward(p, meleeHitChance, SkillDefOf.Melee, 0.5f);
            AssertLevelZeroIsBaseAndTrendIsUpward(p, meleeDodgeChance, SkillDefOf.Melee, 0f);
        }

        [Fact]
        public void MedicalTendQuality_stays_at_its_base_for_a_pawn_whose_backstory_bars_Medicine()
        {
            // NobleChild disables ManualDumb, not Medicine — pick a case that actually bars it: force the
            // Medicine skill totally disabled the same way a real "no medical work" backstory would, and
            // confirm the skill-need pipeline (not just SkillRecord.Level) reflects it end to end.
            var noMedicine = new TraitDef
            {
                defName = "TestNoMedicineForTendQuality",
                degreeDatas = new System.Collections.Generic.List<TraitDegreeData> { new TraitDegreeData { degree = 0 } },
                disabledWorkTags = WorkTags.Caring,
            };

            Pawn p = NewHuman();
            p.skills.GetSkill(SkillDefOf.Medicine)!.Level = 18;
            float skilled = p.GetStatValue(StatDefOf.MedicalTendQuality);
            Assert.True(skilled > 0.9f);

            p.story.traits.GainTrait(new Trait(noMedicine, 0));
            p.Notify_TraitsChanged();

            Assert.True(p.skills.GetSkill(SkillDefOf.Medicine)!.TotallyDisabled);

            // TotallyDisabled forces SkillRecord.Level to 0 regardless of the underlying levelInt (still 18),
            // so the skill-need pipeline should land on the table's own level-0 entry (base(1) * 0.30), the
            // same number a freshly generated, never-trained pawn gets — not the skilled value from a moment
            // ago and not some other placeholder.
            Assert.Equal(0.30f, p.GetStatValue(StatDefOf.MedicalTendQuality), 3);
            Assert.True(p.GetStatValue(StatDefOf.MedicalTendQuality) < skilled);
        }
    }
}
