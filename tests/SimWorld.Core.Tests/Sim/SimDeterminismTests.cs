using System.Collections.Generic;
using System.Globalization;
using System.Text;

using SimWorld.Director;
using SimWorld.God;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// A whole game, run twice from one seed, arrives at the same place.
    ///
    /// <para/><b>Why this is worth a test of its own.</b> Every measurement this project makes about outcomes
    /// is a subtraction: run the simulation, change one thing, run it again, and attribute the difference to
    /// the thing you changed. That reasoning is only valid if two runs that changed nothing would have agreed
    /// — otherwise the "effect" is just the noise floor wearing a hat. `tools/bench` <c>--suite probe</c> is
    /// built entirely on this property, and a bench suite is not run in CI, so the property is defended here
    /// instead of where it is consumed.
    ///
    /// <para/><b>Existing determinism tests pin narrower claims.</b>
    /// <c>GameTests.Same_seed_founds_the_same_size_settlement_on_the_same_tile</c> pins world generation, and
    /// <c>DemographyTests.Determinism_same_seed_produces_the_same_population_tree</c> pins a demography
    /// simulation driven directly against a bare pawn list. Neither ticks a real <see cref="Game"/> through
    /// its tick lists, which is the thing the probe actually does and therefore the thing that has to hold.
    ///
    /// <para/><b>The window is deliberately short.</b> There is no bulk-simulate path — the only way forward
    /// is one <c>DoSingleTick</c> at a time — so a long run belongs in the bench and a cheap one belongs here.
    /// Determinism does not decay with distance: a divergence has to start somewhere, and a run long enough
    /// for the tick lists, needs, jobs and the storyteller's own cadence to have turned over is long enough to
    /// catch one starting.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SimDeterminismTests : ContentTestBase
    {
        public SimDeterminismTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>Long enough that the rare (250) and long (2000) tick cadences have both come round several
        /// times, short enough to stay a test rather than an errand.</summary>
        private const int Ticks = 6000;

        private const int BandSize = 20;

        [Fact]
        public void The_same_seed_runs_the_same_game()
        {
            string a = RunAndDigest("determinism-seed");
            string b = RunAndDigest("determinism-seed");

            Assert.Equal(a, b);
        }

        /// <summary>
        /// The other half of the claim, and the half that is easy to forget: a digest that never changes would
        /// satisfy the test above perfectly while measuring nothing. If this ever fails, the first test has
        /// stopped meaning what it says.
        /// </summary>
        [Fact]
        public void A_different_seed_runs_a_different_game()
        {
            string a = RunAndDigest("determinism-seed");
            string b = RunAndDigest("a-different-world-entirely");

            Assert.NotEqual(a, b);
        }

        /// <summary>
        /// Boots a game, ticks it, and folds everything worth disagreeing about into one string. Deliberately
        /// wide: population and its tier split, the four rollup means, every death by cause, and the era. A
        /// narrow digest passes while the simulation quietly diverges beside it.
        /// </summary>
        private static string RunAndDigest(string seed)
        {
            // The same reset ContentTestBase performs, repeated between runs inside one test: two games in one
            // process must not inherit each other's services. tools/bench Bootstrap.ResetSim omitted exactly
            // this and two identically-seeded arms produced different founding bands because of it.
            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(12345);
            Pawn.ResetThingIdCounter();
            global::SimWorld.Map.Map.ResetMapIdCounter();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                seed,
                subdivisionOverride: 3,
                soloStart: true,
                bandSize: BandSize);

            for (int i = 0; i < Ticks; i++) game.TickManager.DoSingleTick();

            var rollup = new GodRollup();
            global::SimWorld.World.Settlement? home = null;
            if (game.World != null)
            {
                foreach (global::SimWorld.World.Settlement s in game.World.Settlements)
                {
                    home = s;
                    break;
                }
            }

            Assert.NotNull(home);
            rollup.Recompute(home!);

            DeathLedger deaths = Find.Storyteller.deaths;
            var sb = new StringBuilder();
            sb.Append(rollup.TotalPopulation).Append('|')
              .Append(rollup.FullCount).Append('|')
              .Append(rollup.IntervalCount).Append('|')
              .Append(rollup.StatisticalCount).Append('|')
              .Append(rollup.MeanMood.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
              .Append(rollup.MeanHealth.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
              .Append(rollup.MeanFoodNeed.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
              .Append(rollup.MeanIndustrySkill.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
              .Append(rollup.CurrentEra?.defName ?? "-");

            foreach (DeathCause cause in new[]
                     {
                         DeathCause.Age, DeathCause.Starvation, DeathCause.Disease,
                         DeathCause.Injury, DeathCause.Unknown,
                     })
            {
                sb.Append('|').Append(deaths[cause]);
            }

            return sb.ToString();
        }
    }
}
