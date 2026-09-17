using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// The asymmetry at the heart of the catch-up path: when a citizen is brought current after a gap, age
    /// takes the whole span and needs take a bounded one.
    ///
    /// <para/>This is the precondition the starvation clock was reverted against twice — see the long record
    /// on <c>Needs.Need_Food.NeedIntervalBulk</c>. Nothing can eat inside a bulk call, so a span longer than
    /// the abstract economy's own cadence is hunger the citizen was never given a chance to answer, and
    /// charging it anyway once killed a century of them. Nobody, however, owed anybody an opportunity to grow
    /// older, so age is not bounded and must not be.
    /// </summary>
    [Collection("GlobalDefs")]
    public class CatchUpSpanTests : ContentTestBase
    {
        public CatchUpSpanTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>
        /// Asserted as a relationship rather than against a literal: a year away must cost no more nutrition
        /// than the bound's own worth, which is the same thing as saying the clamp fired.
        /// </summary>
        [Fact]
        public void A_year_away_ages_a_citizen_a_year_but_charges_only_a_bounded_span_of_hunger()
        {
            Pawn p = NewHuman();
            Need_Food food = p.needs.food!;

            p.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, p.tier.Tier);

            float ageBefore = p.ageTracker.AgeBiologicalYearsFloat;
            float foodBefore = food.CurLevel;

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerYear);
            p.tier.Notify_AttentionChanged(true);   // promotion runs the catch-up
            Assert.Equal(PawnTier.Full, p.tier.Tier);

            // Age: the whole span.
            Assert.InRange(p.ageTracker.AgeBiologicalYearsFloat - ageBefore, 0.99f, 1.01f);

            // Hunger: no more than the bound could account for. Unbounded, a year charged roughly
            // TicksPerYear / MaxNeedCatchUpTicks = 1,800 times this much and drove the level far negative.
            float charged = foodBefore - food.CurLevel;
            float mostABoundedSpanCouldCost =
                food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * TieringTuning.MaxNeedCatchUpTicks;

            Assert.True(charged > 0f, "a year away should still cost something; the citizen is not frozen");
            Assert.True(charged <= mostABoundedSpanCouldCost + 1e-4f,
                $"charged {charged:F5} nutrition, but a bounded catch-up can cost at most {mostABoundedSpanCouldCost:F5}");
            Assert.True(food.CurLevel > 0f, "a single catch-up must not empty a full stomach");
        }

        /// <summary>
        /// A gap shorter than the bound is charged in full — the clamp is a ceiling, not a flat rate. This is
        /// the case that actually happens in a running game, where a coarse-tier citizen is brought current
        /// every long tick and the bound never fires at all.
        /// </summary>
        [Fact]
        public void A_gap_shorter_than_the_bound_is_charged_in_full()
        {
            Pawn p = NewHuman();
            Need_Food food = p.needs.food!;

            p.tier.Notify_AttentionChanged(false);
            float foodBefore = food.CurLevel;

            int shortGap = TieringTuning.MaxNeedCatchUpTicks / 4;
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + shortGap);
            p.tier.Notify_AttentionChanged(true);

            float charged = foodBefore - food.CurLevel;
            float expected = food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * shortGap;

            Assert.InRange(charged, expected * 0.95f, expected * 1.05f);
        }

        /// <summary>
        /// Two catch-ups of half the bound each cost what one catch-up of the whole bound costs. Without this
        /// the bound would be a per-call allowance that a chatty caller could spend repeatedly, which is a
        /// different rule from the one the doc claims.
        /// </summary>
        [Fact]
        public void The_bound_is_a_ceiling_per_span_not_a_rate_that_two_short_hops_can_beat()
        {
            Need_Food OneRun(int hops, int gapEach)
            {
                Pawn p = NewHuman();
                for (int i = 0; i < hops; i++)
                {
                    p.tier.Notify_AttentionChanged(false);
                    Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + gapEach);
                    p.tier.Notify_AttentionChanged(true);
                }
                return p.needs.food!;
            }

            int half = TieringTuning.MaxNeedCatchUpTicks / 2;
            float twoHops = 0.8f - OneRun(2, half).CurLevel;
            float oneHop = 0.8f - OneRun(1, TieringTuning.MaxNeedCatchUpTicks).CurLevel;

            Assert.InRange(twoHops, oneHop * 0.95f, oneHop * 1.05f);
        }
    }
}
