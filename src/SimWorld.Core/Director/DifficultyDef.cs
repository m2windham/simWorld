using SimWorld.Defs;

namespace SimWorld.Director
{
    /// <summary>
    /// A difficulty preset (RimWorld: <c>RimWorld.DifficultyDef</c>). Carries the subset of RimWorld's real knob
    /// list this port currently reads: threat scale plus the handful of yield/mood/adaptation factors other
    /// systems can already act on. The remaining RimWorld knobs (allowCaveHives, scariaRotChance, raid-loot
    /// shaping, etc.) are intentionally omitted until the systems that would consume them exist.
    ///
    /// <para/><b>Every field here is read by something.</b> That was not true until the difficulty-wiring pass:
    /// six of these eleven were set by content and consulted by no line of <c>src/</c>, so choosing a
    /// difficulty moved threat points, disease rate and adaptation and nothing else. Each now has exactly one
    /// consumer, reached through <see cref="DifficultyUtility"/>:
    /// <list type="bullet">
    /// <item><description><see cref="threatScale"/> — <see cref="StorytellerUtility.DefaultThreatPointsNow"/>,
    /// which is upstream of both raid paths (a watched settlement's real squad and
    /// <see cref="SettlementRaidResolver"/>'s abstract one both spend the same points).</description></item>
    /// <item><description><see cref="allowBigThreats"/> — <see cref="IncidentWorker.CanFireNow"/>.</description></item>
    /// <item><description><see cref="allowIntroThreats"/> — <see cref="StorytellerComp_ClassicIntro"/>.</description></item>
    /// <item><description><see cref="colonistMoodOffset"/> — <c>Needs.Need_Mood.CurInstantLevel</c>.</description></item>
    /// <item><description><see cref="cropYieldFactor"/> — <c>Building.Plant.Harvest</c>.</description></item>
    /// <item><description><see cref="researchSpeedFactor"/> — <c>Research.ResearchManager.ResearchPerformed</c>.</description></item>
    /// <item><description><see cref="questRewardValueFactor"/> — <c>Quests.QuestNode_GiveReward</c>, where the
    /// reward's value is decided. Paying it out still waits on <c>IQuestRewardSink</c>; what a quest is
    /// <i>worth</i> does not.</description></item>
    /// <item><description><see cref="diseaseIntervalFactor"/> — <see cref="StorytellerComp_Disease"/>;
    /// <see cref="adaptationEffectFactor"/> and <see cref="adaptationGrowthRateFactorOverZero"/> —
    /// <see cref="StoryWatcher_Adaptation"/>.</description></item>
    /// </list>
    /// The exception is <see cref="mineYieldFactor"/>, still dormant and correctly so: there is no mining
    /// yield for it to scale (<c>Defs.ThingDef.mineableYield</c> is unread too — the mining job destroys the
    /// rock and spawns nothing), so it lands with the module that makes a mined rock drop something.
    /// Adding a knob here means finding it that consumer first. A difficulty field nothing reads is a promise
    /// the game does not keep, and it is invisible to the wiring audit until some content sets it.
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
