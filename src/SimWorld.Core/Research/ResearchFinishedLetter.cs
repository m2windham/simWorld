using SimWorld.Defs;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// Tells the player a research project is done (RimWorld: <c>ResearchManager.FinishProject</c>'s
    /// "ResearchFinished" message when research work completes a project). Only work that completes a project
    /// is announced: a scenario granting its starting technology, or a debug finish, is not news, and an
    /// Agrarian start would otherwise open on a pile of letters for projects nobody watched being learned.
    /// </summary>
    public static class ResearchFinishedLetter
    {
        /// <summary>Sends the letter for <paramref name="project"/>, finished by research work.</summary>
        public static void Send(ResearchProjectDef project, Pawn? researcher)
        {
            string name = project.label != null ? project.LabelCap : project.defName;
            string text = researcher != null
                ? $"{researcher.Label} has finished researching {name}."
                : $"Research on {name} is finished.";
            Find.LetterStack.ReceiveLetter("Research finished: " + name, text, ResearchLetterDefOf.ResearchFinished);
        }
    }

    [DefOf]
    public static class ResearchLetterDefOf
    {
        /// <summary>A research project has been completed by research work.</summary>
        public static LetterDef ResearchFinished = null!;
    }
}
