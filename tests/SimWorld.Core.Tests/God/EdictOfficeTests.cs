using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// offices.edicts: an edict naming <see cref="EdictDef.requiredOffice"/> cannot be issued while that seat
    /// is vacant everywhere, says so in the words the host shows ("Needs a seated Steward."), and an issued one
    /// is suspended while its seat stands empty and resumes when it is filled.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictOfficeTests : ContentTestBase
    {
        public EdictOfficeTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static EdictDef StewardEdict() => new EdictDef
        {
            defName = "TestStewardEdict",
            label = "test steward edict",
            requiredOffice = OfficeDefOf.Steward,
            prioritizedWork = new List<WorkTypeDef> { WorkTypeDefOf.Construction },
        };

        /// <summary>A generated world with one founded band and its seats filled.</summary>
        private static Settlement SettledWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal,
                "Test", 3, soloStart: true);
            Find.World = world;
            var taken = new HashSet<int>(world.worldObjects.Select(o => o.tile));
            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(t => !taken.Contains(t) && !world.grid.Tiles[t].WaterCovered);
            Settlement town = SettlementFounder.Found(world, tile, world.factions[0], 20, new RandomStream(7), "Seathold");
            OfficeManager.ReconcileWorld(world);
            return town;
        }

        private static Pawn Steward(Settlement town) => OfficeManager.HolderIn(OfficeDefOf.Steward, town)!;

        [Fact]
        public void The_shipped_works_mandate_needs_a_steward_and_no_edict_needs_a_seat_that_retires()
        {
            Assert.Same(OfficeDefOf.Steward, DefDatabase<EdictDef>.GetNamed("GreatWorksMandate").requiredOffice);
            Assert.DoesNotContain(DefDatabase<EdictDef>.AllDefsListForReading, e => e.requiredOffice == OfficeDefOf.Elder);
        }

        [Fact]
        public void With_no_steward_seated_it_is_refused_and_the_reason_names_the_seat()
        {
            EdictDef edict = StewardEdict();

            Assert.False(Find.God.CanActivate(edict));
            Assert.False(Find.God.Activate(edict));

            EdictOption option = EdictOption.For(edict, Find.God);
            Assert.Equal(EdictAvailability.NeedsSeatedOffice, option.Availability);
            Assert.Equal("Needs a seated Steward.", option.Reason);
        }

        [Fact]
        public void The_shipped_mandate_is_refused_through_the_command_seam_with_that_reason()
        {
            foreach (string era in new[] { "SticksAndStones", "Agrarian", "Bronze" })
            {
                foreach (ResearchProjectDef project in DefDatabase<EraDef>.GetNamed(era).Projects)
                {
                    Find.ResearchManager.FinishProject(project);
                }
            }

            GodCommandResult result = GodCommands.IssueEdict("GreatWorksMandate");
            Assert.False(result.Changed);
            Assert.Equal("Needs a seated Steward.", result.Reason);

            EdictOption option = GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == "GreatWorksMandate");
            Assert.Equal(EdictAvailability.NeedsSeatedOffice, option.Availability);
            Assert.Equal(result.Reason, option.Reason);
        }

        [Fact]
        public void Once_a_steward_is_seated_it_can_be_issued()
        {
            SettledWorld("edict-office-seated");
            EdictDef edict = StewardEdict();

            Assert.True(Find.God.CanActivate(edict));
            Assert.True(Find.God.Activate(edict));
            Assert.True(Find.God.IsActive(edict));
            Assert.Equal(EdictAvailability.Active, EdictOption.For(edict, Find.God).Availability);
        }

        [Fact]
        public void A_vacancy_suspends_it_and_a_successor_resumes_it()
        {
            Settlement town = SettledWorld("edict-office-vacancy");
            CoreWorld world = Find.World!;
            EdictDef edict = StewardEdict();
            Assert.True(Find.God.Activate(edict));

            Steward(town).health.Kill(null, null);
            OfficeManager.ReconcileWorld(world); // empties the seat; the interregnum leaves it empty
            Find.God.ReconcileSuspensions();

            Assert.True(Find.God.IsIssued(edict));
            Assert.True(Find.God.IsSuspended(edict));
            Assert.False(Find.God.IsActive(edict));
            Assert.DoesNotContain(edict, Find.God.ActiveEdicts);
            Assert.Contains(edict, Find.God.IssuedEdicts); // it keeps its slot
            EdictOption suspended = EdictOption.For(edict, Find.God);
            Assert.Equal(EdictAvailability.Suspended, suspended.Availability);
            Assert.Contains("Needs a seated Steward.", suspended.Reason);
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Edict suspended:"));

            OfficeManager.ReconcileWorld(world); // the next sweep seats a successor
            Assert.NotNull(OfficeManager.HolderIn(OfficeDefOf.Steward, town));
            Find.God.ReconcileSuspensions();

            Assert.True(Find.God.IsActive(edict));
            Assert.False(Find.God.IsSuspended(edict));
            Assert.Contains(edict, Find.God.ActiveEdicts);
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Edict resumed:"));
        }

        [Fact]
        public void A_suspended_edict_can_still_be_rescinded()
        {
            Settlement town = SettledWorld("edict-office-rescind");
            EdictDef edict = StewardEdict();
            Assert.True(Find.God.Activate(edict));
            Steward(town).health.Kill(null, null);
            OfficeManager.ReconcileWorld(Find.World!);
            Find.God.ReconcileSuspensions();
            Assert.True(Find.God.IsSuspended(edict));

            Assert.True(Find.God.Deactivate(edict));
            Assert.False(Find.God.IsIssued(edict));
            Assert.Empty(Find.God.SuspendedEdicts);
        }

        [Fact]
        public void Suspension_round_trips_through_Scribe()
        {
            Settlement town = SettledWorld("edict-office-scribe");
            EdictDef works = DefDatabase<EdictDef>.GetNamed("GreatWorksMandate");

            // Issued while seated, then suspended by the vacancy.
            foreach (string era in new[] { "SticksAndStones", "Agrarian", "Bronze" })
            {
                foreach (ResearchProjectDef project in DefDatabase<EraDef>.GetNamed(era).Projects)
                {
                    Find.ResearchManager.FinishProject(project);
                }
            }
            Assert.True(Find.God.Activate(works));
            Steward(town).health.Kill(null, null);
            OfficeManager.ReconcileWorld(Find.World!);
            Find.God.ReconcileSuspensions();
            Assert.True(Find.God.IsSuspended(works));

            string xml = Scribe.SaveToString(Find.God, "god");
            GodManager loaded = Scribe.Load<GodManager>(xml, "god", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.True(loaded.IsIssued(works));
            Assert.True(loaded.IsSuspended(works));
            Assert.False(loaded.IsActive(works));
            Assert.DoesNotContain(works, loaded.ActiveEdicts);
        }
    }
}
