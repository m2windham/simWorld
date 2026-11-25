using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

namespace SimWorld.Tests.Thoughts
{
    /// <summary>
    /// What a citizen expects of life, on real generated starts. The third playtest's town sat at 40-55% mood for
    /// three years, a few points above the minor break line, because nothing stood where RimWorld's expectations
    /// thought stands: the largest single mood bonus a young colony has. Here the era is the standard of living.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ExpectationsMapTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public ExpectationsMapTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static Settlement Opened(string scenario, string seed)
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed(scenario).scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        /// <summary>The expectations mood a pawn carries now, as its situational thoughts will next compute it; NaN
        /// when it has none.</summary>
        private static float ExpectationsOf(Pawn pawn)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamed("Expectations");
            ThoughtState state = def.Worker!.CurrentState(pawn);
            return state.Active ? def.stages[state.StageIndex].baseMoodEffect : float.NaN;
        }

        private static bool CarriesExpectations(Pawn pawn) =>
            pawn.needs.mood!.thoughts.MoodThoughtGroupsAsComputed().Any(g => g[0].def.defName == "Expectations");

        [Theory]
        [InlineData("TribalStart", "SticksAndStones", 30f)]
        [InlineData("AgrarianVillage", "Agrarian", 24f)]
        public void A_citizen_expects_what_the_age_gives(string scenario, string era, float mood)
        {
            Settlement settlement = Opened(scenario, "expect-" + era);
            Assert.Equal(era, Find.ResearchManager.CurrentEra!.defName);
            for (int i = 0; i < GenDate.TicksPerHour; i++) Find.TickManager.DoSingleTick();
            Assert.All(settlement.Citizens.Where(p => p.Spawned), p =>
            {
                Assert.Equal(mood, ExpectationsOf(p));
                Assert.True(CarriesExpectations(p), p + " does not carry the thought in its mood");
            });
        }

        [Fact]
        public void Someone_outside_the_civilization_expects_nothing_of_it()
        {
            Settlement settlement = Opened("TribalStart", "expect-outsider");
            Pawn citizen = settlement.Citizens.First(p => p.Spawned);
            Assert.False(float.IsNaN(ExpectationsOf(citizen)));
            citizen.faction = null;
            Assert.True(float.IsNaN(ExpectationsOf(citizen)), "a pawn of no faction has no stake in the town's age");
        }

        [Fact]
        public void A_young_band_lives_above_the_break_line_rather_than_on_it()
        {
            // Five days of a real tribal start. Before expectations the band averaged 0.53-0.56 with the minor
            // break line at 0.35, and anybody with a bad night or an insult crossed it.
            Settlement settlement = Opened("TribalStart", "expect-mood");
            double sum = 0;
            int n = 0, belowMinor = 0;
            for (int day = 0; day < 5; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) Find.TickManager.DoSingleTick();
                foreach (Pawn p in settlement.Citizens.Where(p => !p.Dead && p.needs?.mood != null))
                {
                    sum += p.needs.mood!.CurLevel;
                    n++;
                    if (p.mindState.mentalBreaker.CurMoodBreakIntensity != MentalBreakIntensity.None) belowMinor++;
                }
            }
            double mean = sum / n;
            output.WriteLine($"mean mood {mean:0.00} over {n} citizen-days, {belowMinor} at break risk");
            Assert.True(mean > 0.65, $"mean mood {mean:0.00}");
            Assert.True(belowMinor * 10 < n, $"{belowMinor} of {n} citizen-days at break risk");
        }
    }
}
