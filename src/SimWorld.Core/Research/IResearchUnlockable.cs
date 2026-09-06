using System.Collections.Generic;

namespace SimWorld.Research
{
    /// <summary>
    /// Implemented by any Def that a research project unlocks — a recipe, a building, a piece of gear
    /// (RimWorld: things and recipes gated by <c>researchPrerequisites</c>). Research itself does not know
    /// about those Def types; <see cref="ResearchProjectDef.UnlockedDefs"/> instead scans every loaded Def
    /// for this interface to answer "what does finishing this project unlock".
    /// </summary>
    public interface IResearchUnlockable
    {
        /// <summary>Projects that must be finished before this Def is available. Null or empty when none.</summary>
        IReadOnlyList<ResearchProjectDef>? ResearchPrerequisites { get; }
    }
}
