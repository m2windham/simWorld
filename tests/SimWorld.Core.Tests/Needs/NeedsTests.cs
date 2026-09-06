using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Needs
{
    internal sealed class StarvationRecorder : Pawn
    {
        public readonly List<bool> Intervals = new List<bool>();

        public StarvationRecorder(ThingDef def) : base(def, "Recorder")
        {
        }

        public override void Notify_StarvationInterval(bool starving) => Intervals.Add(starving);
    }

    public class NeedsTests : ContentTestBase
    {
        public NeedsTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void Food_starts_at_80_percent_and_falls_1_6_per_day_while_fed()
        {
            Pawn p = NewHuman();
            Need_Food food = p.needs.food!;
            Assert.Equal(0.8f, food.CurLevel);
            Assert.Equal(HungerCategory.Fed, food.CurCategory);

            RunTicks(GenDate.TicksPerDay / 4, p);
            Assert.InRange(food.CurLevel, 0.39f, 0.41f);
            Assert.Equal(HungerCategory.Fed, food.CurCategory);
        }

        [Fact]
        public void Hunger_slows_as_the_stomach_empties_and_starvation_is_reported()
        {
            var p = new StarvationRecorder(Human);
            Need_Food food = p.needs.food!;
            float fedRate = food.FoodFallPerTickAssumingCategory(HungerCategory.Fed);
            Assert.Equal(fedRate * 0.5f, food.FoodFallPerTickAssumingCategory(HungerCategory.Hungry));
            Assert.Equal(fedRate * 0.25f, food.FoodFallPerTickAssumingCategory(HungerCategory.UrgentlyHungry));
            Assert.Equal(fedRate * 0.15f, food.FoodFallPerTickAssumingCategory(HungerCategory.Starving));

            // 0.8 → 0.3 takes 0.3125 day at full rate; at 0.4 day the level is ~0.23 (hungry, half rate).
            RunTicks(GenDate.TicksPerDay * 2 / 5, p);
            Assert.Equal(HungerCategory.Hungry, food.CurCategory);
            Assert.InRange(food.CurLevel, 0.22f, 0.24f);
            Assert.DoesNotContain(true, p.Intervals);

            // Empty at 0.875 day: 0.1875 day hungry (0.3→0.15) then 0.375 day urgently hungry (0.15→0).
            RunTicks(GenDate.TicksPerDay * 3 / 5, p);
            Assert.Equal(HungerCategory.Starving, food.CurCategory);
            Assert.Contains(true, p.Intervals);
            Assert.True(food.TicksStarving > 0);

            food.Eat(0.5f);
            Assert.Equal(0.5f, food.CurLevel);
            Assert.Equal(HungerCategory.Fed, food.CurCategory);
            food.Eat(5f);
            Assert.Equal(food.MaxLevel, food.CurLevel);
        }

        [Fact]
        public void Suspended_pawns_freeze_their_needs()
        {
            Pawn p = NewHuman();
            p.Suspended = true;
            float before = p.needs.food!.CurLevel;
            for (int i = 0; i < 1000; i++)
            {
                Find.TickManager.DoSingleTick();
                p.needs.NeedsTrackerTick();
            }
            Assert.Equal(before, p.needs.food.CurLevel);
            Assert.True(p.needs.food.IsFrozen);
        }

        [Fact]
        public void Rest_falls_while_awake_and_refills_while_asleep()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            Assert.InRange(rest.CurLevel, 0.9f, 1f);
            Assert.Equal(RestCategory.Rested, rest.CurCategory);

            float start = rest.CurLevel;
            RunTicks(GenDate.TicksPerDay / 2, p);
            Assert.InRange(rest.CurLevel, start - 0.49f, start - 0.46f);

            // Rested → Tired around 0.7 day; tired pawns lose rest at 0.7× so a full day ends very tired.
            RunTicks(GenDate.TicksPerDay * 3 / 10, p);
            Assert.Equal(RestCategory.Tired, rest.CurCategory);
            Assert.Equal(0.7f, rest.RestFallFactor);
            RunTicks(GenDate.TicksPerDay / 5, p);
            Assert.Equal(RestCategory.VeryTired, rest.CurCategory);
            Assert.Equal(0.3f, rest.RestFallFactor);

            // Asleep: +2.28/day at bed effectiveness 1, so half a day refills from anywhere.
            p.Asleep = true;
            RunTicks(GenDate.TicksPerDay / 6, p);
            Assert.InRange(rest.CurLevel, 0.45f, 0.55f);
            RunTicks(GenDate.TicksPerDay / 3, p);
            Assert.Equal(1f, rest.CurLevel);
            Assert.Equal(RestCategory.Rested, rest.CurCategory);
        }

        [Fact]
        public void Time_at_zero_rest_accumulates()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.CurLevel = 0f;
            Assert.Equal(RestCategory.Exhausted, rest.CurCategory);
            RunTicks(1500, p);
            Assert.Equal(1500, rest.TicksAtZero);
            p.Asleep = true;
            RunTicks(150, p);
            Assert.Equal(0, rest.TicksAtZero);
        }

        [Fact]
        public void Joy_decays_and_freezes_during_sleep()
        {
            Pawn p = NewHuman();
            Need_Joy joy = p.needs.joy!;
            Assert.Equal(0.5f, joy.CurLevel);
            Assert.Equal(JoyCategory.Satisfied, joy.CurCategory);
            RunTicks(GenDate.TicksPerDay, p);
            Assert.True(joy.CurLevel < 0.15f);

            p.Asleep = true;
            float frozen = joy.CurLevel;
            RunTicks(1500, p);
            Assert.Equal(frozen, joy.CurLevel);
        }

        [Fact]
        public void Joy_gains_build_tolerance_that_dampens_repeats_and_decays()
        {
            Pawn p = NewHuman();
            Need_Joy joy = p.needs.joy!;
            var social = DefDatabase<JoyKindDef>.GetNamed("Social");
            joy.CurLevel = 0f;

            joy.GainJoy(0.5f, social);
            Assert.Equal(0.5f, joy.CurLevel);
            Assert.Equal(0.325f, joy.tolerances[social], 3);
            Assert.False(joy.tolerances.BoredOf(social));

            joy.GainJoy(0.5f, social);
            Assert.Equal(0.8375f, joy.CurLevel, 3);
            Assert.True(joy.tolerances.BoredOf(social));

            float before = joy.tolerances[social];
            joy.tolerances.NeedInterval();
            Assert.Equal(before - JoyToleranceSet.ToleranceDecayPerInterval, joy.tolerances[social], 5);
        }

        [Fact]
        public void Environment_needs_seek_the_sampled_level_or_their_default()
        {
            Pawn p = NewHuman();
            Need beauty = p.needs.TryGetNeed(DefDatabase<NeedDef>.GetNamed("Beauty"))!;
            Assert.Equal(0.5f, beauty.CurLevel);
            p.environment = new FixedEnvironment(0.9f);
            RunTicks(GenDate.TicksPerHour * 3, p);
            Assert.Equal(0.9f, beauty.CurLevel, 3);
            p.environment = new FixedEnvironment(0.1f);
            RunTicks(GenDate.TicksPerHour * 3, p);
            Assert.Equal(0.1f, beauty.CurLevel, 3);
        }

        private sealed class FixedEnvironment : IEnvironmentSampler
        {
            private readonly float level;
            public FixedEnvironment(float level) => this.level = level;
            public float? InstantLevelFor(NeedDef need, Pawn pawn) => level;
        }
    }
}
