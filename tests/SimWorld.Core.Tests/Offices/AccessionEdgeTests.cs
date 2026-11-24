using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

namespace SimWorld.Tests.Offices
{
    /// <summary>
    /// An accession is recorded when the holder changes, and only then. The playtest's chronicle had
    /// "Accession: Fable takes the stewardship…" eight times by day 4 with no change of steward.
    /// </summary>
    [Collection("GlobalDefs")]
    public class AccessionEdgeTests : ContentTestBase
    {
        public AccessionEdgeTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static List<string> Accessions() =>
            Find.Storyteller.Chronicle.Select(e => e.incidentDefName).Where(l => l.StartsWith("Accession:")).ToList();

        /// <summary>The playtest's lever: the roles panel's "Here" / "Everywhere" sweep a role over every
        /// citizen. It used to overwrite the steward's role, the next office sweep found the seat empty and
        /// seated the same citizen again, writing a fresh accession each time. Without that lever, four
        /// game days of this seed record one accession per seat (measured while finding the cause).</summary>
        [Theory]
        [InlineData("Farmer")]
        [InlineData(null)]
        public void A_role_policy_leaves_the_seat_and_the_chronicle_alone(string? role)
        {
            Settlement settlement = OpenedSettlement("accession-policy");
            OfficeManager.Reconcile(settlement);
            OfficeDef steward = DefDatabase<OfficeDef>.GetNamed("Steward");
            Pawn holder = OfficeManager.HolderIn(steward, settlement)!;
            Assert.NotNull(holder);
            List<string> before = Accessions();

            for (int click = 0; click < 3; click++)
            {
                Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToSettlement(settlement.tile, role).Outcome);
                Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToCivilization(role).Outcome);
                OfficeManager.Reconcile(settlement);
            }

            Assert.Same(holder, OfficeManager.HolderIn(steward, settlement));
            Assert.Equal(before, Accessions());
        }

        [Fact]
        public void A_real_change_of_steward_is_still_an_accession()
        {
            Settlement settlement = OpenedSettlement("accession-succession");
            OfficeManager.Reconcile(settlement);
            OfficeDef steward = DefDatabase<OfficeDef>.GetNamed("Steward");
            Pawn holder = OfficeManager.HolderIn(steward, settlement)!;
            int before = Accessions().Count;

            holder.health.Kill(null, null);
            OfficeManager.Reconcile(settlement); // vacates: the interregnum
            OfficeManager.Reconcile(settlement); // seats a successor

            Pawn? successor = OfficeManager.HolderIn(steward, settlement);
            Assert.NotNull(successor);
            Assert.NotSame(holder, successor);
            Assert.Equal(before + 1, Accessions().Count);
            Assert.Contains(successor!.Label, Accessions().Last());
        }
    }
}
