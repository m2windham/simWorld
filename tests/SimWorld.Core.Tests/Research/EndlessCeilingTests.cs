using System.Linq;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// research.endless.indexceiling: every generated project is a real <see cref="ResearchProjectDef"/> in a
    /// <see cref="DefDatabase"/>, whose <see cref="Def.index"/> is a <c>ushort</c>. The tail stops at
    /// <see cref="ResearchManager.MaxEndlessAges"/> whole ages instead of throwing halfway through one.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EndlessCeilingTests : ContentTestBase
    {
        public EndlessCeilingTests(CoreContentFixture content) : base(content)
        {
            manager = new QuietResearchManager();
            Find.ResearchManager = manager;
        }

        private readonly ResearchManager manager;

        /// <summary>Thousands of ages; the letter and chronicle line per age are not what is under test.</summary>
        private sealed class QuietResearchManager : ResearchManager
        {
            protected override void Notify_EndlessAgeOpened(EndlessAgeDef ages, int ageIndex) { }
        }

        [Fact]
        public void The_shipped_tail_fits_whole_ages_inside_one_registry()
        {
            int perAge = EndlessResearchTests.Tracks.Count * EndlessResearchTests.Ages.projectsPerTrack;
            int max = ResearchManager.MaxEndlessAges(EndlessResearchTests.Ages, EndlessResearchTests.Tracks);

            Assert.True(max * perAge <= DefDatabase.MaxDefsPerType);
            Assert.True((max + 1) * perAge > DefDatabase.MaxDefsPerType);
            Assert.True(max > 1000, "the ceiling is meant to be far past any game, not a real limit on play");
        }

        [Fact]
        public void Opening_ages_stops_at_the_ceiling_instead_of_overflowing_the_index()
        {
            manager.EndlessSeed = 910_777;
            int max = ResearchManager.MaxEndlessAges(EndlessResearchTests.Ages, EndlessResearchTests.Tracks);

            for (int i = 0; i < max + 3; i++) manager.ExtendEndlessTree();

            Assert.Equal(max, manager.EndlessAges);
            int generated = manager.AllProjects.Count(EndlessResearch.IsGenerated);
            Assert.Equal(max * EndlessResearchTests.Tracks.Count * EndlessResearchTests.Ages.projectsPerTrack, generated);

            // The catch-up loop sees there is nothing left past the frontier and stops rather than spinning.
            manager.DebugSetAllProjectsFinished();
            manager.EnsureSomethingToResearch();
            Assert.Equal(max, manager.EndlessAges);
            Assert.True(manager.NothingLeftToResearch);
        }
    }
}
