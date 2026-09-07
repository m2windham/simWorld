using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.ReachHarness
{
    /// <summary>What a policy can see when it picks the next project.</summary>
    public sealed class PolicyContext
    {
        public PolicyContext(TreeModel tree, ResearchManager manager, RandomStream rand, EraRule eraRule, HashSet<ResearchProjectDef> excluded)
        {
            Tree = tree;
            Manager = manager;
            Rand = rand;
            EraRule = eraRule;
            Excluded = excluded;
            Active = tree.Projects.Where(p => !excluded.Contains(p)).ToList();
        }

        public TreeModel Tree { get; }

        public ResearchManager Manager { get; }

        public RandomStream Rand { get; }

        public EraRule EraRule { get; }

        /// <summary>Projects a "trim the surface" modifier removed; never candidates, never counted.</summary>
        public HashSet<ResearchProjectDef> Excluded { get; }

        /// <summary>Every project still part of the tree under the current modifier stack.</summary>
        public List<ResearchProjectDef> Active { get; }

        /// <summary>Projects whose prerequisites are met and which are neither finished nor excluded. Refreshed per pick.</summary>
        public List<ResearchProjectDef> Candidates { get; } = new();

        public bool IsActive(ResearchProjectDef p) => !Excluded.Contains(p);

        public bool IsFinished(ResearchProjectDef p) => Manager.IsFinished(p);

        /// <summary>Research points still to pay for a project, after its tech-level cost factor.</summary>
        public float RemainingCost(ResearchProjectDef p) =>
            Math.Max(0f, p.baseCost - Manager.GetProgress(p)) * p.CostFactor(Manager.ResearcherTechLevel);

        public IEnumerable<ResearchProjectDef> UnfinishedActive()
        {
            foreach (ResearchProjectDef p in Active)
            {
                if (!IsFinished(p)) yield return p;
            }
        }

        /// <summary>
        /// Highest era order a project may belong to and still be researchable. int.MaxValue is the shipped
        /// behaviour (no era gate at all); the sim lowers it when RunConfig.EraGatesAvailability is set.
        /// </summary>
        public int OpenEraOrder { get; set; } = int.MaxValue;

        /// <summary>Refreshes <see cref="Candidates"/> from the real runtime gate (ResearchProjectDef.CanStartNow).</summary>
        public void RefreshCandidates()
        {
            Candidates.Clear();
            foreach (ResearchProjectDef p in Active)
            {
                if (!p.CanStartNow) continue;
                if ((p.era?.order ?? 0) > OpenEraOrder) continue;
                Candidates.Add(p);
            }
        }
    }

    /// <summary>
    /// A scripted player. <see cref="Rank"/> returns (tier, key) with lower better; the harness picks the
    /// lowest tier, then the lowest key after multiplicative jitter, so seeds separate near-ties the way a
    /// real player's attention would. <see cref="Prepare"/> runs once per pick for anything expensive.
    /// To add a policy: subclass this and register it in <see cref="PolicyRegistry"/>.
    /// </summary>
    public abstract class ResearchPolicy
    {
        public abstract string Name { get; }

        /// <summary>Replaces RunConfig.Attention when >= 0 — how a negligent archetype is expressed.</summary>
        public virtual float AttentionOverride => -1f;

        /// <summary>Chance per day of abandoning the current project and re-picking. Progress is kept, as in RimWorld.</summary>
        public virtual float SwitchChancePerDay => 0f;

        /// <summary>Called once before each pick; the place for per-pick sets the ranking then reads in O(1).</summary>
        protected virtual void Prepare(PolicyContext ctx)
        {
        }

        protected abstract (int Tier, float Key) Rank(ResearchProjectDef p, PolicyContext ctx);

        public ResearchProjectDef? Choose(PolicyContext ctx, float jitter)
        {
            if (ctx.Candidates.Count == 0) return null;
            Prepare(ctx);
            ResearchProjectDef? best = null;
            int bestTier = int.MaxValue;
            float bestKey = float.MaxValue;
            foreach (ResearchProjectDef p in ctx.Candidates)
            {
                (int tier, float key) = Rank(p, ctx);
                key *= 1f + ctx.Rand.Range(-jitter, jitter);
                if (tier < bestTier || (tier == bestTier && key < bestKey))
                {
                    best = p;
                    bestTier = tier;
                    bestKey = key;
                }
            }
            return best;
        }

        /// <summary>Union of the prerequisite closures of every unfinished target: everything a beeline must buy.</summary>
        protected static HashSet<ResearchProjectDef> RequiredFor(PolicyContext ctx, IEnumerable<ResearchProjectDef> targets)
        {
            var required = new HashSet<ResearchProjectDef>();
            foreach (ResearchProjectDef t in targets)
            {
                if (ctx.IsFinished(t)) continue;
                if (ctx.Tree.Closure.TryGetValue(t, out HashSet<ResearchProjectDef>? closure)) required.UnionWith(closure);
            }
            return required;
        }
    }

    /// <summary>Always takes the cheapest thing on the shelf. The upper bound on "how many rows can be bought".</summary>
    public sealed class CheapestPolicy : ResearchPolicy
    {
        public override string Name => "cheapest";

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) => (0, ctx.RemainingCost(p));
    }

    /// <summary>Uniformly random among what is available: a player who clicks without reading.</summary>
    public sealed class RandomPolicy : ResearchPolicy
    {
        public override string Name => "random";

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) => (0, ctx.Rand.Value);
    }

    /// <summary>The negligent control: mostly not researching at all, and switching target when it does.</summary>
    public sealed class IdlePolicy : ResearchPolicy
    {
        public override string Name => "idle";

        public override float AttentionOverride => 0.2f;

        public override float SwitchChancePerDay => 0.15f;

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) => (0, ctx.Rand.Value);
    }

    /// <summary>Beelines the named track, paying whatever off-track prerequisites that track needs.</summary>
    public sealed class TrackPolicy : ResearchPolicy
    {
        private readonly string track;
        private HashSet<ResearchProjectDef> required = new();

        public TrackPolicy(string track) => this.track = track;

        public override string Name => "track:" + track;

        protected override void Prepare(PolicyContext ctx) =>
            required = RequiredFor(ctx, ctx.UnfinishedActive().Where(q => ctx.Tree.Track[q] == track));

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
            (required.Contains(p) ? 0 : 1, ctx.RemainingCost(p));
    }

    /// <summary>Spreads across every track, always feeding the track it has served least.</summary>
    public sealed class BreadthPolicy : ResearchPolicy
    {
        private Dictionary<string, int> done = new();

        public override string Name => "breadth";

        protected override void Prepare(PolicyContext ctx)
        {
            done = new Dictionary<string, int>();
            foreach (ResearchProjectDef q in ctx.Active)
            {
                if (!ctx.IsFinished(q)) continue;
                string t = ctx.Tree.Track[q];
                done[t] = done.TryGetValue(t, out int n) ? n + 1 : 1;
            }
        }

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
            (done.TryGetValue(ctx.Tree.Track[p], out int n) ? n : 0, ctx.RemainingCost(p));
    }

    /// <summary>Clears the era ladder rung by rung: only what the lowest incomplete era still needs.</summary>
    public sealed class EraRushPolicy : ResearchPolicy
    {
        private HashSet<ResearchProjectDef> required = new();

        public override string Name => "era-rush";

        protected override void Prepare(PolicyContext ctx)
        {
            foreach (EraDef era in ctx.Tree.Eras)
            {
                if (ctx.EraRule.IsComplete(era, ctx)) continue;
                required = RequiredFor(ctx, ctx.EraRule.Targets(era, ctx));
                return;
            }
            required = new HashSet<ResearchProjectDef>(ctx.Active);
        }

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
            (required.Contains(p) ? 0 : 1, ctx.RemainingCost(p));
    }

    /// <summary>Races the deepest unfinished node in the DAG: the "get to the end of the tree" archetype.</summary>
    public sealed class SpirePolicy : ResearchPolicy
    {
        private HashSet<ResearchProjectDef> required = new();

        public override string Name => "spire";

        protected override void Prepare(PolicyContext ctx)
        {
            ResearchProjectDef? deepest = null;
            int bestDepth = -1;
            foreach (ResearchProjectDef q in ctx.UnfinishedActive())
            {
                int d = ctx.Tree.Depth[q];
                if (d > bestDepth)
                {
                    bestDepth = d;
                    deepest = q;
                }
            }
            required = deepest == null
                ? new HashSet<ResearchProjectDef>(ctx.Active)
                : RequiredFor(ctx, new[] { deepest });
        }

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
            (required.Contains(p) ? 0 : 1, ctx.RemainingCost(p));
    }

    /// <summary>Buys structural leverage: cheap nodes that many other nodes are waiting on.</summary>
    public sealed class UnlockPolicy : ResearchPolicy
    {
        public override string Name => "unlockers";

        protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
            (0, ctx.RemainingCost(p) / (1f + 3f * ctx.Tree.OutDegree[p]));
    }

    /// <summary>Every archetype the panel runs. Add a policy here and it joins every report automatically.</summary>
    public static class PolicyRegistry
    {
        public static List<ResearchPolicy> All()
        {
            var list = new List<ResearchPolicy>
            {
                new CheapestPolicy(),
                new BreadthPolicy(),
                new EraRushPolicy(),
                new SpirePolicy(),
                new UnlockPolicy(),
                new RandomPolicy(),
                new IdlePolicy(),
            };
            foreach (string track in TreeModel.Tracks) list.Add(new TrackPolicy(track));
            return list;
        }

        /// <summary>The seven non-track archetypes: the smaller panel the sensitivity sweeps run.</summary>
        public static List<ResearchPolicy> Core() =>
            All().Where(p => !p.Name.StartsWith("track:", StringComparison.Ordinal)).ToList();
    }
}
