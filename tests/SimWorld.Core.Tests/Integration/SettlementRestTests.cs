using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <b>A third test that watches the game rather than a module</b>, in the shape
    /// <see cref="SettlementFoodTests"/> established and <see cref="SettlementMoraleTests"/> followed: found a
    /// settlement the ordinary way through <see cref="Game.NewGame"/>, tick more than a week of game time
    /// with nothing called by hand, and ask a question no module test can.
    ///
    /// <para/><b>The defect it was written after</b> (<c>docs/WORK-REGISTER.md</c> §10). Measured
    /// independently across three seeds on a <c>TribalStart</c> settlement of twenty-five founders nobody
    /// opened, eight in-game days: <b>mean rest 0.95 → 0.00 by day two, and it stayed at 0.00</b>, leaving
    /// <c>Tired</c> at its worst stage — −20 mood points a head — as the largest single term in an unwatched
    /// settlement's mood ledger once food and recreation had both been closed. The cause is the one the two
    /// tests above were written for, a third time over: sleep in this port is reached only through
    /// <c>AI.JobGiver_GetRest</c>, which refuses a pawn with no map, so a citizen of a settlement nobody has
    /// opened has nowhere to lie down and <see cref="Need_Rest"/> can only fall.
    /// <see cref="AbstractRest"/> closes it, as <c>Needs.AbstractRecreation</c> closed recreation and
    /// <c>Economy.SettlementLarder</c> closed food.
    ///
    /// <para/><b>What this asserts, and what it deliberately does not.</b> It asserts the one thing this lane
    /// changed and can explain when it goes red: that an unwatched settlement <i>sleeps</i> — that its rest
    /// does not sit pinned at the floor and that <c>Tired</c> is not a permanent penalty on everybody. It
    /// asserts nothing about deaths, food, recreation or anything on a map: those belong to the tests above
    /// and to systems this lane did not touch, and a test that failed for their reasons could not explain
    /// itself.
    ///
    /// <para/><b>Unwatched only, and three seeds.</b> The watched column runs the map path
    /// (<c>JobGiver_GetRest</c> → <c>JobDriver_LayDown</c>), which <c>AI/AITests.cs</c> already pins and which
    /// this lane did not change; it is also where nearly all the cost of an integration test is — a watched
    /// settlement of twenty-five is minutes of real time per in-game day. It was measured alongside the
    /// unwatched column while this lane ran and is reported in §10 rather than paid for on every CI run.
    /// Everything below is asserted as a band or a trend, never as a level.
    /// </summary>
    public class SettlementRestTests : ContentTestBase
    {
        public SettlementRestTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>Eight days: long enough to contain several whole sleep cycles (an abstract night comes
        /// round about every three-quarters of a day) and the same span §10's measurement used.</summary>
        private const int Days = 8;

        private const int BandSize = 25;

        [Theory]
        [InlineData("rest-a")]
        [InlineData("rest-b")]
        [InlineData("rest-c")]
        public void A_settlement_nobody_is_watching_sleeps(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed,
                subdivisionOverride: 3, soloStart: true, bandSize: BandSize);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();

            // Held from tick 0: Settlement.PruneDeadCitizens drops the dead off the roster, so the roster
            // alone cannot say who started.
            List<Pawn> founders = settlement.Citizens.ToList();
            Assert.NotEmpty(founders);
            Assert.Null(settlement.InteriorMap);
            Assert.All(founders, p => Assert.False(p.Spawned));

            var dailyMeanRest = new List<float>();
            for (int day = 0; day < Days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                dailyMeanRest.Add(MeanRest(founders));
            }

            List<Pawn> alive = founders.Where(p => !p.Dead).ToList();
            Assert.NotEmpty(alive);
            Assert.Null(settlement.InteriorMap);

            // (1) Rest is reachable at all. Before this lane every one of these readings was 0.00 from day
            // two onward, on every seed.
            float bestMean = dailyMeanRest.Max();
            Assert.True(bestMean > Need_Rest.ThreshTired,
                "across " + Days + " days the settlement's mean rest never once rose out of the tired band (peak "
                + bestMean.ToString("F2") + ") — nothing in this game lets a citizen with no map sleep");

            // (2) And it is not a day-one windfall being spent down: the last day is no worse than the first
            // few. A band sleeps roughly in phase — every founder starts near full and falls at the same rate
            // — so a single day's mean is a sample of that phase, which is why this is a trend and not a
            // floor.
            Assert.True(dailyMeanRest[Days - 1] > dailyMeanRest.Take(3).Average() * 0.5f,
                "rest is sliding away over the run: " + string.Join(", ", dailyMeanRest.Select(v => v.ToString("F2"))));

            // (3) Nobody is left pinned at the floor. Need_Rest.TicksAtZero is the exhaustion clock and it
            // only ever runs while a citizen is at zero rest, so a settlement where it is running at all is
            // the settlement §10 measured.
            Assert.All(alive, p => Assert.Equal(0, p.needs.rest!.TicksAtZero));
            int exhausted = alive.Count(p => p.needs.rest!.CurCategory == RestCategory.Exhausted);
            Assert.True(exhausted == 0,
                exhausted + " of " + alive.Count + " citizens ended the run exhausted");

            // (4) And the thing §10 actually measured: Tired was the largest term in the mood ledger, at the
            // full −20 a head. Asserted against the content's own mildest stage rather than a figure — "not
            // even the gentlest tired thought, held permanently on everybody" — since the stage boundaries
            // are content and the mood values are tuned.
            ThoughtDef tired = DefDatabase<ThoughtDef>.GetNamed("Tired");
            float mildestStage = tired.stages[0].baseMoodEffect;
            float meanTired = alive.Average(p => MoodFrom(p, tired));
            Assert.True(meanTired > mildestStage,
                "mean tiredness is " + meanTired.ToString("F1") + " points a head against a mildest stage of "
                + mildestStage.ToString("F1") + " and a worst of "
                + tired.stages[tired.stages.Count - 1].baseMoodEffect.ToString("F1")
                + " — Tired is still a permanent penalty on this settlement");
        }

        private static float MeanRest(IEnumerable<Pawn> founders)
        {
            List<Pawn> alive = founders.Where(p => !p.Dead).ToList();
            return alive.Count == 0 ? 0f : alive.Average(p => p.needs?.rest?.CurLevelPercentage ?? 0f);
        }

        /// <summary>This pawn's current mood contribution from one thought, however many copies of it stack.
        /// The same reading <see cref="SettlementMoraleTests"/> takes of <c>NeedJoy</c>.</summary>
        private static float MoodFrom(Pawn pawn, ThoughtDef def)
        {
            ThoughtHandler? thoughts = pawn.needs?.mood?.thoughts;
            if (thoughts == null) return 0f;
            var groups = new List<Thought>();
            thoughts.GetDistinctMoodThoughtGroups(groups);
            float total = 0f;
            foreach (Thought group in groups)
            {
                if (group.def == def) total += thoughts.MoodOffsetOfGroup(group);
            }
            return total;
        }
    }
}
