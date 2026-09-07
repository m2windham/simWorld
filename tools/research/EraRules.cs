using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// When an era counts as turned, and what an era-rusher therefore aims at. <see cref="Full"/> is the
    /// shipped rule (EraDef.IsComplete: every project in the era finished); the others are candidate design
    /// changes the harness measures without touching EraDef.
    /// </summary>
    public abstract class EraRule
    {
        public abstract string Name { get; }

        /// <summary>Projects this era needs before it turns.</summary>
        public abstract IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx);

        public abstract bool IsComplete(EraDef era, PolicyContext ctx);

        protected static List<ResearchProjectDef> ActiveIn(EraDef era, PolicyContext ctx) =>
            ctx.Tree.Projects.Where(p => p.era == era && ctx.IsActive(p)).ToList();

        /// <summary>EraDef.IsComplete as shipped: 100% of the era's projects.</summary>
        public static readonly EraRule Full = new FullRule();

        private sealed class FullRule : EraRule
        {
            public override string Name => "full";

            public override IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx) => ActiveIn(era, ctx);

            public override bool IsComplete(EraDef era, PolicyContext ctx)
            {
                foreach (ResearchProjectDef p in ActiveIn(era, ctx))
                {
                    if (!ctx.IsFinished(p)) return false;
                }
                return true;
            }
        }

        /// <summary>An era turns at a fraction of its projects; the rest stay optional content.</summary>
        public sealed class Fraction : EraRule
        {
            private readonly float fraction;

            public Fraction(float fraction) => this.fraction = fraction;

            public override string Name => "era" + (int)Math.Round(fraction * 100f);

            private int Needed(EraDef era, PolicyContext ctx) =>
                Math.Max(1, (int)Math.Ceiling(ActiveIn(era, ctx).Count * fraction));

            public override IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx) =>
                ActiveIn(era, ctx).OrderBy(p => ctx.Tree.ClosureCost[p]).Take(Needed(era, ctx));

            public override bool IsComplete(EraDef era, PolicyContext ctx) =>
                ActiveIn(era, ctx).Count(ctx.IsFinished) >= Needed(era, ctx);
        }

        /// <summary>
        /// An era turns on its spine — the projects something else in the tree depends on. Dead-end leaves
        /// become optional flavour rather than a bar in front of the next age.
        /// </summary>
        public sealed class Spine : EraRule
        {
            public override string Name => "spine";

            public override IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx) =>
                ActiveIn(era, ctx).Where(p => ctx.Tree.OutDegree[p] > 0);

            public override bool IsComplete(EraDef era, PolicyContext ctx)
            {
                foreach (ResearchProjectDef p in Targets(era, ctx))
                {
                    if (!ctx.IsFinished(p)) return false;
                }
                return true;
            }
        }
    }
}
