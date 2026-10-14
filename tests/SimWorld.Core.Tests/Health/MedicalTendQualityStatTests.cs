using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;

using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// How good a tender is (lane: post-raid bleed). The stat was a hand-written table, 0.30 at skill 0 to
    /// 1.00 at skill 20, and RimWorld's is "20% plus 10% per skill level" run through a post-process curve:
    /// a skill-5 doctor is 0.70, skill 8 is 1.00, skill 20 is 1.55 (the stat's XML cites the page). The
    /// numbers below are that page's table; the properties are the ones the tend path depends on.
    /// </summary>
    public class MedicalTendQualityStatTests : ContentTestBase
    {
        public MedicalTendQualityStatTests(CoreContentFixture content) : base(content)
        {
        }

        private static float AtSkill(int level)
        {
            Pawn p = NewHuman("Doctor");
            p.skills.GetSkill(SkillDefOf.Medicine)!.Level = level;
            return p.GetStatValue(StatDefOf.MedicalTendQuality);
        }

        [Fact]
        public void The_stat_follows_the_wiki_table_for_the_Medicine_skill()
        {
            // https://rimworldwiki.com/wiki/Medical_tend_quality, "Medical Tend Quality" column.
            Assert.Equal(0.20f, AtSkill(0), 3);
            Assert.Equal(0.30f, AtSkill(1), 3);
            Assert.Equal(0.70f, AtSkill(5), 3);
            Assert.Equal(1.00f, AtSkill(8), 3);
            Assert.Equal(1.05f, AtSkill(9), 3);
            Assert.Equal(1.10f, AtSkill(10), 3);
            Assert.Equal(1.35f, AtSkill(15), 3);
            Assert.Equal(1.55f, AtSkill(20), 3);
        }

        [Fact]
        public void A_middling_doctor_tends_far_better_than_the_old_table_allowed_and_the_top_flattens()
        {
            // The old table gave skill 5 0.475 and skill 10 0.65. Above skill 8 each level adds half what a
            // level below it adds (the post-process curve), so the top end does not run away.
            Assert.True(AtSkill(5) > 0.6f);
            Assert.True(AtSkill(10) > 1.0f);
            float lowStep = AtSkill(4) - AtSkill(3);
            float highStep = AtSkill(14) - AtSkill(13);
            Assert.True(highStep < lowStep * 0.75f, "low step " + lowStep + ", high step " + highStep);
        }

        [Fact]
        public void A_doctor_who_has_lost_the_use_of_her_hands_tends_worse()
        {
            // RimWorld: Manipulation at 100% importance. Both arms gone, the stat collapses.
            Pawn whole = NewHuman("Whole");
            Pawn maimed = NewHuman("Maimed");
            whole.skills.GetSkill(SkillDefOf.Medicine)!.Level = 10;
            maimed.skills.GetSkill(SkillDefOf.Medicine)!.Level = 10;
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            foreach (string arm in new[] { "left arm", "right arm" })
            {
                cut.Worker.Apply(new DamageInfo(cut, 500f, hitPart: maimed.RaceProps.body!.GetPartByLabel(arm)!), maimed);
            }

            Assert.True(maimed.GetStatValue(StatDefOf.MedicalTendQuality) < whole.GetStatValue(StatDefOf.MedicalTendQuality) * 0.5f);
        }

        [Fact]
        public void The_stat_carries_the_capacity_factors_RimWorld_gives_it()
        {
            StatDef stat = StatDefOf.MedicalTendQuality;
            Assert.NotNull(stat.capacityFactors);
            Assert.Contains(stat.capacityFactors!, f => f.capacity == PawnCapacityDefOf.Manipulation && f.weight >= 0.99f);
            Assert.Contains(stat.capacityFactors!, f => f.capacity == PawnCapacityDefOf.Sight && f.weight > 0f && f.weight < 1f);
            Assert.NotNull(stat.postProcessCurve);
        }
    }
}
