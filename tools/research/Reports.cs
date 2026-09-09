using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>Text tables. Everything the harness prints is meant to be pasteable into docs/research/tech-reachability.md.</summary>
    public static class Reports
    {
        public static void Heading(string text)
        {
            Console.WriteLine();
            Console.WriteLine("== " + text + " " + new string('=', Math.Max(0, 76 - text.Length)));
        }

        /// <summary>Static shape of the shipped tree: size, cost, depth, connectivity and unlock consequence.</summary>
        public static void TreeStats(TreeModel tree)
        {
            Heading("Shipped tree");
            Console.WriteLine($"projects={tree.Projects.Count}  eras={tree.Eras.Count}  tracks={TreeModel.Tracks.Length}");
            Console.WriteLine($"total baseCost={tree.Projects.Sum(p => p.baseCost):N0}  max prerequisite depth={tree.Depth.Values.Max()}");
            int unlockers = tree.Projects.Count(p => p.UnlockedDefs.Count > 0);
            Console.WriteLine($"projects with at least one unlocked Def (IResearchUnlockable): {unlockers} of {tree.Projects.Count}");
            Console.WriteLine($"projects nothing else depends on (dead-end leaves): {tree.Projects.Count(p => tree.OutDegree[p] == 0)}");
            Console.WriteLine();
            Console.WriteLine($"{"era",-18}{"n",4}{"cost sum",12}{"mean",9}{"depth",10}{"leaves",8}{"cheapest closure",18}");
            foreach (EraDef era in tree.Eras)
            {
                List<ResearchProjectDef> ps = tree.InEra(era).ToList();
                if (ps.Count == 0) continue;
                string depth = $"{ps.Min(p => tree.Depth[p])}-{ps.Max(p => tree.Depth[p])}";
                Console.WriteLine($"{era.defName,-18}{ps.Count,4}{ps.Sum(p => p.baseCost),12:N0}{ps.Average(p => p.baseCost),9:N0}{depth,10}" +
                                  $"{ps.Count(p => tree.OutDegree[p] == 0),8}{ps.Min(p => tree.ClosureCost[p]),18:N0}");
            }
            Console.WriteLine();
            Console.WriteLine($"{"track",-26}{"n",4}{"cost sum",12}{"leaves",8}");
            foreach (string track in TreeModel.Tracks)
            {
                List<ResearchProjectDef> ps = tree.InTrack(track).ToList();
                Console.WriteLine($"{track,-26}{ps.Count,4}{ps.Sum(p => p.baseCost),12:N0}{ps.Count(p => tree.OutDegree[p] == 0),8}");
            }
            Console.WriteLine();
            Console.WriteLine("Cost after CostFactor at a researcher tech level pinned to each start era:");
            foreach (EraDef start in tree.Eras)
            {
                float paid = tree.Projects.Where(p => (p.era?.order ?? 0) >= start.order)
                                          .Sum(p => p.baseCost * p.CostFactor(start.techLevel));
                int granted = tree.Projects.Count(p => (p.era?.order ?? 0) < start.order);
                Console.WriteLine($"  start={start.defName,-18} granted={granted,4}  still to buy={tree.Projects.Count - granted,4}  points={paid,12:N0}");
            }
        }

        public static void PanelSummary(PanelResult panel)
        {
            Heading($"{panel.Label}  [modifiers: {panel.ModifierSpec}]");
            RunConfig c = panel.Config;
            Console.WriteLine($"horizon={c.HorizonYears}y ({c.HorizonDays}d)  start={c.StartEra}  researchers={c.Researchers:0.##}" +
                              $"{(c.ResearcherGrowthPerYear > 0 ? $" (+{c.ResearcherGrowthPerYear:P0}/y, cap {c.MaxResearchers:0})" : "")}" +
                              $"  skill={c.StartSkill}{(c.SkillGrowth ? "+" : "")}  attention={c.Attention:0.##}" +
                              $"  throughput x{c.ThroughputMultiplier:0.##}  techLevelTracksEra={c.TechLevelTracksEra}");
            Console.WriteLine($"active projects={panel.ActiveCount}  runs={panel.Runs.Count} ({panel.Policies.Count()} policies x {panel.Runs.Select(r => r.Seed).Distinct().Count()} seeds)");
            Console.WriteLine();
            Console.WriteLine($"mean completed  all archetypes {panel.MeanCompletedFraction:P1}   engaged only {panel.EngagedCompletedFraction:P1}" +
                              $"   ({panel.Engaged.Average(r => r.Finished.Count):0.0} of {panel.ActiveCount})");
            Console.WriteLine($"union over every run: {panel.EverFinished.Count} reached, {panel.NeverFinished.Count} never reached by any policy on any seed");
            Console.WriteLine($"policy overlap (mean pairwise Jaccard, 1.0 = every archetype ends identically): all {panel.PolicyOverlap:0.000}  engaged {panel.EngagedOverlap:0.000}");
            int dry = panel.MedianEngagedDryDay;
            Console.WriteLine($"engaged runs that went dry: {panel.EngagedDryRate:P0}" +
                              (dry < 0 ? "" : $", median first dry day {dry} (year {dry / 60.0:0.0})"));
        }

        public static void PerPolicy(PanelResult panel)
        {
            Console.WriteLine();
            Console.WriteLine($"{"policy",-28}{"done",7}{"%tree",8}{"earned",8}{"maxEra",8}{"dry%",7}{"dryDay",8}{"wasted pts",13}{"skill",7}");
            foreach (string policy in panel.Policies)
            {
                List<RunResult> runs = panel.For(policy).ToList();
                double pct = runs.Average(r => (double)r.Finished.Count / r.ActiveCount);
                List<int> dryDays = runs.Where(r => r.FirstDryDay >= 0).Select(r => r.FirstDryDay).OrderBy(d => d).ToList();
                string dry = dryDays.Count == 0 ? "-" : dryDays[dryDays.Count / 2].ToString(CultureInfo.InvariantCulture);
                Console.WriteLine($"{policy,-28}{runs.Average(r => r.Finished.Count),7:0.0}{pct,8:P1}" +
                                  $"{runs.Average(r => r.Finished.Count - r.GrantedCount),8:0.0}" +
                                  $"{runs.Average(r => r.MaxEraOrder),8:0.00}" +
                                  $"{runs.Count(r => r.FirstDryDay >= 0) / (double)runs.Count,7:P0}{dry,8}" +
                                  $"{runs.Average(r => r.PointsWasted),13:N0}{runs.Average(r => r.FinalSkill),7:0.0}");
            }
        }

        public static void EraTable(PanelResult panel, TreeModel tree)
        {
            Console.WriteLine();
            Console.WriteLine($"{"era",-18}{"reach%",9}{"median day",12}{"median year",13}{"spread (d)",12}");
            for (int order = 0; order < tree.Eras.Count; order++)
            {
                EraDef era = tree.Eras[order];
                int day = panel.MedianEraTurn(order);
                int spread = panel.EraTurnSpread(order);
                string dayText = day < 0 ? "never" : day.ToString(CultureInfo.InvariantCulture);
                string yearText = day < 0 ? "-" : (day / 60.0).ToString("0.0", CultureInfo.InvariantCulture);
                string spreadText = spread < 0 ? "-" : spread.ToString(CultureInfo.InvariantCulture);
                Console.WriteLine($"{era.defName,-18}{panel.EraReachRate(order),9:P0}{dayText,12}{yearText,13}{spreadText,12}");
            }
            Console.WriteLine("(spread = mean days between the first and last engaged archetype turning that era on a seed)");
        }

        /// <summary>
        /// The question the horizon decision actually leaves open. Years are not the currency — a century in
        /// which nothing happened costs nothing to pass — so "can a 50-year civilization have everything" is
        /// a question about a unit that no longer applies. What survives the reframing is per era: by the time
        /// a civilization leaves an era behind, how much of that era had it actually seen, and did two
        /// different civilizations see the same things?
        ///
        /// <list type="bullet">
        /// <item><b>seen%</b>: of the era's own projects, the share finished at the moment that era turned.
        /// 100% means the era offered no choice at all; very low means most of its content is scenery.</item>
        /// <item><b>overlap</b>: mean pairwise Jaccard of what two engaged archetypes had finished at that
        /// same moment. 1.00 means every civilization walks the identical path — the failure the original
        /// "playstyles diverge" phrasing was really about.</item>
        /// </list>
        /// </summary>
        public static void EraShape(PanelResult panel, TreeModel tree)
        {
            Console.WriteLine();
            Console.WriteLine($"{"era",-18}{"projects",10}{"spine%",8}{"seen%",8}{"overlap",9}{"reached",9}");
            for (int order = 0; order < tree.Eras.Count; order++)
            {
                EraDef era = tree.Eras[order];
                List<ResearchProjectDef> inEra = tree.InEra(era).ToList();
                var snapshots = new List<HashSet<ResearchProjectDef>>();
                foreach (RunResult run in panel.Engaged)
                {
                    if (order >= run.FinishedAtEraTurn.Length) continue;
                    HashSet<ResearchProjectDef>? snap = run.FinishedAtEraTurn[order];
                    if (snap != null) snapshots.Add(snap);
                }

                // How much of the era is spine is the ceiling on how much two civilizations can differ:
                // everyone must finish the spine to leave the era at all, so only the leaves are ever a choice.
                double spineShare = inEra.Count == 0 ? 0 : inEra.Count(p => tree.OutDegree[p] > 0) / (double)inEra.Count;

                if (snapshots.Count == 0 || inEra.Count == 0)
                {
                    Console.WriteLine($"{era.defName,-18}{inEra.Count,10}{spineShare,8:P0}{"-",8}{"-",9}{"never",9}");
                    continue;
                }

                double seen = snapshots.Average(snap => inEra.Count(p => snap.Contains(p)) / (double)inEra.Count);
                double overlap = MeanPairwiseOverlap(snapshots, inEra);
                double reached = snapshots.Count / (double)panel.Engaged.Count;
                Console.WriteLine($"{era.defName,-18}{inEra.Count,10}{spineShare,8:P0}{seen,8:P0}{overlap,9:0.00}{reached,9:P0}");
            }
            Console.WriteLine("(spine% = share of the era something else depends on, which every civilization must finish to leave");
            Console.WriteLine(" the era — the structural ceiling on how much two civilizations can differ within it;");
            Console.WriteLine(" seen% = share of that era's own projects finished when the era turned; overlap = mean pairwise");
            Console.WriteLine(" Jaccard of those sets across engaged archetypes; reached = share of runs that turned the era at all)");
        }

        /// <summary>Mean pairwise Jaccard similarity of what each run had finished <i>within one era</i>.</summary>
        private static double MeanPairwiseOverlap(List<HashSet<ResearchProjectDef>> snapshots, List<ResearchProjectDef> inEra)
        {
            if (snapshots.Count < 2) return 1.0;

            double total = 0;
            int pairs = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                for (int j = i + 1; j < snapshots.Count; j++)
                {
                    var a = inEra.Where(snapshots[i].Contains).ToHashSet();
                    var b = inEra.Where(snapshots[j].Contains).ToHashSet();
                    int union = a.Union(b).Count();
                    total += union == 0 ? 1.0 : a.Intersect(b).Count() / (double)union;
                    pairs++;
                }
            }
            return pairs == 0 ? 1.0 : total / pairs;
        }

        public static void DeadContent(PanelResult panel, TreeModel tree, int listLimit = 0)
        {
            Console.WriteLine();
            Console.WriteLine("Never reached by any policy on any seed:");
            Console.WriteLine($"{"era",-18}{"never",7}{"of",5}{"share",9}");
            foreach (EraDef era in tree.Eras)
            {
                int total = tree.InEra(era).Count(p => !panel.Excluded.Contains(p));
                if (total == 0) continue;
                int never = panel.NeverFinished.Count(p => p.era == era);
                Console.WriteLine($"{era.defName,-18}{never,7}{total,5}{(double)never / total,9:P0}");
            }
            Console.WriteLine();
            Console.WriteLine($"{"track",-26}{"never",7}{"of",5}{"share",9}");
            foreach (string track in TreeModel.Tracks)
            {
                int total = tree.InTrack(track).Count(p => !panel.Excluded.Contains(p));
                if (total == 0) continue;
                int never = panel.NeverFinished.Count(p => tree.Track[p] == track);
                Console.WriteLine($"{track,-26}{never,7}{total,5}{(double)never / total,9:P0}");
            }
            if (listLimit > 0 && panel.NeverFinished.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Never-reached projects" + (panel.NeverFinished.Count > listLimit ? $" (first {listLimit})" : "") + ":");
                var sb = new StringBuilder("  ");
                foreach (ResearchProjectDef p in panel.NeverFinished.Take(listLimit))
                {
                    if (sb.Length > 100)
                    {
                        Console.WriteLine(sb.ToString());
                        sb = new StringBuilder("  ");
                    }
                    sb.Append(p.defName).Append(' ');
                }
                if (sb.Length > 2) Console.WriteLine(sb.ToString());
            }
        }

        /// <summary>One line per configuration — the table the candidate-fix comparison is built from.</summary>
        public static void Compare(string title, IReadOnlyList<PanelResult> panels)
        {
            Heading(title);
            Console.WriteLine($"{"configuration",-34}{"active",7}{"done",7}{"%tree",8}{"maxEra",8}{"dead",6}{"overlap",9}{"dry%",7}{"dryDay",8}");
            foreach (PanelResult p in panels)
            {
                int dry = p.MedianEngagedDryDay;
                Console.WriteLine($"{p.Label,-34}{p.ActiveCount,7}{p.Engaged.Average(r => r.Finished.Count),7:0.0}" +
                                  $"{p.EngagedCompletedFraction,8:P1}{p.EngagedMaxEraOrder,8:0.00}{p.NeverFinished.Count,6}" +
                                  $"{p.EngagedOverlap,9:0.000}{p.EngagedDryRate,7:P0}{(dry < 0 ? "-" : dry.ToString(CultureInfo.InvariantCulture)),8}");
            }
            int policyCount = panels.Count == 0 ? 0 : panels[0].Policies.Count();
            int seedCount = panels.Count == 0 ? 0 : panels[0].Runs.Select(r => r.Seed).Distinct().Count();
            Console.WriteLine($"({policyCount} archetypes x {seedCount} seeds; every column is the engaged arm, the idle control excluded)");
        }
    }
}
