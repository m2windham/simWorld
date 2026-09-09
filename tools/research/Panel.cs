using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>Every run of one (config, modifier stack) cell, plus the aggregates the report reads.</summary>
    public sealed class PanelResult
    {
        public string Label = "";
        public string ModifierSpec = "none";
        public RunConfig Config = new();
        public List<RunResult> Runs = new();
        public int ActiveCount;
        public HashSet<ResearchProjectDef> Excluded = new();
        public HashSet<ResearchProjectDef> EverFinished = new();
        public List<ResearchProjectDef> NeverFinished = new();

        /// <summary>The negligent control is excluded from every "engaged" figure, per Epoch's engaged-arm framing.</summary>
        public const string ControlPolicy = "idle";

        public IEnumerable<string> Policies => Runs.Select(r => r.Policy).Distinct();

        public IEnumerable<RunResult> For(string policy) => Runs.Where(r => r.Policy == policy);

        public List<RunResult> Engaged => Runs.Where(r => r.Policy != ControlPolicy).ToList();

        /// <summary>Mean fraction of the active tree finished, over every policy and seed.</summary>
        public double MeanCompletedFraction => MeanFraction(Runs);

        /// <summary>The same, over the engaged archetypes only.</summary>
        public double EngagedCompletedFraction => MeanFraction(Engaged);

        public double EngagedMaxEraOrder
        {
            get
            {
                List<RunResult> runs = Engaged;
                return runs.Count == 0 ? 0 : runs.Average(r => (double)r.MaxEraOrder);
            }
        }

        public double EngagedDryRate
        {
            get
            {
                List<RunResult> runs = Engaged;
                return runs.Count == 0 ? 0 : (double)runs.Count(r => r.FirstDryDay >= 0) / runs.Count;
            }
        }

        /// <summary>Median day an engaged run ran out of things to research; -1 when most never did.</summary>
        public int MedianEngagedDryDay
        {
            get
            {
                List<int> days = Engaged.Where(r => r.FirstDryDay >= 0).Select(r => r.FirstDryDay).OrderBy(d => d).ToList();
                return days.Count == 0 ? -1 : days[days.Count / 2];
            }
        }

        private static double MeanFraction(IReadOnlyCollection<RunResult> runs) =>
            runs.Count == 0 ? 0 : runs.Average(r => (double)r.Finished.Count / r.ActiveCount);

        /// <summary>
        /// Mean pairwise Jaccard similarity between the finished sets of two different policies on the same
        /// seed. 1.0 means every archetype ended in exactly the same place — Epoch's "no meaningful choice"
        /// signal.
        /// </summary>
        public double PolicyOverlap => Overlap(Runs);

        /// <summary>The same over engaged archetypes only, so the control's low score cannot fake divergence.</summary>
        public double EngagedOverlap => Overlap(Engaged);

        private static double Overlap(IReadOnlyCollection<RunResult> source)
        {
            double total = 0;
            int n = 0;
            foreach (var group in source.GroupBy(r => r.Seed))
            {
                List<RunResult> runs = group.ToList();
                for (int i = 0; i < runs.Count; i++)
                {
                    for (int j = i + 1; j < runs.Count; j++)
                    {
                        total += Jaccard(runs[i].Finished, runs[j].Finished);
                        n++;
                    }
                }
            }
            return n == 0 ? 1.0 : total / n;
        }

        private static double Jaccard(HashSet<ResearchProjectDef> a, HashSet<ResearchProjectDef> b)
        {
            if (a.Count == 0 && b.Count == 0) return 1.0;
            int inter = a.Count(b.Contains);
            int union = a.Count + b.Count - inter;
            return union == 0 ? 1.0 : (double)inter / union;
        }

        /// <summary>Median day era <paramref name="order"/> completed, over runs that reached it; -1 when none did.</summary>
        public int MedianEraTurn(int order)
        {
            List<int> days = Runs.Where(r => order < r.EraTurnDay.Length && r.EraTurnDay[order] >= 0)
                                 .Select(r => r.EraTurnDay[order]).OrderBy(d => d).ToList();
            return days.Count == 0 ? -1 : days[days.Count / 2];
        }

        /// <summary>
        /// Mean spread, in days, between the earliest and latest engaged archetype turning era
        /// <paramref name="order"/> on the same seed. Epoch's divergence test: a ladder where every playstyle
        /// turns the era within days of every other is not offering a choice. -1 when fewer than two reached it.
        /// </summary>
        public int EraTurnSpread(int order)
        {
            double total = 0;
            int n = 0;
            foreach (var group in Engaged.GroupBy(r => r.Seed))
            {
                List<int> days = group.Where(r => order < r.EraTurnDay.Length && r.EraTurnDay[order] >= 0)
                                      .Select(r => r.EraTurnDay[order]).ToList();
                if (days.Count < 2) continue;
                total += days.Max() - days.Min();
                n++;
            }
            return n == 0 ? -1 : (int)Math.Round(total / n);
        }

        public double EraReachRate(int order) =>
            Runs.Count == 0 ? 0 : (double)Runs.Count(r => order < r.EraTurnDay.Length && r.EraTurnDay[order] >= 0) / Runs.Count;
    }

    /// <summary>Applies a modifier stack, runs every policy across every seed, and collects the aggregates.</summary>
    public static class Panel
    {
        public static PanelResult Run(TreeModel tree, RunConfig baseConfig, string modifierSpec, IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds, string label)
        {
            tree.Restore();
            var excluded = new HashSet<ResearchProjectDef>();
            RunConfig config = baseConfig.Clone();
            List<Modifier> mods = ModifierRegistry.Parse(modifierSpec);
            EraRule rule = EraRule.Shipped;
            foreach (Modifier m in mods)
            {
                m.Apply(tree, excluded);
                m.Configure(config);
                if (m.EraRuleOverride != null) rule = m.EraRuleOverride;
            }
            tree.Recompute();

            var result = new PanelResult
            {
                Label = label,
                ModifierSpec = modifierSpec,
                Config = config,
                Excluded = excluded,
                ActiveCount = tree.Projects.Count - excluded.Count,
            };

            foreach (ResearchPolicy policy in policies)
            {
                foreach (int seed in seeds)
                {
                    RunResult run = ResearchSim.Run(tree, excluded, rule, config, policy, seed);
                    run.Modifiers = modifierSpec;
                    result.Runs.Add(run);
                    result.EverFinished.UnionWith(run.Finished);
                }
            }
            result.NeverFinished = tree.Projects
                .Where(p => !excluded.Contains(p) && !result.EverFinished.Contains(p))
                .OrderBy(p => p.era!.order)
                .ThenBy(p => p.defName, StringComparer.Ordinal)
                .ToList();

            tree.Restore();
            return result;
        }
    }
}
