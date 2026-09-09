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

        // ---- system: work.stats (skill-driven stats — see Stats/SkillNeed.cs) ----

        /// <summary>General work speed multiplier (RimWorld: <c>StatDefOf.WorkSpeedGlobal</c>). Not read by
        /// any job driver yet in this port; ships with a (deliberately unsourced, see content) skill-need tie
        /// as the demonstration case the work.stats brief names explicitly.</summary>
        public static StatDef WorkSpeedGlobal = null!;

        /// <summary>How good a tend is, skill-need-scaled off Medicine (RimWorld: <c>StatDefOf.MedicalTendQuality</c>).
        /// Health's own <see cref="Health.SurgeryTuning"/> still reads the Medicine skill directly for
        /// surgery's own success chance rather than through this stat — see that module's own doc comment and
        /// <c>docs/spec/simworld-spec.md</c> §7.2.</summary>
        public static StatDef MedicalTendQuality = null!;

        /// <summary>How fast this pawn breaks rock while mining, skill-need-scaled off Mining (RimWorld:
        /// <c>StatDefOf.MiningSpeed</c>). Not read by <see cref="AI.JobGiver_Work"/>'s job drivers yet — see
        /// the content file's own remarks.</summary>
        public static StatDef MiningSpeed = null!;

        /// <summary>How fast this pawn builds things, skill-need-scaled off Construction (RimWorld:
        /// <c>StatDefOf.ConstructionSpeed</c>).</summary>
        public static StatDef ConstructionSpeed = null!;

        /// <summary>How fast this pawn cooks meals, skill-need-scaled off Cooking (RimWorld: <c>StatDefOf.CookSpeed</c>).</summary>
        public static StatDef CookSpeed = null!;

        /// <summary>How fast this pawn generates research points, skill-need-scaled off Intellectual (RimWorld:
        /// <c>StatDefOf.ResearchSpeed</c>).</summary>
        public static StatDef ResearchSpeed = null!;
    }
}
