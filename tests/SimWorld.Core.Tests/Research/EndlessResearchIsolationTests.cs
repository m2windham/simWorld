using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// The fix for tracker #88: endless-research projects used to be minted straight into
    /// <see cref="DefDatabase.Global"/> (<see cref="EndlessResearch.MintAge"/> et al., called from
    /// <see cref="ResearchManager"/>), so a second game in the same process — a second save loaded, a bench
    /// run, the test suite itself — could see the first one's generated tail through any reader that asked
    /// <see cref="DefDatabase{ResearchProjectDef}"/> for "every project". <see cref="ResearchManager"/> now
    /// mints into a private, per-game database instead and <see cref="ResearchManager.AllProjects"/> is the
    /// one place a reader asks for "every project this game can see"; these tests are the regression guard for
    /// that specifically, alongside the shape tests in <see cref="EndlessResearchTests"/> and
    /// <see cref="EndlessDepthTests"/>.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EndlessResearchIsolationTests : ContentTestBase
    {
        public EndlessResearchIsolationTests(CoreContentFixture content) : base(content)
        {
        }

        [Fact]
        public void A_second_games_readers_never_see_the_first_games_endless_tail()
        {
            int authoredCountBefore = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count;

            // Game A: opens several ages and never finishes anything it minted, so every one of its
            // foundation projects (no prerequisites) sits there unfinished and, if it leaked, would look
            // trivially startable to anyone reading Find.ResearchManager.IsFinished ambiently.
            var gameA = new ResearchManager { EndlessSeed = 881_101 };
            Find.ResearchManager = gameA;
            gameA.DebugSetAllProjectsFinished();
            for (int i = 0; i < 5; i++) gameA.ExtendEndlessTree();
            Assert.Equal(5, gameA.EndlessAges);

            var gameAsProjects = new HashSet<ResearchProjectDef>(
                gameA.AllProjects.Where(p => EndlessResearch.BelongsTo(p, gameA.EndlessSeed)));
            Assert.NotEmpty(gameAsProjects);

            // Playing game A never touched the shared, process-wide database.
            Assert.Equal(authoredCountBefore, DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count);

            // Game B: a fresh civilization in the same process.
            var gameB = new ResearchManager { EndlessSeed = 881_102 };
            Find.ResearchManager = gameB;

            // The accessor.
            Assert.DoesNotContain(gameB.AllProjects, p => gameAsProjects.Contains(p));
            Assert.Null(gameB.GetProject(gameAsProjects.First().defName));

            // EraDef: immune by construction (a generated project never carries an era — see
            // EndlessResearch's own doc), checked directly rather than only argued.
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading)
            {
                Assert.DoesNotContain(era.Projects, EndlessResearch.IsGenerated);
                Assert.DoesNotContain(era.SpineProjects, EndlessResearch.IsGenerated);
            }

            // NothingLeftToResearch: game B's own authored tree is untouched, so there is plenty left.
            Assert.False(gameB.NothingLeftToResearch);

            // ResearchAgenda: once B's own authored tree is exhausted, the agenda must mint B's OWN tail
            // rather than silently picking up A's already-minted, unfinished projects. This is the sharpest
            // check — the historical bug made EnsureProject() return one of A's foundations (cheap, no
            // prerequisites, and never marked finished from B's point of view) and B never opened an age of
            // its own at all.
            gameB.DebugSetAllProjectsFinished();
            Assert.True(gameB.NothingLeftToResearch, "game B's own authored tree should be exhausted here.");

            ResearchProjectDef? chosen = ResearchAgenda.EnsureProject();

            Assert.NotNull(chosen);
            Assert.False(gameAsProjects.Contains(chosen), chosen!.defName + " is game A's project, not game B's.");
            Assert.True(EndlessResearch.BelongsTo(chosen, gameB.EndlessSeed), chosen!.defName + " is not game B's own tail.");
            Assert.True(gameB.EndlessAges >= 1, "game B never opened an age of its own — it must have borrowed game A's instead.");
            Assert.Same(chosen, gameB.CurrentProj);

            // Still true after all of the above: the shared database only ever carried authored content.
            Assert.Equal(authoredCountBefore, DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count);
        }

        [Fact]
        public void Opening_endless_ages_never_changes_the_shared_databases_research_count()
        {
            int before = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count;

            var game = new ResearchManager { EndlessSeed = 881_201 };
            Find.ResearchManager = game;
            game.DebugSetAllProjectsFinished();
            for (int i = 0; i < 10; i++) game.ExtendEndlessTree();
            Assert.Equal(10, game.EndlessAges);
            Assert.Contains(game.AllProjects, p => EndlessResearch.BelongsTo(p, game.EndlessSeed));

            Assert.Equal(before, DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count);
        }

        [Fact]
        public void Scribe_round_trip_resumes_mid_endless_age_with_the_same_project_finished_set_and_frontier()
        {
            var manager = new ResearchManager { EndlessSeed = 881_301 };
            Find.ResearchManager = manager;
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch(); // opens age 0
            manager.ExtendEndlessTree(); // age 1
            manager.ExtendEndlessTree(); // age 2
            Assert.Equal(3, manager.EndlessAges);

            EndlessResearchDef track = EndlessResearchTests.Tracks[0];
            EndlessAgeDef ages = EndlessResearchTests.Ages;

            ResearchProjectDef age0Foundation = EndlessResearchTests.Named(ages, track, 0, 0, manager.EndlessSeed);
            manager.FinishProject(age0Foundation);
            ResearchProjectDef current = EndlessResearchTests.Named(ages, track, 0, 1, manager.EndlessSeed);
            manager.CurrentProj = current;
            manager.ResearchPerformed(50f, null);

            float progressBefore = manager.GetProgress(current);
            Assert.True(progressBefore > 0f);
            bool frontierBefore = manager.EndlessFrontierReached;
            var finishedNamesBefore = new HashSet<string>(
                manager.AllProjects
                    .Where(p => EndlessResearch.BelongsTo(p, manager.EndlessSeed) && manager.IsFinished(p))
                    .Select(p => p.defName),
                StringComparer.Ordinal);
            Assert.NotEmpty(finishedNamesBefore);

            string xml = Scribe.SaveToString(manager, "research");
            // Two integers stand for the whole tail; no generated label makes it into the save.
            Assert.Contains("endlessAges", xml);
            Assert.Contains("endlessSeed", xml);
            Assert.DoesNotContain(current.label!, xml);

            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            // The frontier: same number of ages, same seed, so the same tail re-mints.
            Assert.Equal(manager.EndlessAges, loaded.EndlessAges);
            Assert.Equal(manager.EndlessSeed, loaded.EndlessSeed);
            Assert.Equal(frontierBefore, loaded.EndlessFrontierReached);

            // The same current project.
            Assert.NotNull(loaded.CurrentProj);
            Assert.Equal(current.defName, loaded.CurrentProj!.defName);
            Assert.Equal(progressBefore, loaded.GetProgress(loaded.CurrentProj!), 3);

            // The same finished set.
            var finishedNamesAfter = new HashSet<string>(
                loaded.AllProjects
                    .Where(p => EndlessResearch.BelongsTo(p, loaded.EndlessSeed) && loaded.IsFinished(p))
                    .Select(p => p.defName),
                StringComparer.Ordinal);
            Assert.Equal(finishedNamesBefore, finishedNamesAfter);

            ResearchProjectDef? reloadedFoundation = loaded.GetProject(age0Foundation.defName);
            Assert.NotNull(reloadedFoundation);
            Assert.True(loaded.IsFinished(reloadedFoundation!));
        }
    }
}
