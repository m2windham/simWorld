using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

using CoreScenario = SimWorld.Scenario.Scenario;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The read model the Unity host binds to (<c>docs/spec/simworld-spec.md</c> §10, §12): a snapshot of
    /// values plus a command surface that takes names, so the host can render and direct a civilization
    /// without holding a Def or a manager.
    /// </summary>
    [Collection("GlobalDefs")]
    public class GodViewTests : ContentTestBase
    {
        public GodViewTests(CoreContentFixture content) : base(content)
        {
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true);

        private static IReadOnlyList<EdictDef> AllEdicts => DefDatabase<EdictDef>.AllDefsListForReading;

        private static EraDef Era(string defName) => DefDatabase<EraDef>.GetNamed(defName);

        /// <summary>
        /// Walks the civilization up the era ladder through <paramref name="through"/>, the way
        /// <c>ScenPart_StartingEra</c> does — silently, via <see cref="ResearchManager.SetProjectFinishedForSetup"/>,
        /// so no era letters or chronicle lines are minted for eras nobody lived through and the history
        /// assertions below stay about what the test itself recorded.
        /// <para/>
        /// Needed because every shipped edict is gated to a different rung: at a tribal start exactly one is
        /// issuable, so the slot budget cannot be exhausted there at all. Reaching Industrial puts four within
        /// reach of three slots, which is the only state in shipped content where "no slot free" is a real
        /// answer rather than a hypothetical.
        /// </summary>
        private static void CompleteLadderThrough(EraDef through)
        {
            ResearchManager manager = Find.ResearchManager;
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order))
            {
                if (era.order > through.order) break;
                foreach (ResearchProjectDef project in era.Projects)
                {
                    if (!manager.IsFinished(project)) manager.SetProjectFinishedForSetup(project);
                }
            }
        }

        private static int FillEverySlot(GodManager god)
        {
            while (god.ActiveEdicts.Count < GodTuning.MaxActiveEdicts)
            {
                EdictDef? next = AllEdicts.FirstOrDefault(d => god.CanActivate(d));
                if (next == null) break;
                god.Activate(next);
            }
            return god.ActiveEdicts.Count;
        }

        // ---- the invariant that makes the seam trustworthy ----

        /// <summary>
        /// The one property the whole read model rests on. If these two ever disagree, the view either greys
        /// out an edict the player could have issued, or offers one the simulation then refuses — and the
        /// second is worse, because the player already believed it worked.
        /// </summary>
        [Fact]
        public void An_edict_reads_as_available_exactly_when_the_simulation_would_accept_it()
        {
            NewSoloGame("god-view-invariant");
            GodManager god = Find.God;

            // Sweep both ends of the ladder and every slot count in between. A tribal start exercises the
            // era-locked reasons; Industrial exercises the full slate, which is where the two orderings
            // (a person's, and CanActivate's) are most likely to drift apart.
            AssertAvailabilityMatchesTheSimulation(god);
            FillEverySlot(god);
            AssertAvailabilityMatchesTheSimulation(god);

            CompleteLadderThrough(Era("Industrial"));
            AssertAvailabilityMatchesTheSimulation(god);
            FillEverySlot(god);
            AssertAvailabilityMatchesTheSimulation(god);
        }

        private static void AssertAvailabilityMatchesTheSimulation(GodManager god)
        {
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            foreach (EdictOption option in snapshot.Edicts)
            {
                EdictDef def = DefDatabase<EdictDef>.GetNamed(option.DefName);
                Assert.Equal(god.CanActivate(def), option.Availability == EdictAvailability.Available);
                Assert.Equal(god.IsActive(def), option.IsActive);
                Assert.False(string.IsNullOrWhiteSpace(option.Reason));
            }
        }

        [Fact]
        public void Every_edict_in_content_appears_in_the_snapshot_issuable_or_not()
        {
            NewSoloGame("god-view-all-edicts");

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Equal(AllEdicts.Count, snapshot.Edicts.Count);
            foreach (EdictDef def in AllEdicts)
            {
                Assert.Contains(snapshot.Edicts, o => o.DefName == def.defName);
            }
        }

        [Fact]
        public void An_era_locked_edict_says_which_era_it_needs()
        {
            NewSoloGame("god-view-era-lock");

            // A tribal start is at the bottom of the ladder, so anything gated on a later era is locked now.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            var locked = snapshot.Edicts.Where(o => o.Availability == EdictAvailability.RequiresLaterEra).ToList();

            // Content may or may not gate an edict this far up the ladder; only assert the shape when it does,
            // rather than pinning this test to a particular Edicts.xml.
            foreach (EdictOption option in locked)
            {
                Assert.NotNull(option.RequiredEraDefName);
                EraDef required = DefDatabase<EraDef>.GetNamed(option.RequiredEraDefName!);
                Assert.Contains(required.LabelCap, option.Reason);
            }
        }

        [Fact]
        public void A_full_slate_reports_no_slot_free_rather_than_blaming_the_edict()
        {
            NewSoloGame("god-view-full-slate");
            GodManager god = Find.God;

            // Shipped content gates one edict per era rung, so a tribal start has exactly one issuable and
            // the budget can never bind there. Industrial is the first rung with more edicts than slots.
            CompleteLadderThrough(Era("Industrial"));
            Assert.Equal(GodTuning.MaxActiveEdicts, FillEverySlot(god));

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            var blocked = snapshot.Edicts
                .Where(o => o.Availability == EdictAvailability.NoSlotFree)
                .ToList();

            Assert.NotEmpty(blocked);
            foreach (EdictOption option in blocked)
            {
                Assert.False(option.IsActive);
                // The reason points at the slate, not at the edict — that distinction is the point of
                // reporting availability in a person's order of interest rather than CanActivate's.
                Assert.Contains("rescind", option.Reason, System.StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---- the snapshot itself ----

        [Fact]
        public void A_snapshot_of_a_running_game_reports_its_settlements_and_population()
        {
            Game game = NewSoloGame("god-view-population");
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Equal(game.TickManager.TicksGame, snapshot.TicksGame);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.DateLabel));

            Assert.NotEmpty(snapshot.Settlements);
            Assert.Equal(snapshot.Settlements.Count, snapshot.Civilization.SettlementCount);

            SettlementSummary summary = snapshot.Settlements.First(s => s.Tile == settlement.tile);
            Assert.Equal(settlement.name, summary.Name);
            Assert.Equal(settlement.TotalPopulation, summary.TotalPopulation);
            Assert.Equal(settlement.Citizens.Count, summary.CitizenCount);
            Assert.Equal(settlement.StatisticalPopulation, summary.StatisticalPopulation);

            // The civilization total is every settlement's, not just the one the player started in.
            Assert.Equal(
                snapshot.Settlements.Sum(s => s.TotalPopulation),
                snapshot.Civilization.TotalPopulation);
        }

        [Fact]
        public void A_snapshot_before_any_world_exists_reports_an_empty_civilization_rather_than_throwing()
        {
            // The main menu is a state the view has to draw. Find.God auto-creates an inert manager; there is
            // no world, so there is nothing founded and no era reached.
            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            Assert.Empty(snapshot.Settlements);
            Assert.Equal(0, snapshot.Civilization.SettlementCount);
            Assert.Equal(0, snapshot.Civilization.TotalPopulation);
            Assert.NotEmpty(snapshot.Edicts);
        }

        [Fact]
        public void A_statistical_cohort_counts_toward_the_civilization_without_being_walked()
        {
            Game game = NewSoloGame("god-view-statistical");
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();

            int before = settlement.TotalPopulation;
            settlement.AddStatisticalPeople(40_000);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            SettlementSummary summary = snapshot.Settlements.First(s => s.Tile == settlement.tile);

            Assert.Equal(before + 40_000, summary.TotalPopulation);
            Assert.True(summary.StatisticalPopulation >= 40_000);
            // Nobody was instantiated to count them — the tier's whole premise.
            Assert.Equal(settlement.Citizens.Count, summary.CitizenCount);
            Assert.True(snapshot.Civilization.StatisticalCount >= 40_000);
        }

        [Fact]
        public void Recent_history_is_oldest_first_and_capped_at_what_was_asked_for()
        {
            NewSoloGame("god-view-history");

            for (int i = 0; i < 8; i++) Find.Storyteller.RecordChronicle("Line " + i);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture(3);

            Assert.Equal(3, snapshot.RecentHistory.Count);
            Assert.Contains("Line 7", snapshot.RecentHistory[2].Text);
            Assert.Contains("Line 5", snapshot.RecentHistory[0].Text);

            // Ticks never go backwards down the list — that is what "reads as history" means.
            for (int i = 1; i < snapshot.RecentHistory.Count; i++)
            {
                Assert.True(snapshot.RecentHistory[i].Tick >= snapshot.RecentHistory[i - 1].Tick);
            }
        }

        // ---- the command surface ----

        [Fact]
        public void Issuing_an_edict_by_name_changes_the_simulation_and_shows_up_in_the_next_snapshot()
        {
            NewSoloGame("god-view-issue");
            GodManager god = Find.God;

            EdictOption target = GodViewSnapshot.Capture().Edicts
                .First(o => o.Availability == EdictAvailability.Available);

            GodCommandResult result = GodCommands.IssueEdict(target.DefName);

            Assert.True(result.Changed);
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(god.IsActive(DefDatabase<EdictDef>.GetNamed(target.DefName)));

            EdictOption after = GodViewSnapshot.Capture().Edicts.First(o => o.DefName == target.DefName);
            Assert.True(after.IsActive);
            Assert.Equal(EdictAvailability.Active, after.Availability);
        }

        [Fact]
        public void Rescinding_returns_the_civilization_to_where_it_was()
        {
            NewSoloGame("god-view-rescind");
            GodManager god = Find.God;

            EdictOption target = GodViewSnapshot.Capture().Edicts
                .First(o => o.Availability == EdictAvailability.Available);
            GodCommands.IssueEdict(target.DefName);

            GodCommandResult result = GodCommands.RescindEdict(target.DefName);

            Assert.True(result.Changed);
            Assert.False(god.IsActive(DefDatabase<EdictDef>.GetNamed(target.DefName)));
        }

        [Fact]
        public void Rescinding_an_edict_nobody_issued_is_a_no_op_not_a_failure()
        {
            NewSoloGame("god-view-rescind-noop");

            EdictOption target = GodViewSnapshot.Capture().Edicts
                .First(o => o.Availability == EdictAvailability.Available);

            GodCommandResult result = GodCommands.RescindEdict(target.DefName);

            // The world already matches what was asked for, which is not the same as a refusal.
            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
        }

        [Fact]
        public void Issuing_one_already_in_force_is_a_no_op_and_says_so()
        {
            NewSoloGame("god-view-issue-twice");

            EdictOption target = GodViewSnapshot.Capture().Edicts
                .First(o => o.Availability == EdictAvailability.Available);
            GodCommands.IssueEdict(target.DefName);

            GodCommandResult again = GodCommands.IssueEdict(target.DefName);

            Assert.Equal(GodCommandOutcome.NoChange, again.Outcome);
            Assert.Contains("already", again.Reason, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void An_unknown_name_is_reported_rather_than_silently_ignored()
        {
            NewSoloGame("god-view-unknown");

            GodCommandResult issue = GodCommands.IssueEdict("NoSuchEdictExists");
            GodCommandResult rescind = GodCommands.RescindEdict("NoSuchEdictExists");

            Assert.Equal(GodCommandOutcome.UnknownEdict, issue.Outcome);
            Assert.Equal(GodCommandOutcome.UnknownEdict, rescind.Outcome);
            Assert.Contains("NoSuchEdictExists", issue.Reason);
        }

        [Fact]
        public void A_refused_issue_explains_itself_in_the_same_words_the_view_used()
        {
            NewSoloGame("god-view-refusal-wording");

            // A tribal start already supplies blocked edicts: everything gated above SticksAndStones.
            EdictOption blocked = GodViewSnapshot.Capture().Edicts
                .First(o => o.Availability != EdictAvailability.Active && o.Availability != EdictAvailability.Available);

            GodCommandResult result = GodCommands.IssueEdict(blocked.DefName);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            // One rule set, one explanation. A second wording here would drift from the one already shown.
            Assert.Equal(blocked.Reason, result.Reason);
        }

        [Fact]
        public void The_command_surface_is_the_only_way_in()
        {
            // Not a behavioural assertion so much as a structural one, kept as a test because it is the whole
            // premise of the seam: a snapshot hands out names and values, never a Def and never a manager.
            NewSoloGame("god-view-surface");

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();

            foreach (System.Reflection.PropertyInfo property in typeof(EdictOption).GetProperties())
            {
                Assert.False(typeof(Def).IsAssignableFrom(property.PropertyType),
                    "EdictOption." + property.Name + " exposes a Def — the host could reach its worker through it");
            }
            foreach (System.Reflection.PropertyInfo property in typeof(GodViewSnapshot).GetProperties())
            {
                Assert.False(typeof(GodManager).IsAssignableFrom(property.PropertyType),
                    "GodViewSnapshot." + property.Name + " exposes the manager itself");
            }

            Assert.NotEmpty(snapshot.Edicts);
        }
    }
}
