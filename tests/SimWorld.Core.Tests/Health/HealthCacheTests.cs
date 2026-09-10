using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// The per-tick death check reads cached core-part efficiency and cached injury severity instead of
    /// recomputing both every tick (docs/perf/baseline.md §7). A cache is only as good as its invalidation, and
    /// getting it wrong here does not produce a stale number on a screen — it produces a pawn that fails to die
    /// when it should, or dies when it should not. These tests pin each input that must dirty it.
    /// </summary>
    public class HealthCacheTests : ContentTestBase
    {
        public HealthCacheTests(CoreContentFixture content) : base(content)
        {
        }

        private static HediffDef Cut => DefDatabase<HediffDef>.GetNamed("Cut");

        [Fact]
        public void Injuring_a_pawn_moves_the_cached_core_efficiency()
        {
            Pawn pawn = NewHuman();
            float before = pawn.health.hediffSet.CorePartEfficiency;

            BodyPartRecord torso = pawn.RaceProps.body!.corePart!;
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(Cut, pawn, torso);
            injury.Severity = 12f;
            pawn.health.AddHediff(injury, torso, null);

            Assert.True(pawn.health.hediffSet.CorePartEfficiency < before,
                "a wound to the core part must lower its cached efficiency");
        }

        [Fact]
        public void Healing_restores_the_cached_core_efficiency()
        {
            Pawn pawn = NewHuman();
            float healthy = pawn.health.hediffSet.CorePartEfficiency;

            BodyPartRecord torso = pawn.RaceProps.body!.corePart!;
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(Cut, pawn, torso);
            injury.Severity = 12f;
            pawn.health.AddHediff(injury, torso, null);
            Assert.True(pawn.health.hediffSet.CorePartEfficiency < healthy);

            pawn.health.hediffSet.Remove(injury);

            Assert.Equal(healthy, pawn.health.hediffSet.CorePartEfficiency, 4);
        }

        [Fact]
        public void Growing_up_refreshes_the_cached_efficiency_of_an_injured_pawn()
        {
            // The subtle one. Part max health is hitPoints * HealthScale, and HealthScale comes from the life
            // stage — so an injured pawn's core efficiency changes when it crosses a stage boundary with no
            // hediff changing at all. A cache dirtied only by hediff mutations would keep a child's value for
            // the rest of that pawn's life.
            Pawn pawn = NewHuman();
            pawn.ageTracker.DebugSetAge(5f);

            BodyPartRecord torso = pawn.RaceProps.body!.corePart!;
            var injury = (Hediff_Injury)HediffMaker.MakeHediff(Cut, pawn, torso);
            injury.Severity = 8f;
            pawn.health.AddHediff(injury, torso, null);

            float asChild = pawn.health.hediffSet.CorePartEfficiency;

            pawn.ageTracker.DebugSetAge(30f);
            _ = pawn.ageTracker.CurLifeStage; // reading the stage is what notices the crossing

            float asAdult = pawn.health.hediffSet.CorePartEfficiency;

            Assert.NotEqual(asChild, asAdult, 4);
            Assert.True(asAdult > asChild,
                "the same wound is a smaller fraction of an adult's larger part, so efficiency should rise");
        }

        [Fact]
        public void The_life_stage_cache_survives_age_moving_backwards()
        {
            // A cache keyed only on "when does the next stage start" happily keeps an adult stage for a pawn
            // set back to five. Both bounds, or neither.
            Pawn pawn = NewHuman();

            pawn.ageTracker.DebugSetAge(30f);
            Assert.Equal("HumanlikeAdult", pawn.ageTracker.CurLifeStage!.defName);

            pawn.ageTracker.DebugSetAge(5f);
            Assert.Equal("HumanlikeChild", pawn.ageTracker.CurLifeStage!.defName);

            pawn.ageTracker.DebugSetAge(30f);
            Assert.Equal("HumanlikeAdult", pawn.ageTracker.CurLifeStage!.defName);
        }

        [Fact]
        public void A_pawn_still_dies_when_the_cached_check_says_it_should()
        {
            // The cache must not save anyone's life by accident.
            Pawn pawn = NewHuman();
            BodyPartRecord torso = pawn.RaceProps.body!.corePart!;

            for (int i = 0; i < 6 && !pawn.Dead; i++)
            {
                var injury = (Hediff_Injury)HediffMaker.MakeHediff(Cut, pawn, torso);
                injury.Severity = 20f;
                pawn.health.AddHediff(injury, torso, null);
            }

            Assert.True(pawn.Dead, "enough damage to the core part has to kill the pawn");
        }

        // ---- stage-aware dirtying (docs/perf/baseline.md §10) ----
        //
        // HediffComp_Immunizable nudges a disease's Severity every tick it isn't fully immune, which used to
        // dirty this whole cache 60,000 times a day even though almost none of those nudges cross a stage
        // boundary (the only thing that can move pain, bleed rate, core-part efficiency, capacity levels or
        // total injury severity for anything but an injury). The tests below pin that the skip is exact: every
        // within-stage nudge still reads correctly, and the one nudge that does cross a boundary is never missed
        // — through direct Severity assignment and, more importantly, through the real tick-by-tick drift a
        // disease actually produces, all the way to the death it should cause.

        private static void AssertCacheMatchesFreshRecompute(Pawn pawn, PawnCapacityDef capacity)
        {
            HediffSet set = pawn.health.hediffSet;
            Assert.Equal(set.CalculatePain(), set.PainTotal, 5);
            Assert.Equal(set.CalculateBleedRate(), set.BleedRateTotal, 5);
            Assert.Equal(PawnCapacityUtility.CalculatePartEfficiency(set, pawn.RaceProps.body!.corePart!), set.CorePartEfficiency, 5);
            Assert.Equal(set.TotalInjurySeverity(), set.TotalInjurySeverityCached, 5);
            Assert.Equal(PawnCapacityUtility.CalculateCapacityLevel(set, capacity), pawn.health.capacities.GetLevel(capacity), 5);
        }

        [Fact]
        public void A_within_stage_severity_nudge_leaves_cached_values_correct_and_a_stage_crossing_updates_them()
        {
            Pawn pawn = NewHuman();
            Hediff infection = pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("WoundInfection"));
            PawnCapacityDef consciousness = DefDatabase<PawnCapacityDef>.GetNamed("Consciousness");

            AssertCacheMatchesFreshRecompute(pawn, consciousness);
            Assert.Equal(0, infection.CurStageIndex);
            float consciousnessBeforeMajor = pawn.health.capacities.GetLevel(consciousness);

            // Many small nudges, exactly the shape HediffComp_Immunizable produces, that all stay inside
            // stage 0 ("minor", minSeverity 0..0.33) — none of them may corrupt the cache.
            int steps = 0;
            while (infection.CurStageIndex == 0 && steps < 1000)
            {
                infection.Severity += 0.005f;
                AssertCacheMatchesFreshRecompute(pawn, consciousness);
                steps++;
            }

            // The nudge that finally crosses into stage 1 ("major", capMods Consciousness -0.05) must still
            // be caught, even though it looked exactly like every nudge before it.
            Assert.Equal(1, infection.CurStageIndex);
            Assert.True(pawn.health.capacities.GetLevel(consciousness) < consciousnessBeforeMajor,
                "crossing into the major stage must lower consciousness, and the cache must show it");
        }

        [Fact]
        public void An_untended_infections_cached_values_track_gradual_per_tick_drift_all_the_way_to_death()
        {
            // An untended WoundInfection's severityPerDayNotImmune (0.84) outruns immunityPerDaySick (0.7) by
            // design (see the doc comment on Hediffs_Diseases_Infectious.xml), so this pawn should die of it —
            // entirely through HediffComp_Immunizable's per-tick nudges, never one big jump. If the stage-aware
            // skip above ever missed a real crossing, the symptom would be exactly this: a pawn that fails to
            // die, or dies with the wrong capacity levels leading up to it.
            Pawn pawn = NewHuman();
            HediffDef infectionDef = DefDatabase<HediffDef>.GetNamed("WoundInfection");
            Hediff infection = pawn.health.AddHediff(infectionDef);
            PawnCapacityDef consciousness = DefDatabase<PawnCapacityDef>.GetNamed("Consciousness");

            bool sawMajor = false;
            bool sawExtreme = false;
            for (int chunk = 0; chunk < 300 && !pawn.Dead; chunk++)
            {
                RunTicks(1000, pawn);
                if (pawn.Dead) break;

                AssertCacheMatchesFreshRecompute(pawn, consciousness);

                int stage = infection.CurStageIndex;
                if (stage == 1) sawMajor = true;
                if (stage == 2) sawExtreme = true;
            }

            Assert.True(pawn.Dead, "an untended infection outruns immunity by design and should eventually kill");
            Assert.True(sawMajor, "the run must have actually passed through the major stage on the way");
            Assert.True(sawExtreme, "the run must have actually passed through the extreme stage on the way");
            Assert.Same(infectionDef, pawn.health.DeathCauseHediff);
        }
    }
}
