using System.Linq;
using SimWorld.Defs;
using SimWorld.Letters;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// A project finished by research work is announced with a letter the host can show (RimWorld's
    /// "ResearchFinished"); a project granted outright — a scenario's starting technology — is not news.
    /// </summary>
    public class ResearchFinishedLetterTests : ContentTestBase
    {
        private readonly ResearchManager manager;

        public ResearchFinishedLetterTests(CoreContentFixture content) : base(content)
        {
            manager = new ResearchManager();
            Find.ResearchManager = manager;
            Find.LetterStack = new LetterStack();
        }

        private static Letter[] ResearchLetters() =>
            Find.LetterStack.LettersListForReading.Where(l => l.def == ResearchLetterDefOf.ResearchFinished).ToArray();

        [Fact]
        public void Research_work_that_finishes_a_project_sends_one_letter_naming_it()
        {
            ResearchProjectDef project = manager.CurrentProj
                ?? DefDatabase<ResearchProjectDef>.AllDefsListForReading.First(p => p.CanStartNow);
            manager.CurrentProj = project;
            float factor = project.CostFactor(manager.ResearcherTechLevel);

            manager.ResearchPerformed(project.baseCost * factor * 0.5f, null);
            Assert.Empty(ResearchLetters());

            manager.ResearchPerformed(project.baseCost * factor, null);
            Assert.True(manager.IsFinished(project));
            Letter letter = Assert.Single(ResearchLetters());
            Assert.Contains(project.LabelCap, letter.label);
        }

        [Fact]
        public void A_project_granted_outright_sends_no_letter()
        {
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.AllDefsListForReading.First(p => !manager.IsFinished(p));
            manager.FinishProject(project);
            Assert.Empty(ResearchLetters());
        }
    }
}
