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
        /// Multiplies the cost of every project tagged with this era, on top of RimWorld's tech-level penalty
        /// (<see cref="ResearchProjectDef.CostFactor"/>), so the era's place in the arc sets its price as well as
        /// its content. It acts where RimWorld's own cost multiplier acts, dividing the points a researcher
        /// earns, so <see cref="ResearchProjectDef.baseCost"/> stays the authored relative price inside the era.
        /// <para/>
        /// <b>Not sourced from RimWorld</b>, which has no eras. Each value is derived from the era pacing
        /// translation (<c>docs/status.json</c> research, <c>docs/research/era-arc.md</c> §9) and the
        /// derivation is written beside the numbers in <c>Eras.xml</c>. <c>EraPacingTests</c> pins the
        /// nominal era length the factors produce, not the factors.
        /// </summary>
        public float researchCostFactor = 1f;

        /// <summary>
        /// Citizens per research bench the settlement wants while this is its current era
        /// (<see cref="Building.WorksInitiativeTuning.ResearchBenchesWanted(int, EraDef)"/>). Smaller later: a more advanced
        /// settlement spares a larger share of its people for study. <b>Not sourced</b>; the tests pin that the
        /// bench count never falls as the population or the era rises.
        /// </summary>
        public float citizensPerScholar = 25f;

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
            if (researchCostFactor <= 0f) yield return "researchCostFactor must be > 0.";
            if (citizensPerScholar <= 0f) yield return "citizensPerScholar must be > 0.";
        }
    }
}
