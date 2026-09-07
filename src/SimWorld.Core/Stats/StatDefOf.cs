using SimWorld.Defs;

namespace SimWorld.Stats
{
    /// <summary>
    /// The one <c>StatDefOf</c> for the whole codebase (RimWorld: <c>RimWorld.StatDefOf</c>). Before this
    /// module, <c>MarketValue</c> was bound twice — once in <c>SimWorld.Crafting.StatDefOf</c>, once in
    /// <c>SimWorld.Economy.EconomyDefOf</c> — naming the same StatDef from two places. Both are retired here.
    /// </summary>
    [DefOf]
    public static class StatDefOf
    {
        /// <summary>Read by <see cref="Crafting.ThingDef.BaseMarketValue"/> and <see cref="Economy.TradeUtility.BaseMarketValue"/>.</summary>
        public static StatDef MarketValue = null!;

        /// <summary>Rest gain multiplier; capacity-factored by BloodPumping/Metabolism/Breathing (RimWorld: <c>Need_Rest</c>).</summary>
        public static StatDef RestRateMultiplier = null!;

        /// <summary>Immunity gain speed while sick (RimWorld: <c>ImmunityRecord.ImmunityChangePerTick</c>).</summary>
        public static StatDef ImmunityGainSpeed = null!;

        /// <summary>Pain level that downs the pawn (RimWorld: <c>Pawn_HealthTracker.InPainShock</c>).</summary>
        public static StatDef PainShockThreshold = null!;

        /// <summary>Scales indirect (learning-by-doing) skill XP (RimWorld: <c>Pawn_SkillTracker.Learn</c>).</summary>
        public static StatDef GlobalLearningFactor = null!;

        /// <summary>Mood level under which minor mental breaks become possible (RimWorld: <c>MentalBreaker</c>).</summary>
        public static StatDef MentalBreakThreshold = null!;
    }
}
