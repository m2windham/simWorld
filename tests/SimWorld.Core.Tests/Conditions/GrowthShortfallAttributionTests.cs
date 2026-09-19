using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Conditions
{
    /// <summary>
    /// A shortfall is split between the conditions that caused it, and the parts sum to the whole.
    ///
    /// <para/><b>The bug these pin, and why the drought suite beside them could not see it.</b> The first
    /// version of the nutrition-denied instrument looped over every growth-suppressing condition and credited
    /// each the <i>full</i> shortfall. With one drought def in content — all today's content can produce —
    /// that is indistinguishable from correct. With two conditions active the ledger reports twice the
    /// nutrition the settlement ever failed to grow, and the error grows with the number of sources.
    ///
    /// <para/>This is the same defect the death ledger hit one area earlier, in a different system and by a
    /// different hand: a total computed carefully, then handed out without checking that the parts sum to the
    /// whole. The invariant is worth stating once and testing everywhere it applies — <b>an instrument may
    /// under-claim, never over-claim</b> — because both times the over-claim was invisible in every scenario
    /// the code shipped with, and would only have become wrong on the day somebody added a second source.
    ///
    /// <para/>Verified the way a regression test earns its keep: these were run against the looping version
    /// first and three of the six failed, the headline one reporting 200 where 100 was owed.
    /// </summary>
    public class GrowthShortfallAttributionTests : ContentTestBase
    {
        public GrowthShortfallAttributionTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
        }

        /// <summary>Condition defs built here rather than taken from content: the shipped pack has exactly one
        /// growth-suppressing def, which is precisely the situation that hides the bug.</summary>
        private static GameConditionDef DefNamed(string defName) => new GameConditionDef
        {
            defName = defName,
            conditionClass = typeof(GameCondition_Drought),
        };

        private static GameConditionManager ManagerWith(params (string Name, float Severity)[] conditions)
        {
            var manager = new GameConditionManager();
            foreach ((string name, float severity) in conditions)
            {
                manager.RegisterCondition(new GameCondition_Drought
                {
                    def = DefNamed(name),
                    Severity = severity,
                });
            }

            return manager;
        }

        [Fact]
        public void One_source_is_credited_with_the_whole_shortfall()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith(("DroughtA", 0.5f)).AttributeGrowthShortfall(ledger, 100f);

            Assert.Equal(100f, ledger.NutritionDeniedBy("DroughtA"), 3);
            Assert.Equal(100f, ledger.TotalNutritionDenied, 3);
        }

        /// <summary>The regression. Two conditions must share one shortfall, not each receive it.</summary>
        [Fact]
        public void Two_sources_share_one_shortfall_rather_than_each_taking_all_of_it()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith(("DroughtA", 0.5f), ("DroughtB", 0.5f)).AttributeGrowthShortfall(ledger, 100f);

            Assert.Equal(100f, ledger.TotalNutritionDenied, 3);
            Assert.True(
                ledger.NutritionDeniedBy("DroughtA") < 100f,
                "a single source took the whole shortfall while another was also suppressing growth");
        }

        /// <summary>
        /// Equal factors are the one case an equal split would also get right, so the shares are checked where
        /// the two answers differ. Asserted as an ordering and a sum rather than against the log-share
        /// arithmetic itself, which is the implementation rather than the claim.
        /// </summary>
        [Fact]
        public void A_harsher_condition_takes_the_larger_share()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith(("Mild", 0.9f), ("Harsh", 0.1f)).AttributeGrowthShortfall(ledger, 100f);

            Assert.Equal(100f, ledger.TotalNutritionDenied, 3);
            Assert.True(
                ledger.NutritionDeniedBy("Harsh") > ledger.NutritionDeniedBy("Mild"),
                $"harsh {ledger.NutritionDeniedBy("Harsh"):F2} vs mild {ledger.NutritionDeniedBy("Mild"):F2}");
        }

        /// <summary>A condition that stops growth outright has no logarithm; it must still be charged, and the
        /// total must still balance.</summary>
        [Fact]
        public void A_condition_that_stops_growth_completely_does_not_break_the_split()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith(("Total", 0f), ("Mild", 0.9f)).AttributeGrowthShortfall(ledger, 100f);

            Assert.Equal(100f, ledger.TotalNutritionDenied, 3);
            Assert.True(
                ledger.NutritionDeniedBy("Total") > ledger.NutritionDeniedBy("Mild"),
                "a condition that stopped growth outright should carry most of the blame");
        }

        [Fact]
        public void Nothing_is_recorded_when_no_condition_is_suppressing_growth()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith().AttributeGrowthShortfall(ledger, 100f);

            Assert.Equal(0f, ledger.TotalNutritionDenied, 3);
        }

        [Fact]
        public void A_shortfall_of_nothing_records_nothing()
        {
            var ledger = new ResourceImpactLedger();
            ManagerWith(("DroughtA", 0.5f)).AttributeGrowthShortfall(ledger, 0f);

            Assert.Equal(0f, ledger.TotalNutritionDenied, 3);
        }
    }
}
