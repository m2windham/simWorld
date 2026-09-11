using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

// The production namespace SimWorld.Research shares its leaf segment with this test namespace; alias every
// production type explicitly, the convention ResearchTests.cs and ResearchWorkTests.cs already follow
// (CLAUDE.md: "qualify with global:: when it bites").
using ResearchAgenda = global::SimWorld.Research.ResearchAgenda;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using ResearchProjectDefOf = global::SimWorld.Research.ResearchProjectDefOf;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// What a civilization studies when nobody has told it (system: research).
    ///
    /// <para/>The hole these exist to keep closed: exactly one line in all of <c>src/</c> ever assigned
    /// <c>ResearchManager.CurrentProj</c> — <c>GodManager.Activate</c>, for the five shipped edicts that name
    /// a <c>researchFocus</c> — and <c>WorkGiver_Research</c> skips outright while it is null. So a
    /// civilization whose god issued no edict researched nothing for the whole game, and the era ladder above
    /// it never moved.
    /// </summary>
    public class ResearchAgendaTests : ContentTestBase
    {
        public ResearchAgendaTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        private static IReadOnlyList<ResearchProjectDef> AllProjects =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading;

        // ---- the hole ----

        [Fact]
        public void A_civilization_nobody_is_steering_still_has_something_to_study()
        {
            Assert.Null(Find.ResearchManager.CurrentProj);

            ResearchProjectDef? chosen = ResearchAgenda.EnsureProject();

            Assert.NotNull(chosen);
            Assert.Same(chosen, Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void The_agenda_never_overrides_a_project_the_god_chose()
        {
            // What GodManager.Activate does for an edict carrying a researchFocus.
            Find.ResearchManager.CurrentProj = ResearchProjectDefOf.Agriculture;

            ResearchProjectDef? standing = ResearchAgenda.EnsureProject();

            Assert.Same(ResearchProjectDefOf.Agriculture, standing);
            Assert.Same(ResearchProjectDefOf.Agriculture, Find.ResearchManager.CurrentProj);
        }

        [Fact]
        public void It_picks_up_again_once_the_gods_project_is_finished()
        {
            Find.ResearchManager.CurrentProj = ResearchProjectDefOf.Agriculture;
            Find.ResearchManager.FinishProject(ResearchProjectDefOf.Agriculture);
            Assert.Null(Find.ResearchManager.CurrentProj);

            ResearchProjectDef? next = ResearchAgenda.EnsureProject();

            Assert.NotNull(next);
            Assert.NotSame(ResearchProjectDefOf.Agriculture, next);
        }

        // ---- the choice ----

        [Fact]
        public void The_next_project_is_one_that_can_actually_be_started()
        {
            ResearchProjectDef? next = ResearchAgenda.NextProject();

            Assert.NotNull(next);
            Assert.True(next!.CanStartNow, next.defName + " was chosen but cannot be started.");
        }

        /// <summary>The policy itself, asserted as a relation against whatever the database holds rather than
        /// against a named project: endless ages are minted into the same database while a game runs, so any
        /// test naming "the cheapest project" by defName would rot the first time one opened.</summary>
        [Fact]
        public void Nothing_startable_is_cheaper_than_what_the_agenda_picked()
        {
            ResearchProjectDef next = ResearchAgenda.NextProject()!;

            List<ResearchProjectDef> startable = AllProjects.Where(p => p.CanStartNow).ToList();
            Assert.NotEmpty(startable);
            foreach (ResearchProjectDef candidate in startable)
            {
                Assert.True(candidate.baseCost >= next.baseCost,
                    candidate.defName + " (" + candidate.baseCost + ") is startable and cheaper than the chosen "
                        + next.defName + " (" + next.baseCost + ").");
            }
        }

        [Fact]
        public void The_same_civilization_makes_the_same_choice_twice()
        {
            // Determinism is a feature: nothing here may depend on Def load order, so a tie on baseCost has to
            // be settled by something stable. Finishing a project and re-asking exercises the tie path, since
            // the shipped tree prices whole eras of projects identically.
            ResearchProjectDef first = ResearchAgenda.NextProject()!;
            ResearchProjectDef again = ResearchAgenda.NextProject()!;
            Assert.Same(first, again);

            Find.ResearchManager.FinishProject(first);
            ResearchProjectDef afterA = ResearchAgenda.NextProject()!;
            ResearchProjectDef afterB = ResearchAgenda.NextProject()!;
            Assert.Same(afterA, afterB);
            Assert.NotSame(first, afterA);
        }

        [Fact]
        public void An_agenda_run_long_enough_walks_the_ladder_upward()
        {
            // Not "it finishes the tree" — that is the reachability study's question, and it takes fifty
            // in-game years. Only that the agenda never stalls and never repeats itself: each project it
            // hands out is startable, is new, and the era the civilization is in never goes backwards.
            var seen = new HashSet<ResearchProjectDef>();
            global::SimWorld.Research.EraDef? era = Find.ResearchManager.CurrentEra;

            for (int i = 0; i < 25; i++)
            {
                ResearchProjectDef? project = ResearchAgenda.EnsureProject();
                Assert.NotNull(project);
                Assert.True(seen.Add(project!), project!.defName + " was handed out twice.");

                Find.ResearchManager.FinishProject(project);

                global::SimWorld.Research.EraDef? now = Find.ResearchManager.CurrentEra;
                if (era != null && now != null) Assert.True(now.order >= era.order, "The era ladder ran backwards.");
                if (now != null) era = now;
            }
        }

        // ---- save and load ----

        [Fact]
        public void Scribe_round_trip_keeps_the_agendas_choice_and_the_agenda_leaves_it_alone()
        {
            // The agenda holds no state of its own — it is a pure function of what ResearchManager already
            // saves — so the round trip that matters is that a loaded civilization resumes the project it was
            // on instead of the agenda re-deciding on load.
            ResearchProjectDef chosen = ResearchAgenda.EnsureProject()!;
            Find.ResearchManager.ResearchPerformed(50f, null);

            string xml = Scribe.SaveToString(Find.ResearchManager, "research");
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Same(chosen, loaded.CurrentProj);

            Find.ResearchManager = loaded;
            Assert.Same(chosen, ResearchAgenda.EnsureProject());
            Assert.True(loaded.GetProgress(chosen) > 0f, "Progress made before the save should have survived it.");
        }
    }
}
