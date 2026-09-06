using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Needs
{
    public class MoodTests : ContentTestBase
    {
        public MoodTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThoughtDef Thought(string name) => DefDatabase<ThoughtDef>.GetNamed(name);

        [Fact]
        public void Mood_target_is_half_plus_thought_total_and_is_sought_at_the_def_rate()
        {
            Pawn p = NewHuman();
            Need_Mood mood = p.needs.mood!;
            Assert.Equal(0.5f, mood.CurLevel);
            Assert.Equal(0.5f, mood.CurInstantLevel);

            mood.thoughts.memories.TryGainMemory(Thought("AteFineMeal"));
            Assert.Equal(5f, mood.thoughts.TotalMoodOffset());
            Assert.Equal(0.55f, mood.CurInstantLevel);

            RunTicks(GenDate.TicksPerHour / 2, p);
            Assert.InRange(mood.CurLevel, 0.52f, 0.53f);
            RunTicks(GenDate.TicksPerHour, p);
            Assert.Equal(0.55f, mood.CurLevel, 3);
        }

        [Fact]
        public void Memories_expire_after_their_duration()
        {
            Pawn p = NewHuman();
            MemoryThoughtHandler memories = p.needs.mood!.thoughts.memories;
            memories.TryGainMemory(Thought("AteWithoutTable"));
            Assert.Single(memories.Memories);
            RunTicks(GenDate.TicksPerDay - 300, p);
            Assert.Single(memories.Memories);
            RunTicks(600, p);
            Assert.Empty(memories.Memories);
        }

        [Fact]
        public void Stacked_thoughts_use_the_geometric_stacking_rule()
        {
            Pawn p = NewHuman();
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            ThoughtDef insulted = Thought("Insulted");
            for (int i = 0; i < 3; i++) thoughts.memories.TryGainMemory(insulted);

            // average -5 × (1 + 0.75 + 0.75²)
            Assert.Equal(-5f * (1f + 0.75f + 0.5625f), thoughts.TotalMoodOffset(), 3);
            var groups = new List<Thought>();
            thoughts.GetDistinctMoodThoughtGroups(groups);
            Assert.Single(groups);
        }

        [Fact]
        public void Adding_past_the_stack_limit_renews_the_oldest_instead()
        {
            Pawn p = NewHuman();
            MemoryThoughtHandler memories = p.needs.mood!.thoughts.memories;
            ThoughtDef table = Thought("AteWithoutTable");
            memories.TryGainMemory(table);
            Thought_Memory first = memories.Memories[0];
            first.age = 40000;

            Thought_Memory? second = memories.TryGainMemory(table);

            Assert.Null(second);
            Assert.Single(memories.Memories);
            Assert.Same(first, memories.Memories[0]);
            Assert.Equal(0, first.age);
        }

        [Fact]
        public void Memories_about_different_pawns_are_separate_groups()
        {
            Pawn p = NewHuman("P");
            Pawn x = NewHuman("X");
            Pawn y = NewHuman("Y");
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            ThoughtDef insulted = Thought("Insulted");
            thoughts.memories.TryGainMemory(insulted, x);
            thoughts.memories.TryGainMemory(insulted, y);
            var groups = new List<Thought>();
            thoughts.GetDistinctMoodThoughtGroups(groups);
            Assert.Equal(2, groups.Count);
            Assert.Equal(-10f, thoughts.TotalMoodOffset());
        }

        [Fact]
        public void Nullifying_traits_block_memories_and_required_traits_gate_situational_thoughts()
        {
            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(Trait("Psychopath")));
            Assert.Null(p.needs.mood!.thoughts.memories.TryGainMemory(Thought("WitnessedDeathAlly")));

            Pawn q = NewHuman("Q");
            Assert.Equal(0f, q.needs.mood!.thoughts.TotalMoodOffset());
            q.story.traits.GainTrait(new Trait(Trait("NaturalMood"), 1));
            q.needs.mood.thoughts.situational.Notify_SituationalThoughtsDirty();
            Assert.Equal(6f, q.needs.mood.thoughts.TotalMoodOffset());

            Pawn r = NewHuman("R");
            r.story.traits.GainTrait(new Trait(Trait("NaturalMood"), -2));
            Assert.Equal(-12f, r.needs.mood!.thoughts.TotalMoodOffset());
        }

        [Fact]
        public void Situational_need_thoughts_track_the_need_category()
        {
            Pawn p = NewHuman();
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            Need_Food food = p.needs.food!;

            float Offset()
            {
                thoughts.situational.Notify_SituationalThoughtsDirty();
                return thoughts.TotalMoodOffset();
            }

            Assert.Equal(0f, Offset());
            food.CurLevel = 0.25f;
            Assert.Equal(-6f, Offset());
            food.CurLevel = 0.1f;
            Assert.Equal(-12f, Offset());
            food.CurLevel = 0f;
            Assert.Equal(-20f, Offset());
            food.CurLevel = 0.8f;
            Assert.Equal(0f, Offset());
        }

        [Fact]
        public void Situational_thoughts_are_cached_for_ten_ticks()
        {
            Pawn p = NewHuman();
            ThoughtHandler thoughts = p.needs.mood!.thoughts;
            Assert.Equal(0f, thoughts.TotalMoodOffset());
            p.needs.food!.CurLevel = 0f;
            Assert.Equal(0f, thoughts.TotalMoodOffset());
            for (int i = 0; i < SituationalThoughtHandler.RecalculateIntervalTicks; i++) Find.TickManager.DoSingleTick();
            Assert.Equal(-20f, thoughts.TotalMoodOffset());
        }

        [Fact]
        public void Ascetic_nullifies_fine_meals()
        {
            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(Trait("Ascetic")));
            Assert.Null(p.needs.mood!.thoughts.memories.TryGainMemory(Thought("AteFineMeal")));
            Assert.NotNull(p.needs.mood.thoughts.memories.TryGainMemory(Thought("AteWithoutTable")));
        }
    }
}
