using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// When an era counts as turned, and what an era-rusher therefore aims at. <see cref="Shipped"/> asks the
    /// real <c>EraDef</c>, so the harness cannot drift from the game; the others are candidate design changes
    /// measured against it.
    /// <para/>
    /// <b>It has drifted once already.</b> <see cref="Full"/> was the shipped rule and was documented as such,
    /// then <c>EraDef.IsComplete</c> moved to the spine rule and this file was not updated — so every era-shape
    /// measurement taken afterwards was measuring a rule the game no longer used, and measuring it
    /// tautologically: under <see cref="Full"/> an era turns exactly when 100% of it is done, which makes "how
    /// much of the era had they seen when it turned" 100% by construction. Hence <see cref="Shipped"/>.
    /// </summary>
    public abstract class EraRule
    {
        public abstract string Name { get; }

        /// <summary>Projects this era needs before it turns.</summary>
        public abstract IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx);

        public abstract bool IsComplete(EraDef era, PolicyContext ctx);

        protected static List<ResearchProjectDef> ActiveIn(EraDef era, PolicyContext ctx) =>
            ctx.Tree.Projects.Where(p => p.era == era && ctx.IsActive(p)).ToList();

        /// <summary>
        /// Whatever <c>EraDef</c> itself does today, asked directly rather than reimplemented. Use this as the
        /// baseline for any measurement meant to describe the shipped game.
        /// </summary>
        public static readonly EraRule Shipped = new ShippedRule();

        /// <summary>100% of the era's projects. Was the shipped rule; is now a candidate, kept for comparison.</summary>
        public static readonly EraRule Full = new FullRule();

        private sealed class ShippedRule : EraRule
        {
            public override string Name => "shipped";

            public override IEnumerable<ResearchProjectDef> Targets(EraDef era, PolicyContext ctx)
            {
                IReadOnlyList<ResearchProjectDef> spine = era.SpineProjects;
                IEnumerable<ResearchProjectDef> targets = spine.Count > 0 ? spine : ActiveIn(era, ctx);
                return targets.Where(ctx.IsActive);
            }

            public override bool IsComplete(EraDef era, PolicyContext ctx) => era.IsComplete;
        }

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
