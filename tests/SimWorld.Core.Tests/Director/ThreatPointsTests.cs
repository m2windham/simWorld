using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// What a raid's size is made of, and what it is not. The lane that wrote this found the threat curve
    /// growing with the calendar: <c>pointsFactorFromDaysPassed</c> read <c>(0,1) (20,2) (100,4)</c> and so
    /// multiplied every raid by 2.6 on day 45 whether or not the settlement had grown, while RimWorld's own
    /// term of that name is an early-game <i>discount</i> that is gone by day 40. Every assertion here is a
    /// relation between two readings of the same colony, never a literal, because the point is which way a
    /// number moves and what moves it.
    ///
    /// <para/>Sources, for the next person to wonder: <c>https://rimworldwiki.com/wiki/Raid_points</c> for the
    /// starting factor and the formula's shape.
    /// </summary>
    public class ThreatPointsTests : ContentTestBase
    {
        public ThreatPointsTests(CoreContentFixture content) : base(content)
        {
        }

        // ---- helpers ----

        private static global::SimWorld.Director.Storyteller NewStoryteller(
            string storytellerName = "Cassandra_Classic", string difficultyName = "Medium")
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed(storytellerName),
                DefDatabase<DifficultyDef>.GetNamed(difficultyName));
            Find.Storyteller = storyteller;
            return storyteller;
        }

        private static CivilizationTarget NewTarget(
            global::SimWorld.Director.Storyteller storyteller, int pawnCount, float wealth)
        {
            var target = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = wealth };
            for (int i = 0; i < pawnCount; i++) target.pawns.Add(NewHuman("Pawn" + i));
            return target;
        }

        private static float PointsOnDay(CivilizationTarget target, float day)
        {
            Find.TickManager.DebugSetTicksGame((int)(day * GenDate.TicksPerDay));
            return StorytellerUtility.DefaultThreatPointsNow(target);
        }

        public static IEnumerable<object[]> EveryStoryteller() =>
            new[] { "Cassandra_Classic", "Phoebe_Chillax", "Randy_Random" }.Select(n => new object[] { n });

        // ---- the starting factor ----

        [Theory]
        [MemberData(nameof(EveryStoryteller))]
        public void The_first_ten_days_threaten_less_than_day_forty_for_the_same_colony(string storytellerName)
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller(storytellerName);
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 25, wealth: 0f);

            float day0 = PointsOnDay(target, 0f);
            float day10 = PointsOnDay(target, 10f);
            float day40 = PointsOnDay(target, 40f);

            Assert.True(day0 < day40, $"day 0 ({day0}) should be a discount against day 40 ({day40}).");
            Assert.True(day10 < day40, $"day 10 ({day10}) should be a discount against day 40 ({day40}).");
        }

        [Theory]
        [MemberData(nameof(EveryStoryteller))]
        public void From_day_forty_the_calendar_stops_changing_the_points(string storytellerName)
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller(storytellerName);
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 25, wealth: 0f);

            float day40 = PointsOnDay(target, 40f);

            foreach (float day in new[] { 41f, 60f, 100f, 365f, 1000f })
            {
                Assert.Equal(day40, PointsOnDay(target, day));
            }
        }

        [Theory]
        [MemberData(nameof(EveryStoryteller))]
        public void The_calendar_never_multiplies_a_raid_above_what_the_settlement_earned(string storytellerName)
        {
            // "A discount, not a growth curve": at no day does the storyteller's days-passed factor exceed 1.
            // This is the assertion that would have failed on the old (0,1) (20,2) (100,4) curve.
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(storytellerName);
            for (int day = 0; day <= 2000; day += 5)
            {
                Assert.True(def.pointsFactorFromDaysPassed.Evaluate(day) <= 1f,
                    $"{storytellerName}'s days-passed factor is above 1 on day {day}.");
            }
        }

        [Theory]
        [MemberData(nameof(EveryStoryteller))]
        public void The_discount_only_ever_lifts(string storytellerName)
        {
            // Monotone: a colony a day older is never discounted more than it was the day before.
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(storytellerName);
            float previous = def.pointsFactorFromDaysPassed.Evaluate(0f);
            for (int day = 1; day <= 60; day++)
            {
                float factor = def.pointsFactorFromDaysPassed.Evaluate(day);
                Assert.True(factor >= previous, $"{storytellerName} discounts more on day {day} than on day {day - 1}.");
                previous = factor;
            }
        }

        // ---- what a raid does grow with ----

        [Fact]
        public void More_wealth_means_more_points_on_the_same_day()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget poor = NewTarget(storyteller, pawnCount: 10, wealth: 20000f);
            CivilizationTarget rich = NewTarget(storyteller, pawnCount: 10, wealth: 400000f);

            Assert.True(PointsOnDay(rich, 50f) > PointsOnDay(poor, 50f));
        }

        [Fact]
        public void More_colonists_mean_more_points_on_the_same_day()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget few = NewTarget(storyteller, pawnCount: 5, wealth: 50000f);
            CivilizationTarget many = NewTarget(storyteller, pawnCount: 15, wealth: 50000f);

            Assert.True(PointsOnDay(many, 50f) > PointsOnDay(few, 50f));
        }
    }
}
