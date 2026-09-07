using System.Collections.Generic;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.Defs
{
    /// <summary>
    /// Research &amp; Tech's slice of ThingDef: what a civilization must know before this thing exists at all.
    /// Kept in its own partial file so each module owns its own additions — see <c>Defs/ThingDef.cs</c> for the
    /// shared <c>partial</c> declaration every such file depends on.
    ///
    /// Implementing <see cref="IResearchUnlockable"/> here is what makes
    /// <see cref="ResearchProjectDef.UnlockedDefs"/> answer anything at all: research does not know about
    /// things, it scans every loaded Def for this interface. Until some Def implemented it, every project in
    /// the tree unlocked precisely nothing (<c>docs/research/tech-reachability.md</c> §5.3).
    /// </summary>
    public partial class ThingDef : IResearchUnlockable
    {
        /// <summary>Projects that must be finished before this thing can be built, crafted or spawned by the player.</summary>
        public List<ResearchProjectDef>? researchPrerequisites;

        IReadOnlyList<ResearchProjectDef>? IResearchUnlockable.ResearchPrerequisites => researchPrerequisites;

        /// <summary>
        /// True when the civilization knows everything this thing requires — trivially true for the many
        /// things that require nothing. Read this wherever the player is offered the thing (construction
        /// menus, crafting bills, trader stock); it is deliberately not consulted by spawning, so scenarios,
        /// world generation and rival factions can place things the player has not researched.
        /// </summary>
        public bool IsResearchFinished
        {
            get
            {
                if (researchPrerequisites == null) return true;
                for (int i = 0; i < researchPrerequisites.Count; i++)
                {
                    if (!Find.ResearchManager.IsFinished(researchPrerequisites[i])) return false;
                }
                return true;
            }
        }
    }
}
