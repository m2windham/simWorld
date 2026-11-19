using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The calendar's share of threat points is RimWorld's "starting factor" (rimworldwiki.com/wiki/Combat_power):
    /// below 1 early, 1 from day 40 on, and never more. Threat grows with wealth and head count, not with the
    /// date. The unsourced curve it replaced stood at 2.8x on day 51 of seed 12345 and sent 27 raiders at a band
    /// of 18, every one of whom went down and 16 of whom bled out.
    /// </summary>
    public class ThreatStartingFactorTests : ContentTestBase
    {
        public ThreatStartingFactorTests(CoreContentFixture content) : base(content)
        {
        }

        [Theory]
        [InlineData("Cassandra_Classic")]
        [InlineData("Phoebe_Chillax")]
        [InlineData("Randy_Random")]
        public void The_calendar_eases_early_threats_and_never_scales_points_past_one(string storyteller)
        {
            SimpleCurve curve = DefDatabase<StorytellerDef>.GetNamed(storyteller).pointsFactorFromDaysPassed;

            Assert.InRange(curve.Evaluate(0f), 0.6f, 0.8f);
            Assert.True(curve.Evaluate(5f) < 1f, "the first days are eased");
            Assert.Equal(1f, curve.Evaluate(40f), 3);
            for (int day = 0; day <= 600; day += 5)
            {
                Assert.True(curve.Evaluate(day) <= 1f, $"{storyteller} day {day}: {curve.Evaluate(day)}");
            }
        }
    }
}
