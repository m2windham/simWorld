using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// Tuning for <see cref="SettlementLarder"/>. RimWorld has nothing to port here — every RimWorld pawn is
    /// standing on a map, so RimWorld never needed a rule for feeding somebody who is not — so every figure
    /// below is this port's own and every one is <b>derived from a number this codebase already committed
    /// to</b> rather than chosen. <c>SettlementLarderTests</c> pins each as an ordering or a band, never as
    /// the literal, exactly as CLAUDE.md requires for an unsourced number.
    /// </summary>
    public static class SettlementLarderTuning
    {
        /// <summary>
        /// Self-gate cadence for the abstract consumption pass.
        ///
        /// <para/><b>Derived, not chosen: it is the cadence hunger itself moves on off a map.</b> An
        /// off-map citizen at <see cref="Pawns.PawnTier.Interval"/> advances only on the Long bucket
        /// (<c>Pawn.TickLong</c> → <c>Pawn_TierTracker.CoarseTick</c> → <c>Need.NeedIntervalBulk</c>, spec
        /// §11.3), so their hunger changes exactly <see cref="GenTicks.TickLongInterval"/> ticks at a time
        /// and looking more often than that can only ever find the same answer twice. It is also the cadence
        /// <c>Sim.CitizenTickRegistry</c> already walks the same rosters on, so a settlement pays for one
        /// more walk of a list it is already walking rather than for a new cadence.
        ///
        /// <para/>For the other half of the population this serves — a <see cref="Pawns.PawnTier.Full"/>
        /// citizen of a focused settlement nobody has opened, whose needs fall every tick — one interval is
        /// 1/30th of a day, which at <see cref="HuntingTuning.NutritionPerEaterPerDay"/> is 0.053 nutrition
        /// against a stomach of 1.0. That is the worst lag this cadence can impose between getting hungry
        /// and eating, and it is a twentieth of the distance between two hunger categories.
        /// </summary>
        public const int IntervalTicks = GenTicks.TickLongInterval;

        /// <summary>
        /// How much food a founding band walks in carrying, per mouth: enough to eat until the first field
        /// it sows can be harvested, and to still be holding the larder a settlement wants in hand on the
        /// day it is.
        ///
        /// <para/><b>Every term is read off something the codebase already decided.</b>
        /// <list type="bullet">
        /// <item><see cref="DaysToFirstHarvest"/> — the shipped crops' own <c>growDays</c>, which is the
        /// first date on which a settlement that does everything right can eat something it grew.</item>
        /// <item><see cref="HuntingTuning.DaysOfFoodWanted"/> — the food economy's one statement of "how
        /// much a settlement wants in hand", so a band arrives at its first harvest stocked rather than
        /// empty on exactly the day the crop ripens.</item>
        /// <item><see cref="HuntingTuning.NutritionPerEaterPerDay"/> — the food economy's one demand
        /// figure, itself read off <see cref="Needs.Need_Food.BaseFoodFallPerTick"/>. There is no second
        /// per-day number here that could drift from it.</item>
        /// </list>
        /// </summary>
        public static float ProvisionNutritionPerMouth =>
            (DaysToFirstHarvest + HuntingTuning.DaysOfFoodWanted) * HuntingTuning.NutritionPerEaterPerDay;

        /// <summary>
        /// Days from sowing to the first harvest, taken as the <b>slowest</b> food-bearing crop the content
        /// ships rather than the fastest — a band does not know which crop the ground it settles will take
        /// (<c>Building.FarmingInitiative.CropFor</c> decides that from the map's own fertility, which does
        /// not exist yet at founding), so the provision that covers every answer is the longest one.
        ///
        /// <para/>Falls back to <see cref="HuntingTuning.DaysOfFoodWanted"/> when content ships no crop at
        /// all: with nothing to grow there is no first harvest to walk toward, and the only honest figure
        /// left is the larder a settlement wants in hand.
        /// </summary>
        public static float DaysToFirstHarvest
        {
            get
            {
                float longest = 0f;
                IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef def = all[i];
                    if (def.plant == null || def.plant.growDays <= 0f) continue;
                    // "Bears food when grown", asked of the content in the one place that already answers it.
                    if (FarmingInitiative.NutritionPerCellPerDay(def) <= 0f) continue;
                    if (def.plant.growDays > longest) longest = def.plant.growDays;
                }
                return longest > 0f ? longest : HuntingTuning.DaysOfFoodWanted;
            }
        }
    }
}
