using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Keeps score of how the civilization is doing and scales future threat points accordingly (RimWorld:
    /// <c>RimWorld.StoryWatcher_Adaptation</c>). The score is <see cref="AdaptDays"/>, "days since the last
    /// injury" in RimWorld's own analogy, except that it does not reset: it rises while nothing happens,
    /// drops when somebody is hurt or lost, and can go negative. A settlement that has just lost people gets a
    /// break (the factor falls to 0.4); one that has gone quiet for a long time is pressed (1.47 at the cap).
    ///
    /// <para/><b>The mechanics are RimWorld's, from its decompile and the wiki's
    /// <c>https://rimworldwiki.com/wiki/Raid_points</c> ("Adaption factor"), which print the same numbers.</b>
    /// <list type="bullet">
    /// <item><description><b>Growth</b> is checked every 30,000 ticks (twelve hours): add
    /// <c>0.5 x GrowthRateCurve(adaptDays)</c>, which is a day's worth of the curve per day — 3 a day while
    /// negative, 1 a day from 0 to 60, 0.5 to 120. Above zero it is scaled by the difficulty's
    /// <see cref="DifficultyDef.adaptationGrowthRateFactorOverZero"/>. For the first 30 days of a game
    /// nothing grows a score that is already zero or more (a negative one climbs out regardless).</description></item>
    /// <item><description><b>A death</b> costs <see cref="LossFromColonistLostByPostPopulation"/> of the
    /// population left after it: 30 days of quiet from a small colony, 20 from twenty people. Every death of
    /// ours, whatever the cause — see <see cref="StorytellerDeathEvents"/>.</description></item>
    /// <item><description><b>A downing</b> costs <see cref="LossFromColonistViolentlyDownedByPopulation"/>:
    /// 8 days alone, 3.5 among twenty. Only when violence put them down, and held to the end of the tick so
    /// that someone downed and killed in one tick is charged for the death alone — see
    /// <see cref="StorytellerPawnEvents.Notify_PawnDowned"/>.</description></item>
    /// <item><description><b>The clamp</b> is -60 to 100 days. The factor curve runs on past 100 (to 2.0 at
    /// 180) but nothing reaches it; the cap's factor is 1.47.</description></item>
    /// <item><description>The difficulty's <see cref="DifficultyDef.adaptationEffectFactor"/> blends the
    /// factor toward 1 (<c>Lerp(1, factor, effect)</c>), as <c>DefaultThreatPointsNow</c> does in RimWorld.</description></item>
    /// </list>
    ///
    /// <para/><b>Translation, recorded.</b> RimWorld keeps these curves on <c>StorytellerDef</c> and sets the
    /// same ones for all three storytellers; the wiki prints one set. They are held here, once, as static
    /// curves. This replaces a version whose curve ran 0.7 to 1.2 over 0 to 60 days and whose penalties were
    /// 1.5 days for a death and 0.5 for a downing — a colony that lost a fifth of its people was forgiven for
    /// a day and a half.
    /// </summary>
    public sealed class StoryWatcher_Adaptation : IExposable
    {
        /// <summary>Ticks between growth checks (RimWorld: 30000, twelve hours).</summary>
        public const int UpdateInterval = 30000;

        /// <summary>Lowest the score goes (wiki: "Adapt Days have a minimum value of -60").</summary>
        public const float AdaptDaysMin = -60f;

        /// <summary>Highest the score goes (wiki: "and a maximum value of 100").</summary>
        public const float AdaptDaysMax = 100f;

        /// <summary>While the game is younger than this, a score at or above zero does not grow (wiki: "a
        /// grace period of 30 days before the AdaptDays can go above 0").</summary>
        public const float GameStartGraceDays = 30f;

        /// <summary>Score to threat-points multiplier (wiki, "Adaption factor": -30 → 0.4, 0 → 0.8, 30 → 1,
        /// 60 → 1.2, 120 → 1.6, 180 → 2). A new game starts at 0, so at 0.8.</summary>
        public static readonly SimpleCurve PointsFactorFromAdaptDays = new SimpleCurve
        {
            { -30f, 0.4f },
            { 0f, 0.8f },
            { 30f, 1f },
            { 60f, 1.2f },
            { 120f, 1.6f },
            { 180f, 2f },
        };

        /// <summary>Days a score climbs per day, by where it is now (wiki, "AdaptDays Added": 3 below zero, 1
        /// from 0 to 60, 0.5 from 60 to 120, 0.25 past it; the table prints the values the check halves).</summary>
        public static readonly SimpleCurve GrowthRateCurve = new SimpleCurve
        {
            { -30f, 3f },
            { -0.01f, 3f },
            { 0f, 1f },
            { 59.99f, 1f },
            { 60f, 0.5f },
            { 119.99f, 0.5f },
            { 120f, 0.25f },
        };

        /// <summary>Days lost to a death, by the population left (wiki: 0 → 30, 11 → 30, 20 → 20).</summary>
        public static readonly SimpleCurve LossFromColonistLostByPostPopulation = new SimpleCurve
        {
            { 0f, 30f },
            { 11f, 30f },
            { 20f, 20f },
        };

        /// <summary>Days lost to a violent downing, by the population (wiki: 1 → 8, 2 → 6, 3 → 6, 11 → 5, 20 → 3.5).</summary>
        public static readonly SimpleCurve LossFromColonistViolentlyDownedByPopulation = new SimpleCurve
        {
            { 1f, 8f },
            { 2f, 6f },
            { 3f, 6f },
            { 11f, 5f },
            { 20f, 3.5f },
        };

        private float adaptDays;

        /// <summary>Violent downings not yet charged. Held to the end of the tick, so a death in the same tick
        /// can cancel its pawn's entry (RimWorld: <c>pawnsJustDownedThisTick</c>). Not saved: it is empty
        /// between ticks.</summary>
        private readonly List<Pawn> pawnsJustDownedThisTick = new List<Pawn>();

        public float AdaptDays => adaptDays;

        /// <summary>RimWorld's <c>TotalThreatPointsFactor</c>: the curve at the current score, before the
        /// difficulty blends it.</summary>
        public float RawThreatPointsFactor => PointsFactorFromAdaptDays.Evaluate(adaptDays);

        /// <summary>The multiplier the threat points take: the curve, blended toward 1 by the difficulty's
        /// <see cref="DifficultyDef.adaptationEffectFactor"/> (<c>Lerp(1, factor, effect)</c>).</summary>
        public float TotalThreatPointsFactor(DifficultyDef? difficulty) =>
            GenMath.Lerp(1f, RawThreatPointsFactor, GenMath.Clamp01(difficulty?.adaptationEffectFactor ?? 1f));

        /// <summary>
        /// A citizen was put down by violence. Not charged yet: it waits for the end of the tick
        /// (<see cref="AdaptationWatcherTick"/>), where a death in the same tick takes it back. Raised by
        /// <see cref="StorytellerPawnEvents.Notify_PawnDowned"/>, which has already decided the pawn is ours and
        /// the downing was violent.
        /// </summary>
        public void Notify_ColonistDowned(Pawn pawn)
        {
            if (pawn == null) return;
            pawnsJustDownedThisTick.Add(pawn);
        }

        /// <summary>
        /// A citizen died, of anything (RimWorld: <c>AdaptationEvent.Died</c>, which asks nothing about the
        /// cause). Raised by <see cref="StorytellerDeathEvents.Notify_PawnDied"/> and nowhere else. Charged at
        /// once, by the population left after the death; it also withdraws a downing still waiting on this
        /// pawn, so a citizen who went down and died in the same tick pays once.
        /// </summary>
        public void Notify_ColonistDied(Pawn pawn)
        {
            pawnsJustDownedThisTick.Remove(pawn);
            // The pawn is already dead when this is raised (Pawn_HealthTracker.Kill sets the state first), so
            // the live roster is exactly the population "post" the loss.
            Lose(LossFromColonistLostByPostPopulation.Evaluate(StorytellerPawnEvents.CivilizationPopulation()));
        }

        /// <summary>
        /// Once per game tick (RimWorld: <c>AdaptationWatcherTick</c>, run by the story watcher every tick).
        /// Charges the downings the tick collected, then, every <see cref="UpdateInterval"/> ticks, grows the
        /// score.
        /// </summary>
        public void AdaptationWatcherTick(DifficultyDef? difficulty)
        {
            if (pawnsJustDownedThisTick.Count > 0)
            {
                float population = StorytellerPawnEvents.CivilizationPopulation();
                for (int i = 0; i < pawnsJustDownedThisTick.Count; i++)
                {
                    Lose(LossFromColonistViolentlyDownedByPopulation.Evaluate(population));
                }
                pawnsJustDownedThisTick.Clear();
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame % UpdateInterval != 0) return;

            if (adaptDays >= 0f && GenDate.DaysPassedAt(ticksGame) < GameStartGraceDays) return;

            float growth = 0.5f * GrowthRateCurve.Evaluate(adaptDays);
            if (adaptDays > 0f) growth *= difficulty?.adaptationGrowthRateFactorOverZero ?? 1f;
            adaptDays = System.Math.Min(adaptDays + growth, AdaptDaysMax);
        }

        /// <summary>Moves the score by hand (RimWorld: <c>Debug_OffsetAdaptDays</c>). For a test that wants a
        /// stretch of quiet — or of trouble — without running the clock for it. Unclamped, as RimWorld's is.</summary>
        public void Debug_OffsetAdaptDays(float days) => adaptDays += days;

        private void Lose(float days) => adaptDays = System.Math.Max(AdaptDaysMin, adaptDays - days);

        public void ExposeData()
        {
            Scribe_Values.Look(ref adaptDays, "adaptDays");
        }
    }
}
