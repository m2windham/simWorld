using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Health
{
    /// <summary>
    /// <see cref="HediffStage.mentalBreakMtbDays"/> and <see cref="HediffStage.forgetMemoryThoughtMtbDays"/> are
    /// read now (defs.wiringaudit.untouched.gaps): a stage carrying either one rolls it every
    /// <see cref="HediffStageMtbEvents.CheckIntervalTicks"/> ticks. A near-zero mean time makes the roll certain,
    /// so the tests pin that the effect happens, not its odds.
    /// </summary>
    [Collection("GlobalDefs")]
    public class HediffStageMtbTests : ContentTestBase
    {
        public HediffStageMtbTests(CoreContentFixture content) : base(content)
        {
        }

        private const float Certain = 1e-7f;

        private static HediffDef StageDef(string name, HediffStage stage) =>
            new HediffDef { defName = name, label = name, stages = new List<HediffStage> { stage } };

        private static ThoughtDef Thought(string name) => DefDatabase<ThoughtDef>.GetNamed(name);

        [Fact]
        public void A_stage_with_a_mental_break_mean_time_breaks_the_pawn()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, StageDef("TestBreaking", new HediffStage { mentalBreakMtbDays = Certain }), 1f);
            Assert.False(p.InMentalState);

            RunTicks(HediffStageMtbEvents.CheckIntervalTicks, p);

            Assert.True(p.InMentalState, "a near-certain mental break mean time never broke the pawn");
            Assert.False(p.mindState.mentalStateHandler.CurState!.causedByMood);
        }

        [Fact]
        public void A_stage_without_one_does_not()
        {
            Pawn p = NewHuman();
            HealthUtility.AdjustSeverity(p, StageDef("TestCalm", new HediffStage()), 1f);

            RunTicks(HediffStageMtbEvents.CheckIntervalTicks * 20, p);

            Assert.False(p.InMentalState);
        }

        [Fact]
        public void A_stage_with_a_forget_memory_mean_time_takes_a_memory_away()
        {
            Pawn p = NewHuman();
            MemoryThoughtHandler memories = p.needs.mood!.thoughts.memories;
            memories.TryGainMemory(Thought("AteFineMeal"));
            memories.TryGainMemory(Thought("AteWithoutTable"));
            int before = memories.Memories.Count;
            Assert.True(before > 0);

            HealthUtility.AdjustSeverity(p, StageDef("TestForgetful", new HediffStage { forgetMemoryThoughtMtbDays = Certain }), 1f);
            RunTicks(HediffStageMtbEvents.CheckIntervalTicks, p);

            Assert.True(memories.Memories.Count < before, "a near-certain forget-memory mean time forgot nothing");
        }
    }
}
