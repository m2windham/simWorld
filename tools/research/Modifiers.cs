using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// A candidate design change, applied to the tree <em>in memory only</em> — the shipped XML is never
    /// written. <see cref="TreeModel.Restore"/> puts costs and prerequisites back between configurations.
    /// To add one: subclass this, then register a prefix in <see cref="ModifierRegistry.Parse"/>.
    /// </summary>
    public abstract class Modifier
    {
        public abstract string Name { get; }

        /// <summary>Edit costs or prerequisites, or add to <paramref name="excluded"/> to cut authored surface.</summary>
        public virtual void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
        }

        /// <summary>Change the run's economy rather than its content.</summary>
        public virtual void Configure(RunConfig config)
        {
        }

        /// <summary>Replace the shipped era-completion rule.</summary>
        public virtual EraRule? EraRuleOverride => null;
    }

    /// <summary>Baseline: the tree exactly as shipped.</summary>
    public sealed class NoModifier : Modifier
    {
        public override string Name => "none";
    }

    /// <summary>Let ResearchManager.ResearcherTechLevel follow CurrentEra, removing the stale CostFactor penalty.</summary>
    public sealed class TechTrackModifier : Modifier
    {
        public override string Name => "techtrack";

        public override void Configure(RunConfig config) => config.TechLevelTracksEra = true;
    }

    /// <summary>Raise research throughput by a flat multiplier (a civilization researching, not a colony).</summary>
    public sealed class ThroughputModifier : Modifier
    {
        private readonly float factor;

        public ThroughputModifier(float factor) => this.factor = factor;

        public override string Name => "throughput" + factor.ToString("0.##", CultureInfo.InvariantCulture);

        public override void Configure(RunConfig config) => config.ThroughputMultiplier *= factor;
    }

    /// <summary>
    /// Cut authored surface to at most N projects per era, dropping dead-end leaves first (highest cost
    /// among projects nothing else still depends on), so the surviving DAG never dangles.
    /// </summary>
    public sealed class TrimModifier : Modifier
    {
        private readonly int perEra;

        public TrimModifier(int perEra) => this.perEra = perEra;

        public override string Name => "trim" + perEra;

        public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            // Swept to a fixed point: cutting a leaf in a late era can make a node in an early era a leaf,
            // and eras are visited in order, so one pass would stop short of what the rule actually allows.
            bool cut = true;
            while (cut)
            {
                cut = false;
                foreach (EraDef era in tree.Eras)
                {
                    List<ResearchProjectDef> active = tree.InEra(era).Where(p => !excluded.Contains(p)).ToList();
                    while (active.Count > perEra)
                    {
                        ResearchProjectDef? victim = active
                            .Where(p => ActiveOutDegree(p, tree, excluded) == 0)
                            .OrderByDescending(p => p.baseCost)
                            .ThenBy(p => p.defName, StringComparer.Ordinal)
                            .FirstOrDefault();
                        if (victim == null) break; // every survivor carries a dependent: nothing left to cut safely
                        excluded.Add(victim);
                        active.Remove(victim);
                        cut = true;
                    }
                }
            }
        }

        private static int ActiveOutDegree(ResearchProjectDef p, TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            int n = 0;
            foreach (ResearchProjectDef q in tree.Projects)
            {
                if (excluded.Contains(q) || q.prerequisites == null) continue;
                if (q.prerequisites.Contains(p)) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Cap prerequisite depth at D by rebasing: a too-deep project's deepest prerequisite is replaced with
    /// an ancestor of that prerequisite shallow enough to fit. Nothing becomes a root, so the tree keeps its
    /// "only the first era has roots" shape.
    /// </summary>
    public sealed class FlattenModifier : Modifier
    {
        private readonly int maxDepth;

        public FlattenModifier(int maxDepth) => this.maxDepth = maxDepth;

        public override string Name => "flatten" + maxDepth;

        public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            for (int guard = 0; guard < 10000; guard++)
            {
                ResearchProjectDef? worst = tree.Projects
                    .Where(p => tree.Depth[p] > maxDepth && p.prerequisites is { Count: > 0 })
                    .OrderBy(p => tree.Depth[p])
                    .FirstOrDefault();
                if (worst == null) return;

                List<ResearchProjectDef> prereqs = worst.prerequisites!;
                ResearchProjectDef deepest = prereqs.OrderByDescending(q => tree.Depth[q]).First();
                ResearchProjectDef replacement = ShallowestUsableAncestor(deepest, tree);
                prereqs.Remove(deepest);
                if (!prereqs.Contains(replacement) && replacement != worst) prereqs.Add(replacement);
                if (prereqs.Count == 0) prereqs.Add(replacement);
                tree.Recompute();
            }
        }

        /// <summary>Walks up the deepest chain until the node fits under the cap; the node itself if it already does.</summary>
        private ResearchProjectDef ShallowestUsableAncestor(ResearchProjectDef start, TreeModel tree)
        {
            ResearchProjectDef cur = start;
            while (tree.Depth[cur] > maxDepth - 1 && cur.prerequisites is { Count: > 0 })
            {
                cur = cur.prerequisites.OrderByDescending(q => tree.Depth[q]).First();
            }
            return cur;
        }
    }

    /// <summary>
    /// Freeze the era cost escalation at era E: every later era's project is rescaled to era E's mean.
    /// Epoch's capped-quadratic-then-flat finding, applied to SimWorld's per-era cost bands.
    /// </summary>
    public sealed class CostFreezeModifier : Modifier
    {
        private readonly int freezeAtOrder;

        public CostFreezeModifier(int freezeAtOrder) => this.freezeAtOrder = freezeAtOrder;

        public override string Name => "costfreeze" + freezeAtOrder;

        public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            Dictionary<EraDef, float> mean = tree.Eras.ToDictionary(e => e, e => tree.InEra(e).Average(p => p.baseCost));
            EraDef? anchor = tree.Eras.FirstOrDefault(e => e.order == freezeAtOrder);
            if (anchor == null) return;
            foreach (EraDef era in tree.Eras)
            {
                if (era.order <= freezeAtOrder) continue;
                float scale = mean[anchor] / mean[era];
                foreach (ResearchProjectDef p in tree.InEra(era)) p.baseCost = MathF.Round(p.baseCost * scale / 10f) * 10f;
            }
            tree.Recompute();
        }
    }

    /// <summary>
    /// Scale every project's cost by a constant. Not a proposal on its own — the instrument that answers
    /// "how much more expensive would the tree have to be before a 50-year game cannot buy it out".
    /// </summary>
    public sealed class CostScaleModifier : Modifier
    {
        private readonly float factor;

        public CostScaleModifier(float factor) => this.factor = factor;

        public override string Name => "costscale" + factor.ToString("0.##", CultureInfo.InvariantCulture);

        public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            foreach (ResearchProjectDef p in tree.Projects) p.baseCost = MathF.Round(p.baseCost * factor / 10f) * 10f;
            tree.Recompute();
        }
    }

    /// <summary>
    /// Multiply the cost of every project nothing else depends on — the leaves — leaving the spine alone.
    /// The candidate answer to what the era-shape measurement found: a civilization sees ~90% of an era before
    /// leaving it because the optional content is cheap enough to sweep up while working the spine that
    /// actually gates the age. Make the flavour cost something and it becomes a choice instead of a formality.
    /// </summary>
    public sealed class LeafCostModifier : Modifier
    {
        private readonly float factor;

        public LeafCostModifier(float factor) => this.factor = factor;

        public override string Name => "leafcost" + factor.ToString("0.##", CultureInfo.InvariantCulture);

        public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded)
        {
            foreach (ResearchProjectDef p in tree.Projects)
            {
                if (tree.OutDegree[p] > 0) continue; // spine: the era waits on it, so its price sets the pace
                p.baseCost = MathF.Round(p.baseCost * factor / 10f) * 10f;
            }
            tree.Recompute();
        }
    }

    /// <summary>
    /// Make the era ladder gate content instead of only labelling it: a project becomes researchable when
    /// its era opens, which happens when every earlier era is complete under the active era rule.
    /// </summary>
    public sealed class EraGateModifier : Modifier
    {
        public override string Name => "eragate";

        public override void Configure(RunConfig config) => config.EraGatesAvailability = true;
    }

    /// <summary>Swap the era-completion rule for a lateral one.</summary>
    public sealed class EraRuleModifier : Modifier
    {
        private readonly EraRule rule;

        public EraRuleModifier(EraRule rule) => this.rule = rule;

        public override string Name => rule.Name;

        public override EraRule? EraRuleOverride => rule;
    }

    /// <summary>Parses a comma-separated modifier stack, e.g. "techtrack,throughput4".</summary>
    public static class ModifierRegistry
    {
        public static List<Modifier> Parse(string spec)
        {
            var list = new List<Modifier>();
            foreach (string raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (token.Length == 0 || token == "none") continue;
                list.Add(ParseOne(token));
            }
            if (list.Count == 0) list.Add(new NoModifier());
            return list;
        }

        private static Modifier ParseOne(string token)
        {
            if (token == "techtrack") return new TechTrackModifier();
            if (token == "spine") return new EraRuleModifier(new EraRule.Spine());
            if (token == "eragate") return new EraGateModifier();
            if (TryNumeric(token, "leafcost", out float leaf)) return new LeafCostModifier(leaf);
            if (TryNumeric(token, "costscale", out float scale)) return new CostScaleModifier(scale);
            if (TryNumeric(token, "throughput", out float t)) return new ThroughputModifier(t);
            if (TryNumeric(token, "trim", out float trim)) return new TrimModifier((int)trim);
            if (TryNumeric(token, "flatten", out float flat)) return new FlattenModifier((int)flat);
            if (TryNumeric(token, "costfreeze", out float freeze)) return new CostFreezeModifier((int)freeze);
            if (TryNumeric(token, "era", out float pct)) return new EraRuleModifier(new EraRule.Fraction(pct / 100f));
            throw new ArgumentException("unknown modifier: " + token);
        }

        private static bool TryNumeric(string token, string prefix, out float value)
        {
            value = 0f;
            if (!token.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string rest = token.Substring(prefix.Length);
            return float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
