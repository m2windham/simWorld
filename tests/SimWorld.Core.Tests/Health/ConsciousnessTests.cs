using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// <see cref="PawnCapacityWorker_Consciousness"/> against RimWorld's own formula (decompiled source,
    /// <c>josh-m/RW-Decompile</c>, <c>RimWorld/PawnCapacityWorker_Consciousness.cs</c>): pain is subtracted and
    /// capped, not multiplied in outright, and BloodPumping/Breathing/BloodFiltration are blended in with Lerp
    /// weights 0.2/0.2/0.1 rather than multiplied straight through. Before this, a single badly damaged one of
    /// those three could wipe consciousness out on its own — the reason most citizens bleeding out after a raid
    /// collapsed to a lethal consciousness around 45% blood loss instead of RimWorld's 100%
    /// (docs/perf/tend/README.md, "Not fixed here"; <c>HealthTests.Blood_loss_stages_apply_capacity_modifiers</c>
    /// and <c>Pain_lowers_consciousness</c> pin the sourced numbers this produces).
    /// </summary>
    public class ConsciousnessTests : ContentTestBase
    {
        public ConsciousnessTests(CoreContentFixture content) : base(content)
        {
        }

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        private static float Level(Pawn p, string capacity) => p.health.capacities.GetLevel(DefDatabase<PawnCapacityDef>.GetNamed(capacity));

        private static void Stab(Pawn p, string part, float amount) =>
            DefDatabase<DamageDef>.GetNamed("Stab").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Stab"), amount, hitPart: Part(p, part)), p);

        private static void Cut(Pawn p, string part, float amount) =>
            DefDatabase<DamageDef>.GetNamed("Cut").Worker.Apply(new DamageInfo(DefDatabase<DamageDef>.GetNamed("Cut"), amount, hitPart: Part(p, part)), p);

        // ---- the blend: a single degraded capacity cannot wipe consciousness out on its own ----

        [Fact]
        public void A_wounded_heart_alone_costs_far_less_consciousness_than_its_own_efficiency_loss()
        {
            Pawn p = NewHuman();
            Stab(p, "heart", 3f); // heart's max HP is 15 (Destroying_the_heart_kills uses 15f to destroy it outright)
            float bloodPumping = Level(p, "BloodPumping");
            Assert.Equal(0.8f, bloodPumping, 3); // 12/15 remaining

            float consciousness = Level(p, "Consciousness");
            // The old code multiplied consciousness by BloodPumping outright, so a 20% loss there was a flat
            // 20% loss here too (consciousness == bloodPumping, pain being ~0 for a single small stab). RimWorld
            // instead blends it in at a 0.2 weight: Lerp(1, 1*0.8, 0.2) = 0.96, only a 4% loss.
            Assert.True(consciousness > bloodPumping,
                $"a single damaged capacity must cost far less than its own loss once blended in, not be multiplied straight through (consciousness {consciousness}, bloodPumping {bloodPumping})");
            Assert.Equal(0.96f, consciousness, 3);
            Assert.False(p.Downed);
        }

        [Fact]
        public void Pain_below_RimWorlds_own_floor_costs_no_consciousness_at_all()
        {
            Pawn p = NewHuman();
            Cut(p, "torso", 5f); // small enough to stay under 0.1 total pain
            Assert.True(p.health.hediffSet.PainTotal < 0.1f, $"fixture assumption: pain {p.health.hediffSet.PainTotal} must be under RimWorld's 0.1 floor");
            Assert.Equal(1f, Level(p, "Consciousness"), 4);
        }

        // ---- blood loss alone: downed well before dead, dead only at RimWorld's own lethalSeverity ----

        [Fact]
        public void Blood_loss_alone_downs_an_otherwise_healthy_pawn_long_before_it_can_kill_them()
        {
            Pawn p = NewHuman();
            Hediff bloodLoss = p.health.AddHediff(HediffDefOf.BloodLoss);
            bloodLoss.Severity = 0.99f; // one tick short of RimWorld's lethalSeverity (1.0)
            Assert.True(p.Downed, "extreme blood loss (>= 0.6) caps consciousness at 0.1, below the 0.3 awake threshold");
            Assert.False(p.Dead, "consciousness never reaches lethal (0) from blood loss alone — only the hediff's own lethalSeverity kills");
            bloodLoss.Severity = 1f;
            Assert.True(p.Dead);
            Assert.Same(HediffDefOf.BloodLoss, p.health.DeathCauseHediff);
        }

        [Fact]
        public void Consciousness_from_blood_loss_alone_is_never_below_the_extreme_stages_own_cap()
        {
            // A trend, not a magic-number sweep: as severity climbs from 0 to just under lethal, consciousness
            // (otherwise-healthy pawn, so this is purely BloodLoss's own capMods) only ever falls, and never
            // drops below the 0.1 the extreme stage caps it at — it must not be multiplied down further by
            // anything else, because for this pawn there is nothing else degraded to multiply by.
            Pawn p = NewHuman();
            Hediff bloodLoss = p.health.AddHediff(HediffDefOf.BloodLoss);
            float previous = 1f;
            for (float s = 0.05f; s < 1f; s += 0.05f)
            {
                bloodLoss.Severity = s;
                float level = Level(p, "Consciousness");
                Assert.True(level <= previous, $"consciousness must not rise as blood loss climbs (severity {s})");
                Assert.True(level >= 0.1f - 1e-4f, $"consciousness must not fall below the extreme stage's own 0.1 cap (severity {s}, level {level})");
                previous = level;
            }
        }
    }
}
