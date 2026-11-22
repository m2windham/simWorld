using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// How the era ladder is paced: what an era's research costs, and which era a civilization is working
    /// toward (system: research, translation "era pacing").
    ///
    /// <para/><b>The RimWorld mechanic.</b> A project costs <see cref="ResearchProjectDef.baseCost"/> points,
    /// multiplied by <see cref="ResearchProjectDef.CostFactor"/> when it sits above the researcher's tech
    /// level, and researchers at benches earn the points. RimWorld paces a colony's climb with those two
    /// numbers and nothing else.
    ///
    /// <para/><b>How the arc changes the answer.</b> RimWorld prices its tree for one colony over a few years.
    /// SimWorld's tree is one settlement over eight eras, and played at RimWorld's price the first four eras
    /// went by in 26 to 78 days each while Medieval and Industrial took a year or more
    /// (<c>docs/research/era-arc.md</c> §2). The arc wants each era to last long enough to be lived in. So each
    /// era carries a cost multiplier of its own (<see cref="EraDef.researchCostFactor"/>), applied in the same
    /// place and the same way as RimWorld's tech-level multiplier. The values are derived, era by era, from an
    /// intended era length, the scholars a nominal settlement has in that era and the points a scholar earns
    /// in a day; <c>Eras.xml</c> carries the arithmetic.
    /// </summary>
    public static class EraPacing
    {
        /// <summary>
        /// The era multiplier on <paramref name="project"/>'s cost: its own era's
        /// <see cref="EraDef.researchCostFactor"/>. A generated endless project carries no era
        /// (<see cref="EndlessResearch"/>) but comes after the whole ladder, so it is priced at the last era's
        /// factor. Without that the tail would open cheaper than the era it follows. Any other project with no
        /// era is off the ladder and is not paced.
        /// </summary>
        public static float CostFactorFor(ResearchProjectDef project)
        {
            if (project.era != null) return project.era.researchCostFactor;
            if (!EndlessResearch.IsGenerated(project)) return 1f;
            EraDef? last = null;
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            for (int i = 0; i < eras.Count; i++)
            {
                if (last == null || eras[i].order > last.order) last = eras[i];
            }
            return last?.researchCostFactor ?? 1f;
        }

        /// <summary>
        /// The era the civilization is working toward: the earliest era, by order, that is not yet
        /// <see cref="EraDef.IsComplete"/>. Null once every era on the ladder is complete. This is
        /// <see cref="ResearchManager.NextEra"/> except at the very start, where <see cref="ResearchManager.CurrentEra"/>
        /// falls back to the first era before its spine is done and so names the era after it as next.
        /// </summary>
        public static EraDef? FrontierEra()
        {
            EraDef? frontier = null;
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            for (int i = 0; i < eras.Count; i++)
            {
                EraDef era = eras[i];
                if (era.IsComplete) continue;
                if (frontier == null || era.order < frontier.order) frontier = era;
            }
            return frontier;
        }

        /// <summary>
        /// True when <paramref name="project"/> belongs to an era past <paramref name="frontier"/>, so the
        /// agenda leaves it until the frontier turns. A project with no era is off the ladder and never waits,
        /// and with no frontier (the whole ladder complete) nothing does.
        /// </summary>
        public static bool IsPastFrontier(ResearchProjectDef project, EraDef? frontier) =>
            frontier != null && project.era != null && project.era.order > frontier.order;
    }
}
