using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// A civilization-wide age of technological development — SimWorld's translation of RimWorld's
    /// <see cref="TechLevel"/> ladder into "civilization through the ages". Every <see cref="ResearchProjectDef"/>
    /// tags itself with the era it belongs to (<see cref="ResearchProjectDef.era"/>); an era is complete once
    /// its <see cref="SpineProjects"/> are finished. This is a fixed, authored ladder for now — see the
    /// "endless"/"translation" tracker items for the later procedural extension beyond it.
    /// </summary>
    public class EraDef : Def
    {
        /// <summary>Position on the era ladder; lower eras come first. Not necessarily contiguous.</summary>
        public int order;

        /// <summary>The <see cref="TechLevel"/> band this era occupies, for <see cref="ResearchProjectDef.CostFactor"/> and (later) faction/settlement generation.</summary>
        public TechLevel techLevel = TechLevel.Undefined;

        /// <summary>
        /// Multiplies the threat points the director spends while a civilization is in this era
        /// (<see cref="Director.StorytellerUtility.DefaultThreatPointsNow"/>) — the spec's "era completion …
        /// scales threats" (§10).
        /// <para/>
        /// <b>Not sourced from RimWorld.</b> RimWorld scales threats off colony wealth
        /// (<c>pointsPerWealthCurve</c>): what you have is what comes for it. SimWorld ports that curve but
        /// nothing yet computes real wealth — <c>IIncidentTarget.PlayerWealthForStoryteller</c> is a settable
        /// stub — so on its own the wealth term stays flat for a whole game. This factor stands in for it at
        /// civilization scale: a bronze-age rival threatens with bronze-age force. The ladder of numbers in
        /// content is SimWorld's own and could not be sourced, so the tests pin the <i>trend</i> (later eras
        /// never threaten less; reaching an era raises the points spent) rather than any literal.
        /// <para/>
        /// When real wealth accounting lands, revisit this: wealth and era both grow across a game, and
        /// multiplying by both would count the same growth twice.
        /// </summary>
        public float threatPointsFactor = 1f;

        private List<ResearchProjectDef>? projectsCache;
        private List<ResearchProjectDef>? spineCache;

        /// <summary>Every loaded project tagged with this era, in Def registration order. Cached after first use.</summary>
        public IReadOnlyList<ResearchProjectDef> Projects
        {
            get
            {
                if (projectsCache == null)
                {
                    projectsCache = new List<ResearchProjectDef>();
                    foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                    {
                        if (project.era == this)
                        {
                            projectsCache.Add(project);
                        }
                    }
                }
                return projectsCache;
            }
        }

        /// <summary>Fraction of this era's projects finished, via <see cref="Sim.Find.ResearchManager"/>. 1 when the era has no projects.</summary>
        public float Progress
        {
            get
            {
                IReadOnlyList<ResearchProjectDef> projects = Projects;
                if (projects.Count == 0) return 1f;
                int finished = 0;
                for (int i = 0; i < projects.Count; i++)
                {
                    if (projects[i].IsFinished) finished++;
                }
                return (float)finished / projects.Count;
            }
        }

        /// <summary>
        /// This era's structural spine: the projects tagged with it that some other project lists as a
        /// prerequisite. A project nothing depends on is a leaf — flavour, or a dead end — and holds nothing
        /// up behind it. Cached after first use.
        /// </summary>
        public IReadOnlyList<ResearchProjectDef> SpineProjects
        {
            get
            {
                if (spineCache == null)
                {
                    var dependedUpon = new HashSet<ResearchProjectDef>();
                    foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                    {
                        AddPrerequisites(project.prerequisites, dependedUpon);
                        AddPrerequisites(project.hiddenPrerequisites, dependedUpon);
                    }
                    spineCache = new List<ResearchProjectDef>();
                    foreach (ResearchProjectDef project in Projects)
                    {
                        if (dependedUpon.Contains(project)) spineCache.Add(project);
                    }
                }
                return spineCache;
            }
        }

        private static void AddPrerequisites(List<ResearchProjectDef>? prerequisites, HashSet<ResearchProjectDef> into)
        {
            if (prerequisites == null) return;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (prerequisites[i] != null) into.Add(prerequisites[i]);
            }
        }

        /// <summary>
        /// True once every project on this era's <see cref="SpineProjects"/> is finished — a structural rule
        /// rather than a tuned percentage. Requiring 100% of an era instead (the rule this replaces) let a
        /// single unresearched piece of dead-end flavour bar the way to the next age indefinitely; measured
        /// over a 21-archetype panel it held the mean highest era reached at 5.63 where the spine rule reaches
        /// 6.18, at no other measured cost (<c>docs/research/tech-reachability.md</c> §6, §7.2).
        ///
        /// An era whose projects are all leaves has no spine, and falls back to requiring all of them: an era
        /// of pure flavour still has to be finished to be finished.
        /// </summary>
        public bool IsComplete
        {
            get
            {
                IReadOnlyList<ResearchProjectDef> spine = SpineProjects;
                if (spine.Count == 0) return Progress >= 1f;
                for (int i = 0; i < spine.Count; i++)
                {
                    if (!spine[i].IsFinished) return false;
                }
                return true;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            projectsCache = null;
            spineCache = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (order < 0) yield return "order must be >= 0.";
            if (threatPointsFactor <= 0f) yield return "threatPointsFactor must be > 0.";
        }
    }
}
