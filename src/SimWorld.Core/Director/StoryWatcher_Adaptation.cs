using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Tracks how long it's been since the civilization last faced a threat, and scales future threat points
    /// accordingly (RimWorld: <c>RimWorld.StoryWatcher_Adaptation</c>) — quiet stretches build up "slack" that
    /// makes the next threat land a little lighter, while very long quiet stretches ramp back toward (and past)
    /// normal so the story doesn't stall forever.
    /// </summary>
    public sealed class StoryWatcher_Adaptation : IExposable
    {
        /// <summary>
        /// Accumulated quiet days → threat-points multiplier. A documented approximation of RimWorld's
        /// adaptation-effect curve: freshly threatened colonies get a break (0.7×) and long-quiet ones ramp up
        /// past normal (1.2× past 60 days) so the story can't go silent indefinitely.
        /// </summary>
        public static readonly SimpleCurve AdaptDaysToThreatPointsFactorCurve = new SimpleCurve
        {
            { 0f, 0.7f },
            { 10f, 0.9f },
            { 30f, 1.0f },
            { 60f, 1.2f },
        };

        /// <summary>Adapt-days penalty when a colonist is downed (documented approximation).</summary>
        public const float DownedAdaptDaysPenalty = 0.5f;

        /// <summary>Adapt-days penalty when a colonist dies (documented approximation).</summary>
        public const float DeathAdaptDaysPenalty = 1.5f;

        private float adaptDays;

        public float AdaptDays => adaptDays;

        /// <summary>Current threat-points multiplier: the curve above, scaled by the difficulty's own adaptation-effect knob.</summary>
        public float TotalThreatPointsFactor(DifficultyDef? difficulty) =>
            AdaptDaysToThreatPointsFactorCurve.Evaluate(adaptDays) * (difficulty?.adaptationEffectFactor ?? 1f);

        /// <summary>Called once per storyteller interval; grows adaptDays by one day's worth per real day, scaled by difficulty.</summary>
        public void AdaptationTick(DifficultyDef? difficulty)
        {
            float growthRate = difficulty?.adaptationGrowthRateFactorOverZero ?? 1f;
            if (growthRate > 0f)
            {
                adaptDays += growthRate * Storyteller.IncidentCycleLengthTicks / (float)GenDate.TicksPerDay;
            }
        }

        public void Notify_ColonistDowned() => adaptDays = System.Math.Max(0f, adaptDays - DownedAdaptDaysPenalty);

        public void Notify_ColonistDied() => adaptDays = System.Math.Max(0f, adaptDays - DeathAdaptDaysPenalty);

        public void ExposeData()
        {
            Scribe_Values.Look(ref adaptDays, "adaptDays");
        }
    }
}
