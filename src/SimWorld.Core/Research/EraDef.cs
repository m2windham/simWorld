using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// A civilization-wide age of technological development — SimWorld's translation of RimWorld's
    /// <see cref="TechLevel"/> ladder into "civilization through the ages". Every <see cref="ResearchProjectDef"/>
    /// tags itself with the era it belongs to (<see cref="ResearchProjectDef.era"/>); an era is complete once
    /// every project tagged with it is finished. This is a fixed, authored ladder for now — see the
    /// "endless"/"translation" tracker items for the later procedural extension beyond it.
    /// </summary>
    public class EraDef : Def
    {
        /// <summary>Position on the era ladder; lower eras come first. Not necessarily contiguous.</summary>
        public int order;

        /// <summary>The <see cref="TechLevel"/> band this era occupies, for <see cref="ResearchProjectDef.CostFactor"/> and (later) faction/settlement generation.</summary>
        public TechLevel techLevel = TechLevel.Undefined;

        private List<ResearchProjectDef>? projectsCache;

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

        /// <summary>True once every project tagged with this era is finished (or it has none).</summary>
        public bool IsComplete => Progress >= 1f;

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            projectsCache = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (order < 0) yield return "order must be >= 0.";
        }
    }
}
