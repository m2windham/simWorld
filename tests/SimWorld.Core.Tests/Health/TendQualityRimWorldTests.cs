using System.Linq;
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
    /// How good a tend is, against RimWorld's own numbers.
    ///
    /// <para/>Found tracing why a threat-free founding band died of wound infections
    /// (<c>docs/perf/storyteller-pressure</c>): every traced tend ran at 4% to 14% quality, and an infected
    /// patient resting in bed needs about 15% to live. Two things held it down. The doctor's skill was read
    /// off an invented table worth roughly half of RimWorld's for a competent doctor; and each tend's random
    /// spread was clamped to the tend's own base instead of the medicine's ceiling, so the spread could only
    /// ever subtract.
    /// </summary>
    public class TendQualityRimWorldTests : ContentTestBase
    {
        public TendQualityRimWorldTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>rimworldwiki.com/wiki/Medical_Tend_Quality, "Full Post-Processing Curve Table".</summary>
        [Theory]
        [InlineData(0, 0.20f)]
        [InlineData(1, 0.30f)]
        [InlineData(4, 0.60f)]
        [InlineData(8, 1.00f)]
        [InlineData(10, 1.10f)]
        [InlineData(18, 1.50f)]
        [InlineData(20, 1.55f)]
        public void Medical_tend_quality_follows_RimWorlds_table(int medicine, float expected)
        {
            Pawn doctor = NewHuman();
            doctor.skills.GetSkill(SkillDefOf.Medicine)!.Level = medicine;

            Assert.Equal(expected, doctor.GetStatValue(StatDefOf.MedicalTendQuality), 3);
        }

        /// <summary>
        /// A level-8 doctor with no medicine tends at a base of 1.00 x 0.3 = 0.30; RimWorld rolls each tend
        /// within 0.25 either side of that and clamps to the no-medicine ceiling of 0.70. Over many tends
        /// some must come out above the base and the mean must sit near it.
        /// </summary>
        [Fact]
        public void A_tend_rolls_either_side_of_its_base_and_only_the_medicines_ceiling_caps_it()
        {
            Pawn doctor = NewHuman("Doctor");
            doctor.skills.GetSkill(SkillDefOf.Medicine)!.Level = 8;
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");

            float baseQuality = TendUtility.CalculateBaseTendQuality(doctor, null, medicineDef: null);
            Assert.Equal(0.30f, baseQuality, 3);

            const int Tends = 300;
            int above = 0;
            float sum = 0f;
            for (int i = 0; i < Tends; i++)
            {
                Pawn patient = NewHuman("Patient" + i);
                cut.Worker.Apply(new DamageInfo(cut, 6f), patient);
                var tended = TendUtility.DoTendWithMedicine(doctor, patient, medicine: null);
                Assert.NotEmpty(tended);

                float quality = tended.First().TendQuality;
                Assert.InRange(quality, 0f, TendUtility.MaxQualityNoMedicine);
                if (quality > baseQuality + 0.01f) above++;
                sum += quality;
            }

            Assert.InRange(above, Tends / 4, Tends);
            Assert.InRange(sum / Tends, 0.25f, 0.35f);
        }
    }
}
