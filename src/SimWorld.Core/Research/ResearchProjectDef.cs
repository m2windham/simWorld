using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// One node of the tech tree (RimWorld: <c>RimWorld.ResearchProjectDef</c>). Progress and completion are
    /// tracked by <see cref="ResearchManager"/>, reached ambiently through <see cref="Find.ResearchManager"/>
    /// the way ticking reaches <see cref="Find.TickManager"/>; this Def only describes the node's shape and cost.
    /// </summary>
    public class ResearchProjectDef : Def
    {
        /// <summary>Research points needed to finish. RimWorld's range is roughly 200-6000; SimWorld's seed tree uses 100-3000.</summary>
        public float baseCost;

        public TechLevel techLevel = TechLevel.Undefined;

        /// <summary>Visible prerequisites: drawn as edges on the tree, and gate <see cref="CanStartNow"/>.</summary>
        public List<ResearchProjectDef>? prerequisites;

        /// <summary>Prerequisites that gate the project without being drawn on the tree.</summary>
        public List<ResearchProjectDef>? hiddenPrerequisites;

        public ResearchTabDef? tab;

        /// <summary>Rough grid position in the research tree UI.</summary>
        public float researchViewX;

        public float researchViewY;

        /// <summary>Free-form tags (era/theme keywords) for grouping outside the prerequisite DAG itself.</summary>
        public List<string>? tags;

        /// <summary>The civilization-wide era this project belongs to (see <see cref="EraDef.Projects"/>).</summary>
        public EraDef? era;

        /// <summary>Shown in a letter/notification when the project finishes, if set.</summary>
        public string? discoveredLetterTitle;

        public string? discoveredLetterText;

        private List<IResearchUnlockable>? unlockedDefsCache;

        /// <summary>RimWorld gates some projects behind physical techprints; SimWorld has no techprint economy yet, so this is always 0.</summary>
        public int TechprintCount => 0;

        /// <summary>The raw research-point cost, before <see cref="CostFactor"/>.</summary>
        public float Cost => baseCost;

        /// <summary>True when every entry in <see cref="prerequisites"/> is finished (vacuously true with none).</summary>
        public bool PrerequisitesCompleted
        {
            get
            {
                if (prerequisites == null) return true;
                for (int i = 0; i < prerequisites.Count; i++)
                {
                    if (!prerequisites[i].IsFinished) return false;
                }
                return true;
            }
        }

        /// <summary>True when every entry in <see cref="hiddenPrerequisites"/> is finished (vacuously true with none).</summary>
        public bool HiddenPrerequisitesCompleted
        {
            get
            {
                if (hiddenPrerequisites == null) return true;
                for (int i = 0; i < hiddenPrerequisites.Count; i++)
                {
                    if (!hiddenPrerequisites[i].IsFinished) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 1 at or below the researcher's tech level; RimWorld's formula otherwise: +0.5 per level of
        /// difference (one level above costs 1.5x, two levels above costs 2x).
        /// </summary>
        public float CostFactor(TechLevel researcherTechLevel)
        {
            if (techLevel <= researcherTechLevel) return 1f;
            return 1f + 0.5f * (techLevel - researcherTechLevel);
        }

        public bool IsFinished => Find.ResearchManager.IsFinished(this);

        public float ProgressReal => Find.ResearchManager.GetProgress(this);

        public float ProgressPercent => Find.ResearchManager.ProgressPercent(this);

        /// <summary>Not finished, and every visible and hidden prerequisite is finished.</summary>
        public bool CanStartNow => !IsFinished && PrerequisitesCompleted && HiddenPrerequisitesCompleted;

        /// <summary>Every loaded Def that names this project among its research prerequisites. Cached after first use.</summary>
        public IReadOnlyList<IResearchUnlockable> UnlockedDefs
        {
            get
            {
                if (unlockedDefsCache == null)
                {
                    unlockedDefsCache = new List<IResearchUnlockable>();
                    foreach (Def def in DefDatabase.Global.AllDefs)
                    {
                        if (def is IResearchUnlockable unlockable
                            && unlockable.ResearchPrerequisites != null
                            && unlockable.ResearchPrerequisites.Contains(this))
                        {
                            unlockedDefsCache.Add(unlockable);
                        }
                    }
                }
                return unlockedDefsCache;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            unlockedDefsCache = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (tab == null) yield return "tab is required.";
            if (baseCost <= 0f) yield return "baseCost must be > 0.";
            if (prerequisites != null && prerequisites.Contains(this)) yield return "a project cannot be its own prerequisite.";
            if (hiddenPrerequisites != null && hiddenPrerequisites.Contains(this)) yield return "a project cannot be its own hidden prerequisite.";
            if (HasPrerequisiteCycle()) yield return "prerequisites form a cycle.";
        }

        private bool HasPrerequisiteCycle()
        {
            var visiting = new HashSet<ResearchProjectDef>();
            var visited = new HashSet<ResearchProjectDef>();
            return Visit(this, visiting, visited);
        }

        /// <summary>DFS over the visible-prerequisite graph; true the moment a node reappears on the current path.</summary>
        private static bool Visit(ResearchProjectDef node, HashSet<ResearchProjectDef> visiting, HashSet<ResearchProjectDef> visited)
        {
            if (visited.Contains(node)) return false;
            if (!visiting.Add(node)) return true;
            if (node.prerequisites != null)
            {
                for (int i = 0; i < node.prerequisites.Count; i++)
                {
                    ResearchProjectDef? p = node.prerequisites[i];
                    if (p != null && Visit(p, visiting, visited)) return true;
                }
            }
            visiting.Remove(node);
            visited.Add(node);
            return false;
        }
    }
}
