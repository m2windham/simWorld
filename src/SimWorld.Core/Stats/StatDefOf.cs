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

        /// <summary>Cells per second a pawn walks at (RimWorld: <c>Pawn_PathFollower</c>'s speed stat).</summary>
        public static StatDef MoveSpeed = null!;

        /// <summary>Total work (in <see cref="Building.Frame"/> work-units) a Def's construction requires
        /// (RimWorld: <c>StatDefOf.WorkToBuild</c>). A plain <c>statBases</c> lookup like <c>MaxHitPoints</c> —
        /// RimWorld's real stat also scales with a Def's <c>ConstructionSkillPrerequisite</c> and a stuff
        /// factor, neither of which this pass's content needs (system 16: Building).</summary>
        public static StatDef WorkToBuild = null!;

        /// <summary>How strongly a wall/door resists a room's temperature equalising with the outdoors
        /// (system 16: Building; RimWorld has no single named equivalent — its real insulation math lives in
        /// <c>RoomTemperature</c> reading a wall's <c>StatCategoryDef</c> data directly rather than a stat this
        /// port's pipeline can address the same way, but the shape — a per-Def resistance value — carries
        /// over cleanly as its own stat).</summary>
        public static StatDef Insulation = null!;
    }
}
