using SimWorld.Defs;

namespace SimWorld.Director
{
    /// <summary>
    /// A difficulty preset (RimWorld: <c>RimWorld.DifficultyDef</c>). Carries the subset of RimWorld's real knob
    /// list this port currently reads: threat scale plus the handful of yield/mood/adaptation factors other
    /// systems can already act on. The remaining RimWorld knobs (allowCaveHives, scariaRotChance, raid-loot and
    /// quest-reward shaping, etc.) are intentionally omitted until the systems that would consume them exist.
    /// </summary>
    public class DifficultyDef : Def
    {
        public float threatScale = 1f;
        public bool allowBigThreats = true;
        public bool allowIntroThreats = true;
        public float colonistMoodOffset;
        public float cropYieldFactor = 1f;
        public float mineYieldFactor = 1f;
        public float researchSpeedFactor = 1f;
        public float diseaseIntervalFactor = 1f;
        public float adaptationEffectFactor = 1f;
        public float adaptationGrowthRateFactorOverZero = 1f;
        public float questRewardValueFactor = 1f;
    }
}
