using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;
using Xunit.Abstractions;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// The whole-game round trip on a game that has actually been played: save, load, save again, and the two
    /// saves must be the same text. Anything a load drops, defers or reads into the wrong field shows up here
    /// as a line that differs, which is how the host's 61-day verify run found the causes
    /// <see cref="SaveLoadRoundTripCauseTests"/> pins one at a time.
    /// </summary>
    [Collection("GlobalDefs")]
    public class SaveLoadRoundTripTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public SaveLoadRoundTripTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
        }

        [Fact]
        public void A_played_game_saved_loaded_and_saved_again_writes_the_same_save()
        {
            // The host's default new game (LayoutSuite.HostSeed, a solo band of 20), played for three days:
            // long enough for milk and wool to fill apart, rooms to warm and haulers to be caught mid-carry.
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "simworld-host", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            game.EnterSettlement(settlement);
            for (int i = 0; i < 3 * GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();

            string first = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(first, "game", out IReadOnlyList<string> errors, Content.Database);
            foreach (string error in errors) output.WriteLine("load error: " + error);
            string second = Scribe.SaveToString(loaded, "game");

            Assert.Empty(errors);
            Assert.True(first == second, FirstDifference(first, second));
        }

        /// <summary>
        /// The first line where the two saves part, with a few lines of the first save before it for context.
        /// Only the first: once a node is missing every later line is shifted, so counting differing lines
        /// position by position overstates the damage and blames sections that are in fact identical.
        /// </summary>
        private static string FirstDifference(string a, string b)
        {
            string[] la = a.Split('\n');
            string[] lb = b.Split('\n');
            int n = Math.Min(la.Length, lb.Length);
            int i = 0;
            while (i < n && la[i] == lb[i]) i++;
            var context = new List<string>();
            for (int k = Math.Max(0, i - 4); k < i; k++) context.Add("  " + la[k].Trim());
            string was = i < la.Length ? la[i].Trim() : "<end>";
            string now = i < lb.Length ? lb[i].Trim() : "<end>";
            return "saves part at line " + (i + 1) + ":\n" + string.Join("\n", context) + "\n- " + was + "\n+ " + now;
        }
    }
}
