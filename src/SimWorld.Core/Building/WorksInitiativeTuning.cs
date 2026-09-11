using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Tuning for <see cref="SettlementWorksInitiative"/> — the public works a settlement raises for itself
    /// beyond bed, wall and store. There is exactly one number here, and the point of this file is the
    /// arithmetic behind it; everything else the initiative decides is derived where it is used
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
        /// Research benches a settlement wants. <b>Two, and this is a sourced number, not a chosen one</b> —
        /// which matters, because the obvious civilization-scale instinct is to scale it with population and
        /// the arithmetic says doing that would wreck the game.
        ///
        /// <para/><see cref="AI.WorkGiver_Research"/> reserves the bench it works
        /// (<c>reservationManager.CanReserve</c>), so benches are researchers: N benches is N citizens
        /// advancing the tree at once, and nothing else in this port bounds how many. The whole authored tech
        /// tree was then sized against a specific number of them. <c>docs/research/tech-reachability.md</c>
        /// §1.2 names "researcher-equivalents: 2, constant" as one of its two dominant assumptions and §4
        /// measures what moving it does, over 24 seeds and a 21-archetype policy panel:
        ///
        /// <list type="bullet">
        /// <item><description>1 researcher — 74.9% of the tree completed in fifty years, era 3.2 of 7.</description></item>
        /// <item><description>2 researchers — 96.4%, era 4.8, and the tree lasts the whole playthrough.</description></item>
        /// <item><description>4 researchers — 100%, and the civilization runs out of research to do on day 1,558.</description></item>
        /// <item><description>8 researchers — dry on day 800. 16 — dry on day 421.</description></item>
        /// </list>
        ///
        /// <para/>That study's own conclusion is the one this constant obeys: "every plausible move makes the
        /// game drier, not richer" (§4). A settlement of two thousand citizens with a bench per fifty of them
        /// would exhaust 245 projects inside the first in-game year and then generate research points forever
        /// with nothing to spend them on — the failure the study measured at 1.32 million wasted points on a
        /// 100-year horizon. So the count is flat, it is two, and two is the figure the content on the other
        /// side of it was balanced for.
        ///
        /// <para/><b>Two rather than one</b>, of the two rows that keep the tree intact: one researcher leaves
        /// 25% of the authored tree unreached at fifty years, which is dead content, and a single bench is
        /// also a single point of failure — one unreachable cell, one citizen asleep, and a civilization stops
        /// researching. The day research throughput becomes a lever the god pulls rather than a constant, this
        /// is the number it should move, and the endless tail (research.endless) is what stops a raised one
        /// from hitting a wall.
        /// </summary>
        public const int ResearchBenchesWanted = 2;
    }
}
