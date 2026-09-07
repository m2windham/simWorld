using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Stats
{
    /// <summary>
    /// The five Pawn properties the Stats module retired from hard-coded constants to real stat lookups
    /// (RestRateMultiplier, ImmunityGainSpeed, PainShockThreshold, GlobalLearningFactor, MentalBreakThreshold),
    /// plus Armor.ArmorRating now reading through the same pipeline.
    /// </summary>
    public class PawnStatsTests : ContentTestBase
    {
        public PawnStatsTests(CoreContentFixture content) : base(content)
        {
        }

        private static BodyPartRecord Part(Pawn p, string label) =>
            p.RaceProps.body!.GetPartByLabel(label) ?? throw new InvalidOperationException("no part " + label);

        private static DamageResult Hit(Pawn p, string damage, float amount, string partLabel)
        {
            DamageDef def = DefDatabase<DamageDef>.GetNamed(damage);
            return def.Worker.Apply(new DamageInfo(def, amount, hitPart: Part(p, partLabel)), p);
        }

        // ---- defaults unchanged from the retired hard-coded values ----

        [Fact]
        public void A_healthy_humans_stat_backed_properties_match_their_old_hard_coded_defaults()
        {
            Pawn p = NewHuman();

            Assert.Equal(1f, p.RestRateMultiplier, 4);
            Assert.Equal(1f, p.ImmunityGainSpeed, 4);
            Assert.Equal(0.8f, p.PainShockThreshold, 4);
            Assert.Equal(1f, p.GlobalLearningFactor, 4);
            Assert.Equal(0.35f, p.MentalBreakThreshold, 4);
        }

        [Fact]
        public void The_five_properties_are_real_stat_lookups()
        {
            Pawn p = NewHuman();

            Assert.Equal(p.GetStatValue(StatDefOf.RestRateMultiplier), p.RestRateMultiplier);
            Assert.Equal(p.GetStatValue(StatDefOf.ImmunityGainSpeed), p.ImmunityGainSpeed);
            Assert.Equal(p.GetStatValue(StatDefOf.PainShockThreshold), p.PainShockThreshold);
            Assert.Equal(p.GetStatValue(StatDefOf.GlobalLearningFactor), p.GlobalLearningFactor);
            Assert.Equal(p.GetStatValue(StatDefOf.MentalBreakThreshold), p.MentalBreakThreshold);
        }

        // ---- capacity factors: a wounded pawn's stat degrades, and recovers on healing ----

        [Fact]
        public void RestRateMultiplier_falls_when_the_heart_is_hurt_and_recovers_when_healed()
        {
            Pawn p = NewHuman();
            Assert.Equal(1f, p.RestRateMultiplier, 4);

            // Heart carries BloodPumpingSource and has 15 hit points; a non-destroying hit leaves it partly
            // working, so BloodPumping (one of RestRateMultiplier's three capacityFactors) falls but the pawn
            // is nowhere near the lethal (BloodPumping == 0) threshold.
            Hit(p, "Blunt", 5f, "heart");
            float wounded = p.RestRateMultiplier;
            Assert.True(wounded < 1f, "expected RestRateMultiplier to fall below 1 with a hurt heart, was " + wounded);
            Assert.False(p.Dead);

            foreach (Hediff_Injury injury in p.health.hediffSet.GetHediffs<Hediff_Injury>().ToList())
            {
                injury.Heal(30f);
            }

            Assert.Equal(1f, p.RestRateMultiplier, 4);
        }

        // ---- Armor.ArmorRating now reads through the pipeline, so a hediff can move it ----

        [Fact]
        public void ArmorRating_is_zero_by_default_and_moved_by_a_hediff_statOffset()
        {
            Pawn p = NewHuman();
            StatDef armorStat = DefDatabase<StatDef>.GetNamed("ArmorRating_Blunt");
            var natural = new NaturalArmor(p);

            Assert.Equal(0f, natural.ArmorRating(armorStat, Part(p, "torso")));

            var toughened = new HediffDef
            {
                defName = "TestToughened",
                stages = new List<HediffStage>
                {
                    new HediffStage { statOffsets = new List<StatModifier> { new StatModifier(armorStat, 0.2f) } },
                },
            };
            HealthUtility.AdjustSeverity(p, toughened, 1f);

            Assert.Equal(0.2f, natural.ArmorRating(armorStat, Part(p, "torso")), 4);
        }
    }
}
