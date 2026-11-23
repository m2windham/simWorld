using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Tracks how long it's been since the civilization last faced a threat, and scales future threat points
    /// accordingly (RimWorld: <c>RimWorld.StoryWatcher_Adaptation</c>) — quiet stretches build up "slack" that
    /// makes the next threat land a little lighter, while very long quiet stretches ramp back toward (and past)
    /// normal so the story doesn't stall forever.
    /// <para/>
    /// <b>The problem it solves is the death spiral.</b> A raid that costs a settlement people leaves it
    /// weaker, and without this the next raid is sized as if nothing had happened. Every casualty spends
    /// adapt-days, the threat-points factor falls, and the next threat lands lighter: the hurt settlement gets
    /// a window to recover in. It is the same at every era, so it is ported 1:1.
    /// <para/>
    /// <b>Sourced numbers, and why they replaced the old ones.</b> All five shapes below are RimWorld's, read
    /// from rimworldwiki.com/wiki/Raid_points (2026-11-22, "Adaptation"): adapt-days are clamped to
    /// [<see cref="AdaptDaysMin"/>, <see cref="AdaptDaysMax"/>], a death costs 20–30 adapt-days and a downing
    /// 3.5–8 by population, growth slows past 60 and speeds up below 0. The call shape is the decompile's
    /// (josh-m/RW-Decompile <c>StoryWatcher_Adaptation</c>): growth is <c>adaptDaysGrowthRateCurve</c> at the
    /// current adapt-days, the max applies on growth and the min on losses, a death is charged against the
    /// population left after it. This port had none of the clamps and losses of 1.5 and 0.5 days, with
    /// adapt-days free to grow without bound: on a populated 20/99 arc the factor sat at its 1.2 ceiling from
    /// day 60 to extinction on day ~410, and the sixteen deaths of the day-385 raid moved the next raid's
    /// points by about 2%. The threat never answered the losses, which is the one thing this watcher is for.
    /// <para/>
    /// <b>Not ported:</b> RimWorld's 30-day start grace (adapt-days held at or below 0 until day 30) — the
    /// storyteller's own <c>pointsFactorFromDaysPassed</c> already eases the first days here — and the rule
    /// that only a violent downing is charged (see <see cref="StorytellerPawnEvents.Notify_PawnDowned"/>).
    /// </summary>
    public sealed class StoryWatcher_Adaptation : IExposable
    {
        /// <summary>RimWorld's adapt-days floor (wiki: Raid_points). Losses never take adapt-days below it.</summary>
        public const float AdaptDaysMin = -60f;

        /// <summary>RimWorld's adapt-days ceiling (wiki: Raid_points; also <c>StorytellerDef.adaptDaysMax</c>'s
        /// code default). Growth never takes adapt-days above it.</summary>
        public const float AdaptDaysMax = 100f;

        /// <summary>
        /// Adapt-days → threat-points multiplier (RimWorld: <c>StorytellerDef.pointsFactorFromAdaptDays</c>, the
        /// wiki's "AdaptDays to Adaption Factor" chart). 0.8 for a fresh start, 0.4 after heavy losses, 1.47 at
        /// <see cref="AdaptDaysMax"/>; the last point lies past the reachable range, as in RimWorld.
        /// </summary>
        public static readonly SimpleCurve AdaptDaysToThreatPointsFactorCurve = new SimpleCurve
        {
            { -30f, 0.4f },
            { 0f, 0.8f },
            { 30f, 1.0f },
            { 60f, 1.2f },
            { 120f, 1.6f },
            { 180f, 2.0f },
        };

        /// <summary>
        /// Adapt-days gained per day at the current adapt-days (RimWorld: <c>adaptDaysGrowthRateCurve</c>,
        /// evaluated at the current value and halved per twelve-hour check, so per day it is the curve itself).
        /// A settlement in the hole climbs back three times as fast as one at peace grows past 60.
        /// </summary>
        public static readonly SimpleCurve AdaptDaysGrowthRateCurve = new SimpleCurve
        {
            { -30f, 3f },
            { -0.01f, 3f },
            { 0f, 1f },
            { 59.99f, 1f },
            { 60f, 0.5f },
            { 119.99f, 0.5f },
            { 120f, 0.25f },
        };

        /// <summary>Adapt-days a death costs, by the population left after it (RimWorld:
        /// <c>adaptDaysLossFromColonistLostByPostPopulation</c>). A small band feels a loss more.</summary>
        public static readonly SimpleCurve DeathAdaptDaysLossByPostPopulation = new SimpleCurve
        {
            { 0f, 30f },
            { 11f, 30f },
            { 20f, 20f },
        };

        /// <summary>Adapt-days a downing costs, by population (RimWorld:
        /// <c>adaptDaysLossFromColonistViolentlyDownedByPopulation</c>).</summary>
        public static readonly SimpleCurve DownedAdaptDaysLossByPopulation = new SimpleCurve
        {
            { 1f, 8f },
            { 2f, 6f },
            { 3f, 6f },
            { 11f, 5f },
            { 20f, 3.5f },
        };

        private float adaptDays;

        public float AdaptDays => adaptDays;

        /// <summary>What one death costs when <paramref name="postPopulation"/> are left alive.</summary>
        public static float DeathAdaptDaysLoss(int postPopulation) => DeathAdaptDaysLossByPostPopulation.Evaluate(postPopulation);

        /// <summary>What one downing costs in a civilization of <paramref name="population"/>.</summary>
        public static float DownedAdaptDaysLoss(int population) => DownedAdaptDaysLossByPopulation.Evaluate(population);

        /// <summary>
        /// Current threat-points multiplier: the curve above, pulled toward 1 by the difficulty's
        /// adaptation-impact knob (RimWorld's "Adaptation impact"; the wiki's worked example — 40% impact gives an
        /// effective range of 0.75 to 1.19 — is <c>Lerp(1, curve, impact)</c>, not a product).
        /// </summary>
        public float TotalThreatPointsFactor(DifficultyDef? difficulty) =>
            GenMath.Lerp(1f, AdaptDaysToThreatPointsFactorCurve.Evaluate(adaptDays), difficulty?.adaptationEffectFactor ?? 1f);

        /// <summary>Called once per storyteller interval: grows adapt-days at <see cref="AdaptDaysGrowthRateCurve"/>'s
        /// rate, the difficulty's growth factor applying only above zero (as in RimWorld), never past
        /// <see cref="AdaptDaysMax"/>.</summary>
        public void AdaptationTick(DifficultyDef? difficulty)
        {
            float rate = AdaptDaysGrowthRateCurve.Evaluate(adaptDays);
            if (adaptDays > 0f) rate *= difficulty?.adaptationGrowthRateFactorOverZero ?? 1f;
            if (rate <= 0f) return;
            adaptDays = System.Math.Min(AdaptDaysMax, adaptDays + rate * Storyteller.IncidentCycleLengthTicks / (float)GenDate.TicksPerDay);
        }

        /// <summary>A citizen went down in a civilization of <paramref name="population"/>. Raised by
        /// <see cref="StorytellerPawnEvents.Notify_PawnDowned"/>.</summary>
        public void Notify_ColonistDowned(int population) => Spend(DownedAdaptDaysLoss(population));

        /// <summary>
        /// A citizen died, of anything (RimWorld: <c>AdaptationEvent.Died</c>, which asks nothing about the
        /// cause), leaving <paramref name="postPopulation"/> alive. Raised by
        /// <see cref="StorytellerDeathEvents.Notify_PawnDied"/> and nowhere else.
        /// </summary>
        public void Notify_ColonistDied(int postPopulation) => Spend(DeathAdaptDaysLoss(postPopulation));

        private void Spend(float days) => adaptDays = System.Math.Max(AdaptDaysMin, adaptDays - days);

        public void ExposeData()
        {
            Scribe_Values.Look(ref adaptDays, "adaptDays");
        }
    }
}
