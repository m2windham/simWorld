using System.Linq;

using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Opening a settlement through the god seam (<c>docs/spec/simworld-spec.md</c> §12a).
    ///
    /// <para/>The host is told to bind to <c>God/View</c> and never reach into <see cref="Game"/>, and this
    /// seam had no way to generate an interior map at all. So "attention and the interior are two separate
    /// decisions" — which is true of the simulation and deliberate — was not a separation a host could
    /// actually work with; it was a wall. A host could focus a town, get attention with no map, and have
    /// raids resolve mapless forever, with nothing in the seam to do about it.
    /// </summary>
    public class OpenSettlementTests : ContentTestBase
    {
        public OpenSettlementTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        [Fact]
        public void FocusingAloneLeavesTheSettlementWithNoInterior()
        {
            // The state this whole addition exists to make reachable-and-then-fixable. Not a defect: focus is
            // attention, and attention is not a map. But before OpenSettlement it was the only state a
            // seam-bound host could reach, and it is the one that makes raids resolve mapless.
            Game game = NewSoloGame("focus-only");
            Settlement settlement = PlayerSettlement(game);

            GodCommands.ClearSettlementFocus();
            GodCommandResult result = GodCommands.FocusSettlement(settlement.tile);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Null(settlement.InteriorMap);
        }

        [Fact]
        public void OpeningASettlementGivesItBothAttentionAndAnInterior()
        {
            Game game = NewSoloGame("open-both");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.ClearSettlementFocus();

            GodCommandResult result = GodCommands.OpenSettlement(settlement.tile);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.NotNull(settlement.InteriorMap);
            Assert.Equal(settlement.tile, GodViewSnapshot.Capture().FocusedSettlementTile);
        }

        [Fact]
        public void AnOpenedSettlementIsNotAnEmptyTown()
        {
            // The reason the order inside OpenSettlement is load-bearing. Only a Full-tier citizen is ever
            // placed on an interior, and for an ordinary citizen only attention holds them at Full — so a map
            // generated before the focus is a town with nobody in it. That corrects itself on the next
            // citizen-sync sweep, which is exactly why it is dangerous: it reads as a rendering glitch.
            Game game = NewSoloGame("open-populated");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.ClearSettlementFocus();

            GodCommands.OpenSettlement(settlement.tile);

            global::SimWorld.Map.Map map = settlement.InteriorMap!;
            int spawnedCitizens = settlement.Citizens.Count(p => p.Spawned && p.Map == map);
            Assert.True(spawnedCitizens > 0,
                "an opened settlement drew an empty town: " + settlement.Citizens.Count + " citizens, none on the map");
        }

        [Fact]
        public void OpeningTheSettlementThatIsAlreadyOpenChangesNothing()
        {
            Game game = NewSoloGame("open-twice");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            global::SimWorld.Map.Map first = settlement.InteriorMap!;

            GodCommandResult again = GodCommands.OpenSettlement(settlement.tile);

            Assert.Equal(GodCommandOutcome.NoChange, again.Outcome);
            Assert.Same(first, settlement.InteriorMap);
        }

        [Fact]
        public void AStaleTileIsReportedAsUnknownRatherThanRefused()
        {
            // Same treatment FocusSettlement gives it: a handle the host held across a change is not a rule
            // refusing the request, and a host needs to tell those apart.
            NewSoloGame("open-unknown");

            GodCommandResult result = GodCommands.OpenSettlement(-12345);

            Assert.Equal(GodCommandOutcome.UnknownSettlement, result.Outcome);
        }

        [Fact]
        public void GeneratingAnInteriorDoesNotMoveAttention()
        {
            // The narrow half, for a host that wants a map for a settlement nobody is watching. It draws
            // empty, and that is correct rather than broken.
            Game game = NewSoloGame("generate-only");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.ClearSettlementFocus();

            GodCommandResult result = GodCommands.GenerateSettlementInterior(settlement.tile);

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.NotNull(settlement.InteriorMap);
            Assert.Null(GodViewSnapshot.Capture().FocusedSettlementTile);
        }

        [Fact]
        public void TheSnapshotReportsWhetherASettlementHasAnInterior()
        {
            // How a host tells "no map yet" from "a map with nobody on it" without reaching into the game.
            Game game = NewSoloGame("snapshot-interior");
            Settlement settlement = PlayerSettlement(game);

            SettlementSummary before = GodViewSnapshot.Capture().Settlements.Single(s => s.Tile == settlement.tile);
            Assert.False(before.HasInteriorMap);

            GodCommands.OpenSettlement(settlement.tile);

            SettlementSummary after = GodViewSnapshot.Capture().Settlements.Single(s => s.Tile == settlement.tile);
            Assert.True(after.HasInteriorMap);
        }
    }
}
