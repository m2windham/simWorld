using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// Tuning for <see cref="SettlementSubsistence"/>. RimWorld has nothing to port here — its world-tile
    /// settlements produce nothing at all and its colony's whole economy is a map — so every figure below is
    /// this port's own, and every one is <b>read off a number this codebase already committed to</b> rather
    /// than chosen. <c>SettlementSubsistenceTests</c> pins each as an ordering, a band or a trend, never as
    /// the literal, exactly as CLAUDE.md requires for an unsourced number.
    /// </summary>
    public static class SettlementSubsistenceTuning
    {
        /// <summary>
        /// Self-gate cadence for the abstract production pass: <b>the same clock the ledger's consumer runs
        /// on</b> (<see cref="SettlementLarderTuning.IntervalTicks"/>, itself
        /// <see cref="GenTicks.TickLongInterval"/> because that is the cadence an off-map citizen's hunger
        /// actually moves on). The two halves of one book must not be read on two clocks: production on a
        /// faster cadence would only ever find the same ledger twice, and on a slower one would let a
        /// settlement's stores swing by a whole interval of eating before anything answered.
        /// <para/>
        /// It is also the cadence <c>Crafting.GuildTuning.GuildIntervalTicks</c> uses, so what a settlement
        /// produces and what its industry spends move together, and a multiple of
        /// <c>Economy.SettlementStockTuning.IntervalTicks</c> (250 × 8), so the map-side banking pass is
        /// always current on the tick this one reads the ledger.
        /// </summary>
        public const int IntervalTicks = SettlementLarderTuning.IntervalTicks;

        /// <summary>
        /// How much a settlement's hands aim to produce relative to what its mouths burn, on land that is as
        /// good as this world's content knows.
        ///
        /// <para/><b>Derived, not chosen.</b> It is the one statement this codebase already makes about that
        /// ratio: <see cref="FarmingTuning.FieldSizeMargin"/> is the multiple of break-even that
        /// <c>Building.FarmingInitiative.FieldCellsWantedFor</c> sizes a settlement's field at — "a settlement
        /// sows this much more ground than it strictly eats". On a map that margin is spent on losses the map
        /// itself imposes (growth below 1 for most of a real day, an unripe plant cut early, the difficulty's
        /// own crop-yield factor); off a map there is no map to lose it on, so the same intent arrives with
        /// <see cref="LandYieldFactor"/> standing in for the loss term instead. A settlement on the best land
        /// content ships realises the whole intent; one on thin land realises a fraction of it and can fall
        /// short of its own mouths, which is the model saying something true about that land rather than a
        /// defect — see <see cref="SettlementSubsistence"/>'s own doc.
        /// </summary>
        public static float YieldRatio => FarmingTuning.FieldSizeMargin;

        /// <summary>
        /// How much of <see cref="YieldRatio"/> the land under a settlement actually returns: its biome's own
        /// <see cref="BiomeDef.forageability"/> as a share of the best forageability content ships. 1 on the
        /// most generous land in the world, 0 where nothing edible grows at all (the ice sheet, every water
        /// biome) — and therefore no production at all there.
        ///
        /// <para/><b>Why this field and no other.</b> <see cref="BiomeDef.forageability"/>'s own doc reads
        /// "0..1; how much food a forager can find here", it is authored for all eleven land biomes from an
        /// extreme desert's 0.02 to a tropical rainforest's 0.5, and <c>Building.WildFoodTuning</c> already
        /// established reading it as literal food rather than as a dryness proxy. It is the only number in
        /// content that answers "how much food does this land give".
        ///
        /// <para/><b>Why normalised against content rather than against its own 0..1 range.</b> Read raw, the
        /// shipped range tops out at 0.5, so even a tropical rainforest would return half of what a settlement
        /// sets out to grow and <i>every</i> settlement in the world would starve — the field is authored as a
        /// relative ranking of biomes, not as an absolute yield. Taking the best land in content as the
        /// reference is the same discipline <see cref="SettlementLarderTuning.DaysToFirstHarvest"/> (the
        /// slowest shipped crop) and <c>SettlementLarder.ProvisionDef</c> (the densest shipped ration) already
        /// use: read the answer off the content that already states it, so new content shifts the model
        /// without a code change.
        /// </summary>
        public static float LandYieldFactor(BiomeDef? biome)
        {
            if (biome == null || biome.forageability <= 0f) return 0f;
            float best = BestForageabilityInContent;
            if (best <= 0f) return 0f;
            float factor = biome.forageability / best;
            return factor > 1f ? 1f : factor;
        }

        /// <summary>The most food-bearing land this world's content knows, read off the shipped
        /// <see cref="BiomeDef"/> list. One walk of eleven defs; see <see cref="SettlementSubsistence.Tick"/>
        /// for where in a pass it is paid.</summary>
        public static float BestForageabilityInContent
        {
            get
            {
                float best = 0f;
                IReadOnlyList<BiomeDef> all = DefDatabase<BiomeDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].forageability > best) best = all[i].forageability;
                }
                return best;
            }
        }

        /// <summary>
        /// Days of food a settlement keeps in its ledger before it stops producing into it — the food
        /// economy's one statement of "how much a settlement wants in hand"
        /// (<see cref="HuntingTuning.DaysOfFoodWanted"/>), which <c>AI.HuntingInitiative.WantsMeat</c> already
        /// gates hunting on and <c>Building.FarmingInitiative</c> already sizes a field against. Reading the
        /// same number here is what makes the abstract larder the same size as the one a watched settlement
        /// keeps on its map, rather than a second, separately-tuned target.
        /// </summary>
        public static float DaysOfFoodWanted => HuntingTuning.DaysOfFoodWanted;
    }
}
