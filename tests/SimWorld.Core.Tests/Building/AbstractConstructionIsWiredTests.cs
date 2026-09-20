using System.Linq;

using SimWorld.Building;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// That abstract construction runs in a <i>game</i>, not merely when a test calls it.
    ///
    /// <para/><b>Why this is a separate file from the tests of the mechanism.</b> The mechanism arrived
    /// complete, unit-tested and unreachable: <c>AbstractSettlementConstruction.Tick</c> had no caller
    /// anywhere in <c>src/</c>, so a settlement nobody had opened would have built nothing for an entire run
    /// while its food and its medicine carried on working. Every test of it drove the class directly, so
    /// every test passed. One line in <c>Sim/Game.cs</c>'s <c>WireTickHooks</c> was missing, beside the line
    /// that wires the map-path sibling.
    ///
    /// <para/>This is the exact failure this project keeps finding — something that looks like it works
    /// because only a test asks it to — and the one that the shipped <c>IncidentWorker_ThreatEvent</c> stub
    /// managed to sustain for the project's whole life. The repository's own wiring audit did not catch it
    /// either: it passes, all twenty-two of it, against the unwired tree.
    ///
    /// <para/>So the guard is the one <c>OfficeTests</c> already established for the same hazard — run the
    /// real loop and assert the outcome, which is "the only thing that proves the one line in
    /// <c>Sim/Game.cs</c>'s <c>WireTickHooks</c> is actually there and firing". Delete that line and this
    /// test fails; no other test in the suite does.
    /// </summary>
    public class AbstractConstructionIsWiredTests : ContentTestBase
    {
        public AbstractConstructionIsWiredTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_real_game_builds_for_a_settlement_nobody_has_opened()
        {
            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, "abstract-construction-wired",
                subdivisionOverride: 3, soloStart: true, bandSize: 20);

            Settlement town = game.World!.worldObjects.OfType<Settlement>().First(s => s.Citizens.Count > 0);

            // The whole point: no interior map. If the scenario ever starts one open, this test is measuring
            // the map path instead and its claim is void — so it asserts the precondition rather than
            // assuming it.
            Assert.Null(town.InteriorMap);

            // Every structure the producer knows how to make, not walls alone: needs are satisfied in order
            // (beds, then walls, then storage) and a twenty-strong band spends several intervals on beds
            // before it lays a single wall. Asserting on walls measured the wrong building and failed against
            // a working producer — the claim here is "it builds", not "it builds walls first".
            int before = BuildingsOf(town);

            // Several gated passes of the real loop, in Game's own tick order — never a direct call to the
            // producer, which is what the mechanism's own tests already do and what hid this defect.
            for (int i = 0; i < AbstractConstructionTuning.IntervalTicks * 4; i++) game.TickManager.DoSingleTick();

            int after = BuildingsOf(town);

            Assert.True(
                after > before,
                $"a settlement nobody opened built nothing over four intervals of a real game: {before} -> {after}. "
                + "The producer is almost certainly not wired into Sim/Game.cs's WireTickHooks.");
            Assert.Null(town.InteriorMap);   // and it still has no map: this was the abstract path throughout
        }

        /// <summary>Everything <c>AbstractSettlementConstruction</c> can credit, read through the one accessor
        /// every consumer is meant to use rather than off the ledger directly.</summary>
        private static int BuildingsOf(Settlement settlement) =>
            settlement.StructureCount(ConstructionThingDefOf.Bed)
            + settlement.StructureCount(ConstructionThingDefOf.Wall)
            + settlement.StructureCount(ConstructionThingDefOf.StorageHut);
    }
}
