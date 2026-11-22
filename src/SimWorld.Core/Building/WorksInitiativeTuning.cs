using System;

using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="SettlementWorksInitiative"/> — the public works a settlement raises for itself
    /// beyond bed, wall and store. The point of this file is the arithmetic behind its numbers;
    /// everything else the initiative decides is derived where it is used
    /// (<see cref="SculptureMaterials.SculpturesWantedOf"/> off the beauty sampler,
    /// <see cref="SettlementWorksInitiative.ArtClusterRadius"/> off that same sampler's radius) rather than
    /// written down as a figure somebody would later have to justify.
    /// </summary>
    public static class WorksInitiativeTuning
    {
        /// <summary>
        /// Self-gate cadence, the same <see cref="GenTicks.TickRareInterval"/>
        /// <c>ConstructionInitiativeTuning.IntervalTicks</c> and <c>Crafting.StonecuttingTuning.IntervalTicks</c>
        /// already use. Deliberately identical to those two: this initiative places blueprints alongside the
        /// ones they place, and three settlement-scale deciders running on different clocks would only make
        /// which of them noticed a change first depend on the tick number.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// Research benches a settlement of <paramref name="citizens"/> wants while <paramref name="era"/> is
        /// its current era: one per <see cref="EraDef.citizensPerScholar"/> citizens, rounded to the nearest
        /// whole bench, and never fewer than one. With no era loaded, one bench per 25 citizens.
        ///
        /// <para/><b>The RimWorld mechanic.</b> <see cref="AI.WorkGiver_Research"/> reserves the bench it works,
        /// so benches are researchers: N benches is N citizens advancing the tree at once. A RimWorld player
        /// decides how many to build.
        ///
        /// <para/><b>How the arc changes it.</b> This was a flat 2, sourced from the reachability study's
        /// modelled economy (<c>docs/research/tech-reachability.md</c> §1.2), which warned that more researchers
        /// would run the tree dry. Played, a flat 2 cut research off from the settlement: a band of 40
        /// researched about 8% faster than a band of 20 (<c>docs/research/era-arc.md</c> §2.1), and nothing
        /// the settlement became changed how fast it climbed. Now a bigger settlement studies more, and a more
        /// advanced one spares a larger share of its people for it. The dry-tree worry is answered twice: the
        /// endless tail (research.endless) means there is always something to study, and each era's cost
        /// multiplier (<see cref="EraDef.researchCostFactor"/>) was derived against the scholar count this
        /// returns for a nominal settlement, so the faster late game is already priced in.
        ///
        /// <para/>The ratios are content and not sourced; <c>ResearchBenchNeedTests</c> pins the behaviour: at
        /// least one, never fewer as the population or the era rises, and a bigger settlement more.
        /// </summary>
        public static int ResearchBenchesWanted(int citizens, EraDef? era)
        {
            if (citizens <= 0) return 0;
            float perScholar = era?.citizensPerScholar ?? DefaultCitizensPerScholar;
            int wanted = (int)Math.Floor(citizens / perScholar + 0.5f);
            return Math.Max(1, wanted);
        }

        /// <summary>The bench count for <paramref name="settlement"/>'s citizens at the civilization's current
        /// era (<see cref="ResearchManager.CurrentEra"/>).</summary>
        public static int ResearchBenchesWanted(Settlement settlement) =>
            ResearchBenchesWanted(settlement.Citizens.Count, Find.ResearchManager.CurrentEra);

        /// <summary>Citizens per bench with no era loaded: the same as the first era's in content.</summary>
        private const float DefaultCitizensPerScholar = 25f;
    }
}
