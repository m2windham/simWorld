using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.MindState
{
    public class MentalBreakTests : ContentTestBase
    {
        public MentalBreakTests(CoreContentFixture content) : base(content)
        {
        }

        private static MentalStateDef State(string name) => DefDatabase<MentalStateDef>.GetNamed(name);

        [Fact]
        public void Thresholds_derive_from_the_pawns_break_threshold()
        {
            Pawn p = NewHuman();
            MentalBreaker breaker = p.mindState.mentalBreaker;
            Assert.Equal(0.35f, breaker.BreakThresholdMinor);
            Assert.Equal(0.20f, breaker.BreakThresholdMajor, 5);
            Assert.Equal(0.05f, breaker.BreakThresholdExtreme, 5);

            p.needs.mood!.CurLevel = 0.5f;
            Assert.Equal(MentalBreakIntensity.None, breaker.CurMoodBreakIntensity);
            p.needs.mood.CurLevel = 0.34f;
            Assert.Equal(MentalBreakIntensity.Minor, breaker.CurMoodBreakIntensity);
            p.needs.mood.CurLevel = 0.19f;
            Assert.Equal(MentalBreakIntensity.Major, breaker.CurMoodBreakIntensity);
            p.needs.mood.CurLevel = 0.04f;
            Assert.Equal(MentalBreakIntensity.Extreme, breaker.CurMoodBreakIntensity);
        }

        [Fact]
        public void Possible_breaks_match_intensity_and_trait_requirements()
        {
            Pawn p = NewHuman();
            p.needs.mood!.CurLevel = 0.1f;
            List<string> major = p.mindState.mentalBreaker.CurrentPossibleMoodBreaks().Select(b => b.defName).ToList();
            Assert.Contains("Tantrum", major);
            Assert.DoesNotContain("FireStartingSpree", major);
            Assert.DoesNotContain("SadisticRage", major);
            Assert.DoesNotContain("Berserk", major);

            p.story.traits.GainTrait(new Trait(Trait("Pyromaniac")));
            Assert.Contains("FireStartingSpree", p.mindState.mentalBreaker.CurrentPossibleMoodBreaks().Select(b => b.defName));

            p.needs.mood.CurLevel = 0.01f;
            List<string> extreme = p.mindState.mentalBreaker.CurrentPossibleMoodBreaks().Select(b => b.defName).ToList();
            Assert.Equal(4, extreme.Count);
            Assert.Contains("Berserk", extreme);
        }

        [Fact]
        public void Trait_data_can_restrict_breaks_to_an_allowed_set()
        {
            Pawn p = NewHuman();
            TraitDef pyro = Trait("Pyromaniac");
            pyro.DataAtDegree(0).theOnlyAllowedMentalBreaks = new List<MentalBreakDef> { DefDatabase<MentalBreakDef>.GetNamed("FireStartingSpree") };
            try
            {
                p.story.traits.GainTrait(new Trait(pyro));
                p.needs.mood!.CurLevel = 0.1f;
                Assert.Equal(new[] { "FireStartingSpree" }, p.mindState.mentalBreaker.CurrentPossibleMoodBreaks().Select(b => b.defName));
            }
            finally
            {
                pyro.DataAtDegree(0).theOnlyAllowedMentalBreaks = null;
            }
        }

        [Fact]
        public void A_pawn_kept_in_despair_breaks_after_the_grace_period_and_recovers_with_catharsis()
        {
            Pawn p = NewHuman();
            // Starving, exhausted, recreation-starved and depressive: the mood target is 0.
            p.story.traits.GainTrait(new Trait(Trait("NaturalMood"), -2));
            p.needs.food!.CurLevel = 0f;
            p.needs.rest!.CurLevel = 0f;
            p.needs.joy!.CurLevel = 0f;
            p.needs.mood!.CurLevel = 0f;
            Assert.Equal(0f, p.needs.mood.CurInstantLevel);

            RunTicks(MentalBreaker.MinTicksBelowToBreak, p);
            Assert.False(p.InMentalState);
            Assert.True(p.mindState.mentalBreaker.TicksBelowExtreme > 0);

            int brokeAt = -1;
            for (int t = 0; t < 10 * GenDate.TicksPerDay && brokeAt < 0; t += 150)
            {
                RunTicks(150, p);
                if (p.InMentalState) brokeAt = Find.TickManager.TicksGame;
            }
            Assert.True(brokeAt > MentalBreaker.MinTicksBelowToBreak, "no break within ten days");
            MentalStateDef broke = p.MentalStateDef!;
            Assert.Equal(MentalBreakIntensity.Extreme, DefDatabase<MentalBreakDef>.AllDefs.Single(b => b.mentalState == broke).intensity);
            Assert.True(p.mindState.mentalStateHandler.CurState!.causedByMood);

            RunTicks(broke.maxTicksBeforeRecovery + 1, p);
            Assert.False(p.InMentalState);
            Assert.Contains(p.needs.mood.thoughts.memories.Memories, m => m.def.defName == "Catharsis");
            // Immunity was armed at 15000 on recovery and has been counting down since.
            Assert.InRange(p.mindState.mentalBreaker.TicksUntilCanDoMentalBreak, MentalBreaker.MinTicksSinceRecoveryToBreak - broke.maxTicksBeforeRecovery, MentalBreaker.MinTicksSinceRecoveryToBreak);
            Assert.True(p.mindState.mentalBreaker.TicksUntilCanDoMentalBreak > 0);
        }

        [Fact]
        public void Mental_states_end_between_min_and_max_ticks()
        {
            Pawn p = NewHuman();
            MentalStateDef wander = State("Wander_Sad");
            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(wander, "test"));
            Assert.False(p.mindState.mentalStateHandler.TryStartMentalState(wander, "again"));
            Assert.Equal("test", p.mindState.mentalStateHandler.CurState!.reason);

            RunTicks(wander.minTicksBeforeRecovery - 1, p);
            Assert.True(p.InMentalState);
            RunTicks(wander.maxTicksBeforeRecovery - wander.minTicksBeforeRecovery + 2, p);
            Assert.False(p.InMentalState);
            Assert.DoesNotContain(p.needs.mood!.thoughts.memories.Memories, m => m.def.defName == "Catharsis");
        }

        [Fact]
        public void Sleep_and_downing_end_states_that_allow_it()
        {
            Pawn p = NewHuman();
            p.mindState.mentalStateHandler.TryStartMentalState(State("Wander_Sad"));
            p.Asleep = true;
            RunTicks(1, p);
            Assert.False(p.InMentalState);

            p.Asleep = false;
            p.mindState.mentalStateHandler.TryStartMentalState(State("Catatonic"));
            p.Downed = true;
            RunTicks(1, p);
            Assert.True(p.InMentalState);
            p.mindState.mentalStateHandler.ClearMentalStateDirect();
            p.Downed = false;

            p.mindState.mentalStateHandler.TryStartMentalState(State("Berserk"));
            p.Downed = true;
            RunTicks(1, p);
            Assert.False(p.InMentalState);
        }

        [Fact]
        public void Downed_or_dead_pawns_cannot_start_unforced_states()
        {
            Pawn p = NewHuman();
            p.Downed = true;
            Assert.False(p.mindState.mentalStateHandler.TryStartMentalState(State("Wander_Sad")));
            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(State("Wander_Sad"), forced: true));
        }

        [Fact]
        public void Being_in_a_mental_state_adds_the_losing_it_thought()
        {
            Pawn p = NewHuman();
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            Assert.Equal(0f, thoughts.TotalMoodOffset());
            p.mindState.mentalStateHandler.TryStartMentalState(State("Wander_Sad"));
            Assert.Equal(-5f, thoughts.TotalMoodOffset());
            var groups = new List<Thought>();
            thoughts.GetDistinctMoodThoughtGroups(groups);
            Assert.False(groups[0].VisibleInNeedsTab);
        }

        [Fact]
        public void Weighted_selection_respects_weights()
        {
            var rand = new RandomStream(3);
            var items = new List<string> { "never", "rare", "common" };
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < 4000; i++)
            {
                string pick = GenCollection.RandomElementByWeight(items, s => s == "never" ? 0f : (s == "rare" ? 1f : 3f), rand);
                counts[pick] = counts.TryGetValue(pick, out int c) ? c + 1 : 1;
            }
            Assert.False(counts.ContainsKey("never"));
            Assert.InRange(counts["rare"], 850, 1150);
            Assert.False(GenCollection.TryRandomElementByWeight(items, _ => 0f, rand, out _));
        }
    }
}
