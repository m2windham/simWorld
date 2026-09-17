using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Needs
{
    internal sealed class StarvationRecorder : Pawn
    {
        public readonly List<bool> Intervals = new List<bool>();

        /// <summary>How many IntervalTicks slices each call stood for, in order.</summary>
        public readonly List<float> Slices = new List<float>();

        /// <summary>Slices counted as starving minus slices counted as fed — the signed quantity that
        /// actually drives Malnutrition, which a bare count of calls does not.</summary>
        public float NetStarvingSlices
        {
            get
            {
                float net = 0f;
                for (int i = 0; i < Intervals.Count; i++) net += Intervals[i] ? Slices[i] : -Slices[i];
                return net;
            }
        }

        public StarvationRecorder(ThingDef def) : base(def, "Recorder")
        {
        }

        public override void Notify_StarvationInterval(bool starving, float slices = 1f)
        {
            Intervals.Add(starving);
            Slices.Add(slices);
        }
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

            // Measured over a span that stays inside the Rested band: below Need_Rest.ThreshTired an off-map
            // citizen now sleeps a night of its own (Needs.AbstractRest, tested in
            // Needs/AbstractRestTests.cs) and the level stops being a pure decay curve. This test is about
            // the fall, so it keeps out of that — the old form decayed all the way down into VeryTired, which
            // was only ever reachable because nothing in this codebase could let an off-map citizen lie down.
            float start = rest.CurLevel;
            RunTicks(GenDate.TicksPerDay / 2, p);
            Assert.InRange(rest.CurLevel, start - 0.49f, start - 0.46f);
            Assert.Equal(RestCategory.Rested, rest.CurCategory);

            // The per-band fall factors, set rather than decayed into for the same reason.
            rest.CurLevel = Need_Rest.ThreshTired - 0.01f;
            Assert.Equal(RestCategory.Tired, rest.CurCategory);
            Assert.Equal(0.7f, rest.RestFallFactor);
            rest.CurLevel = Need_Rest.ThreshVeryTired - 0.01f;
            Assert.Equal(RestCategory.VeryTired, rest.CurCategory);
            Assert.Equal(0.3f, rest.RestFallFactor);

            // Asleep: +2.28/day at bed effectiveness 1, so half a day refills from anywhere. A pawn who is
            // already resting is one AbstractRest steps over (it must not pay a sleeper twice), so this is
            // the same measurement it always was.
            p.Asleep = true;
            RunTicks(GenDate.TicksPerDay / 6, p);
            Assert.InRange(rest.CurLevel, 0.45f, 0.55f);
            RunTicks(GenDate.TicksPerDay / 3, p);
            Assert.Equal(1f, rest.CurLevel);
            Assert.Equal(RestCategory.Rested, rest.CurCategory);
        }

        /// <summary>
        /// The exhaustion clock, measured on a citizen the map path owns. An off-map citizen who runs down
        /// below the rest tier's threshold now sleeps a night of its own (<c>Needs.AbstractRest</c>, asserted
        /// directly in <c>Needs/AbstractRestTests.cs</c>), so "sitting at zero rest while nothing happens" is
        /// no longer a state waiting produces <i>off</i> a map — which is the whole point of that class. On a
        /// map it still is, for a pawn who cannot reach a bed or is kept from one, and that is whose clock
        /// this is. Driven a slice at a time rather than through the tick loop so the think tree does not put
        /// the pawn to bed halfway through the measurement.
        /// </summary>
        [Fact]
        public void Time_at_zero_rest_accumulates()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.CurLevel = 0f;
            Assert.Equal(RestCategory.Exhausted, rest.CurCategory);

            var map = new CoreMap(16, 16, TerrainDefOf.Soil);
            GenSpawn.Spawn(p, new IntVec3(8, 0, 8), map);

            for (int i = 0; i < 10; i++) rest.NeedInterval();
            Assert.Equal(1500, rest.TicksAtZero);

            p.Asleep = true;
            rest.NeedInterval();
            Assert.Equal(0, rest.TicksAtZero);
        }

        [Fact]
        public void Joy_decays_and_freezes_during_sleep()
        {
            Pawn p = NewHuman();
            Need_Joy joy = p.needs.joy!;
            Assert.Equal(0.5f, joy.CurLevel);
            Assert.Equal(JoyCategory.Satisfied, joy.CurCategory);

            // Measured over a span the need stays inside one band for, and stops short of
            // Need_Joy.ThreshLow: below that line an off-map citizen now takes a break of its own
            // (Needs.AbstractRecreation, tested in AI/JoyTests.cs) and the level stops being a pure decay
            // curve. This test is about the decay, so it keeps out of that. The old form ran a whole day and
            // asserted the level ended under 0.15, which was only ever true because nothing in this codebase
            // could raise recreation at all.
            const int Intervals = 100;
            RunTicks(Need.IntervalTicks * Intervals, p);
            Assert.Equal(0.5f - Need_Joy.BaseFallPerInterval * Intervals, joy.CurLevel, 4);
            Assert.True(joy.CurLevel > Need_Joy.ThreshLow);

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
