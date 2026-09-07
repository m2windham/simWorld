using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// Read-only structure derived from the shipped tech tree, plus the snapshot/restore machinery a
    /// <see cref="Modifier"/> needs to try a "what if the content looked like this" variant without ever
    /// touching the XML on disk. Everything a modifier changes (<see cref="ResearchProjectDef.baseCost"/>,
    /// <see cref="ResearchProjectDef.prerequisites"/>) is captured on construction and put back by
    /// <see cref="Restore"/>.
    /// </summary>
    public sealed class TreeModel
    {
        /// <summary>Track vocabulary from tools/content/gen_techtree.py; a project's track is whichever tag is in here.</summary>
        public static readonly string[] Tracks =
        {
            "SurvivalFood", "MaterialsCrafts", "ConstructionSettlement", "AgricultureAnimals",
            "SocietyGovernance", "KnowledgeWriting", "BeliefCulture", "Warfare", "TradeEconomy",
            "MedicineHealth", "EnergyIndustry", "TransportExploration", "InformationComputation", "FrontierExotic",
        };

        private static readonly HashSet<string> TrackSet = new(Tracks);

        private readonly Dictionary<ResearchProjectDef, float> originalCost = new();
        private readonly Dictionary<ResearchProjectDef, List<ResearchProjectDef>?> originalPrereqs = new();

        public TreeModel()
        {
            Projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.ToList();
            Eras = DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();
            foreach (ResearchProjectDef p in Projects)
            {
                originalCost[p] = p.baseCost;
                originalPrereqs[p] = p.prerequisites == null ? null : new List<ResearchProjectDef>(p.prerequisites);
            }
            Track = Projects.ToDictionary(p => p, TrackOf);
            Recompute();
        }

        public List<ResearchProjectDef> Projects { get; }

        public List<EraDef> Eras { get; }

        /// <summary>Cached track tag per project — <see cref="TrackOf"/> re-scans the tag list, which is hot in policy code.</summary>
        public Dictionary<ResearchProjectDef, string> Track { get; }

        /// <summary>Longest prerequisite chain ending at each project; 0 for a root.</summary>
        public Dictionary<ResearchProjectDef, int> Depth { get; private set; } = new();

        /// <summary>How many projects name this one as a prerequisite.</summary>
        public Dictionary<ResearchProjectDef, int> OutDegree { get; private set; } = new();

        /// <summary>Every ancestor of a project, including itself.</summary>
        public Dictionary<ResearchProjectDef, HashSet<ResearchProjectDef>> Closure { get; private set; } = new();

        /// <summary>Summed <see cref="ResearchProjectDef.baseCost"/> over <see cref="Closure"/> — the raw price of first reaching a project.</summary>
        public Dictionary<ResearchProjectDef, float> ClosureCost { get; private set; } = new();

        public static string TrackOf(ResearchProjectDef p)
        {
            if (p.tags != null)
            {
                foreach (string tag in p.tags)
                {
                    if (TrackSet.Contains(tag)) return tag;
                }
            }
            return "Untracked";
        }

        public IEnumerable<ResearchProjectDef> InTrack(string track) => Projects.Where(p => TrackOf(p) == track);

        public IEnumerable<ResearchProjectDef> InEra(EraDef era) => Projects.Where(p => p.era == era);

        /// <summary>Rebuilds every derived table. Call after a modifier edits costs or prerequisites.</summary>
        public void Recompute()
        {
            Depth = new Dictionary<ResearchProjectDef, int>();
            OutDegree = new Dictionary<ResearchProjectDef, int>();
            Closure = new Dictionary<ResearchProjectDef, HashSet<ResearchProjectDef>>();
            ClosureCost = new Dictionary<ResearchProjectDef, float>();

            foreach (ResearchProjectDef p in Projects) OutDegree[p] = 0;
            foreach (ResearchProjectDef p in Projects)
            {
                if (p.prerequisites == null) continue;
                foreach (ResearchProjectDef q in p.prerequisites) OutDegree[q] = OutDegree.TryGetValue(q, out int n) ? n + 1 : 1;
            }
            foreach (ResearchProjectDef p in Projects) ComputeDepth(p);
            foreach (ResearchProjectDef p in Projects)
            {
                HashSet<ResearchProjectDef> set = ComputeClosure(p);
                float cost = 0f;
                foreach (ResearchProjectDef q in set) cost += q.baseCost;
                ClosureCost[p] = cost;
            }
        }

        private int ComputeDepth(ResearchProjectDef p)
        {
            if (Depth.TryGetValue(p, out int cached)) return cached;
            int d = 0;
            if (p.prerequisites != null)
            {
                foreach (ResearchProjectDef q in p.prerequisites) d = Math.Max(d, ComputeDepth(q) + 1);
            }
            Depth[p] = d;
            return d;
        }

        private HashSet<ResearchProjectDef> ComputeClosure(ResearchProjectDef p)
        {
            if (Closure.TryGetValue(p, out HashSet<ResearchProjectDef>? cached)) return cached;
            var set = new HashSet<ResearchProjectDef> { p };
            Closure[p] = set; // set before recursing; the DAG is acyclic (ConfigErrors asserts it)
            if (p.prerequisites != null)
            {
                foreach (ResearchProjectDef q in p.prerequisites) set.UnionWith(ComputeClosure(q));
            }
            return set;
        }

        /// <summary>Puts every cost and prerequisite list back the way the shipped XML had it.</summary>
        public void Restore()
        {
            foreach (ResearchProjectDef p in Projects)
            {
                p.baseCost = originalCost[p];
                List<ResearchProjectDef>? original = originalPrereqs[p];
                p.prerequisites = original == null ? null : new List<ResearchProjectDef>(original);
            }
            Recompute();
        }
    }
}
