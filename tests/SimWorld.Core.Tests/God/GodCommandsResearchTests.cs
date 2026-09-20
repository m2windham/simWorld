using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using ResearchAgenda = global::SimWorld.Research.ResearchAgenda;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The direct-pick half of the research lever (<see cref="GodCommands.SetResearchProject"/>) and its read
    /// side (<see cref="GodViewSnapshot.Research"/>) — <c>docs/design/player-first.md</c>'s "research focus"
    /// cell of the standing-rule/global row, wired up for the first time. See
    /// <c>GodCommands.Research.cs</c> for what this refuses and why.
    /// </summary>
    public class GodCommandsResearchTests : ContentTestBase
    {
        public GodCommandsResearchTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Researcher")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnBench(CoreMap map, IntVec3 cell)
        {
            Thing thing = ThingMaker.MakeThing(Def("ResearchBench"));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static ResearchProjectDef Project(string defName) => DefDatabase<ResearchProjectDef>.GetNamed(defName);

        // ---- refusals, one per reason ----

        [Fact]
        public void Refuses_an_empty_project_id()
        {
            GodCommandResult result = GodCommands.SetResearchProject("");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            Assert.Null(Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void Refuses_an_unknown_project_defName()
        {
            GodCommandResult result = GodCommands.SetResearchProject("NoSuchProjectAtAll");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("NoSuchProjectAtAll", result.Reason);
            Assert.Null(Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void Refuses_a_project_already_researched()
        {
            ResearchProjectDef fire = Project("Fire");
            Find.ResearchManager.FinishProject(fire);

            GodCommandResult result = GodCommands.SetResearchProject("Fire");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("already been researched", result.Reason);
        }

        [Fact]
        public void Refuses_a_project_whose_prerequisites_are_genuinely_unmet()
        {
            // Agriculture (Agrarian) requires StoneTools (SticksAndStones), unfinished on a fresh civilization
            // — CanStartNow decides this, not GodCommands re-deriving prerequisite logic of its own.
            ResearchProjectDef agriculture = Project("Agriculture");
            Assert.False(agriculture.CanStartNow);

            GodCommandResult result = GodCommands.SetResearchProject("Agriculture");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains("locked behind", result.Reason);
            Assert.Contains("Stone tools", result.Reason);
            Assert.Null(Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void Setting_the_project_already_current_is_a_no_op_not_a_failure()
        {
            GodCommands.SetResearchProject("Fire");

            GodCommandResult result = GodCommands.SetResearchProject("Fire");

            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
            Assert.Equal(Project("Fire"), Find.ResearchManager.CurrentProj);
        }

        // ---- the happy path ----

        [Fact]
        public void A_startable_project_becomes_the_current_research_focus()
        {
            Assert.Null(Find.ResearchManager.CurrentProj);

            GodCommandResult result = GodCommands.SetResearchProject("Fire");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
            Assert.Equal(Project("Fire"), Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void An_explicit_pick_replaces_whatever_was_current_before_it()
        {
            Find.ResearchManager.CurrentProj = Project("Foraging"); // e.g. an edict's own researchFocus

            GodCommandResult result = GodCommands.SetResearchProject("Fire");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(Project("Fire"), Find.ResearchManager.CurrentProj);
        }

        // ---- wasteful, but allowed (docs/design/player-first.md §5) ----

        [Fact]
        public void Picking_a_project_other_than_the_agendas_own_cheapest_first_tie_break_is_allowed()
        {
            // Fire, Foraging, Language and StoneTools are all startable and tied at baseCost 100 on a fresh
            // civilization, so ResearchAgenda.NextProject ties on defName and picks Fire first. The player is
            // free to want StoneTools instead — no more "useful" a choice, just a different one.
            ResearchProjectDef agendaWould = ResearchAgenda.NextProject()!;
            Assert.Equal("Fire", agendaWould.defName);

            GodCommandResult result = GodCommands.SetResearchProject("StoneTools");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(Project("StoneTools"), Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void Abandoning_a_projects_progress_for_a_different_one_is_allowed_and_the_progress_is_not_erased()
        {
            ResearchProjectDef fire = Project("Fire");
            Find.ResearchManager.CurrentProj = fire;
            Find.ResearchManager.ResearchPerformed(90f, null); // most of the way to Fire's 100-point cost
            float progressBefore = Find.ResearchManager.GetProgress(fire);
            Assert.True(progressBefore > 0f);

            GodCommandResult result = GodCommands.SetResearchProject("Foraging");

            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(Project("Foraging"), Find.ResearchManager.CurrentProj);
            // Fire is no longer being worked, but its progress is exactly where it was left — the player's
            // waste is time, never the record of what was already earned.
            Assert.Equal(progressBefore, Find.ResearchManager.GetProgress(fire));
        }

        // ---- the real tick loop: a chosen project actually accumulates progress and completes ----

        [Fact]
        public void A_project_chosen_through_the_command_accumulates_progress_through_the_real_work_giver_path()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnBench(map, new IntVec3(4, 0, 0));

            GodCommandResult result = GodCommands.SetResearchProject("Fire");
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.Equal(0f, Find.ResearchManager.GetProgress(Project("Fire")));

            // Nothing here calls WorkGiver_Research or JobDriver_Research directly: an idle pawn's own think
            // tree finds the bench and starts the job, exactly the path ResearchWorkTests exercises.
            RunTicks(3000, pawn);

            Assert.True(Find.ResearchManager.GetProgress(Project("Fire")) > 0f,
                "An idle pawn with a reachable bench and a player-chosen project should have advanced it by now.");
        }

        [Fact]
        public void A_project_chosen_through_the_command_can_finish_through_the_real_tick_loop()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            pawn.skills.GetSkill(SkillDefOf.Intellectual)!.levelInt = 20;
            SpawnBench(map, new IntVec3(4, 0, 0));

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetResearchProject("Fire").Outcome);

            RunTicks(60_000, pawn);

            Assert.True(Find.ResearchManager.IsFinished(Project("Fire")),
                "A 100-cost project worked by a level-20 Intellectual researcher for 60,000 ticks should finish.");
        }

        // ---- the EnsureProject claim, verified by running it rather than trusting the doc ----

        [Fact]
        public void A_players_pick_survives_while_in_progress_and_the_agenda_only_resumes_once_it_finishes()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            pawn.skills.GetSkill(SkillDefOf.Intellectual)!.levelInt = 20;
            SpawnBench(map, new IntVec3(4, 0, 0));

            ResearchProjectDef chosen = Project("Fire");
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetResearchProject("Fire").Outcome);
            Assert.Same(chosen, Find.ResearchManager.CurrentProj);

            RunTicks(2000, pawn);
            Assert.False(Find.ResearchManager.IsFinished(chosen), "test setup should not finish this soon.");

            // What the periodic driver (Building.SettlementWorksInitiative.Tick) does every gated interval:
            // asks the agenda to ensure a project. If EnsureProject ever overwrote a non-null CurrentProj this
            // is where the player's pick would evaporate.
            ResearchProjectDef? stillCurrent = ResearchAgenda.EnsureProject();
            Assert.Same(chosen, stillCurrent);
            Assert.Same(chosen, Find.ResearchManager.CurrentProj);

            RunTicks(60_000, pawn);
            Assert.True(Find.ResearchManager.IsFinished(chosen), "the chosen project should have finished by now.");
            Assert.Null(Find.ResearchManager.CurrentProj);

            // Only now, with the player's pick genuinely done, does the agenda pick up again on its own —
            // the civilization never idles, and the player was never overridden while their choice stood.
            ResearchProjectDef? next = ResearchAgenda.EnsureProject();
            Assert.NotNull(next);
            Assert.NotSame(chosen, next);
            Assert.Same(next, Find.ResearchManager.CurrentProj);
        }

        // ---- Scribe round trip: the command leaves state on ResearchManager, already Scribed there ----

        [Fact]
        public void A_project_set_through_the_command_round_trips_through_Scribe()
        {
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetResearchProject("Fire").Outcome);
            Find.ResearchManager.ResearchPerformed(37f, null);

            string xml = Scribe.SaveToString(Find.ResearchManager, "research");
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(Project("Fire"), loaded.CurrentProj);
            Assert.Equal(37f, loaded.GetProgress(Project("Fire")));
        }

        // ---- the read side: a player picks from what they can see ----

        [Fact]
        public void The_research_overview_reports_the_current_project_and_its_progress()
        {
            GodCommands.SetResearchProject("Fire");
            Find.ResearchManager.ResearchPerformed(25f, null); // 25 of Fire's 100-point cost

            ResearchOverview overview = GodViewSnapshot.Capture().Research;

            Assert.Equal("Fire", overview.CurrentProjectDefName);
            Assert.InRange(overview.CurrentProjectProgressPercent, 0.2f, 0.3f);
        }

        [Fact]
        public void The_research_overview_reports_nothing_current_when_nothing_is_current()
        {
            Assert.Null(Find.ResearchManager.CurrentProj);

            ResearchOverview overview = GodViewSnapshot.Capture().Research;

            Assert.Null(overview.CurrentProjectDefName);
            Assert.Equal(0f, overview.CurrentProjectProgressPercent);
        }

        [Fact]
        public void The_research_overview_marks_every_startable_project_available_now()
        {
            ResearchOverview overview = GodViewSnapshot.Capture().Research;

            ResearchProjectOption fire = overview.Projects.Single(p => p.DefName == "Fire");
            Assert.True(fire.CanStartNow);
            Assert.False(fire.IsFinished);
            Assert.False(fire.IsCurrent);
            Assert.Empty(fire.LockedBehind);
        }

        [Fact]
        public void The_research_overview_shows_what_a_locked_project_is_locked_behind()
        {
            ResearchOverview overview = GodViewSnapshot.Capture().Research;

            ResearchProjectOption agriculture = overview.Projects.Single(p => p.DefName == "Agriculture");
            Assert.False(agriculture.CanStartNow);
            Assert.Contains("Stone tools", agriculture.LockedBehind);
        }

        [Fact]
        public void The_research_overview_agrees_with_the_command_about_what_can_be_set()
        {
            // The view can never offer a pick the command would then refuse, the same guarantee GodViewTests
            // pins between EdictOption.Availability and GodManager.CanActivate.
            ResearchOverview overview = GodViewSnapshot.Capture().Research;

            foreach (ResearchProjectOption option in overview.Projects)
            {
                GodCommandResult result = GodCommands.SetResearchProject(option.DefName);
                if (option.CanStartNow)
                {
                    Assert.True(result.Outcome == GodCommandOutcome.Done || result.Outcome == GodCommandOutcome.NoChange);
                }
                else
                {
                    Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
                }
            }
        }
    }
}
