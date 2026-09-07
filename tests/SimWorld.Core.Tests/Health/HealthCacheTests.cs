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
    }
}
