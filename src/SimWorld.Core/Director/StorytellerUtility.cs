using System;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Threat-point math and default parms building (RimWorld: <c>RimWorld.StorytellerUtility</c>).</summary>
    public static class StorytellerUtility
    {
        public const float MinThreatPoints = 35f;
        public const float MaxThreatPoints = 10000f;

        /// <summary>Base points from civilization wealth (RimWorld's <c>pointsPerWealthCurve</c>).</summary>
        public static readonly SimpleCurve PointsPerWealthCurve = new SimpleCurve
        {
            { 0f, 0f },
            { 14000f, 0f },
            { 400000f, 2400f },
            { 700000f, 3600f },
            { 1000000f, 4200f },
        };

        /// <summary>Points added per pawn, itself scaled by wealth (RimWorld's <c>pointsPerColonistByWealthCurve</c>).</summary>
        public static readonly SimpleCurve PointsPerColonistByWealthCurve = new SimpleCurve
        {
            { 0f, 15f },
            { 10000f, 15f },
            { 400000f, 140f },
            { 1000000f, 200f },
        };

        /// <summary>
        /// RimWorld's threat-points formula: wealth curve, plus per-pawn points scaled by each pawn's health
        /// (dead pawns contribute nothing, hurt ones contribute proportionally less down to half), times
        /// difficulty's threat scale, times the civilization's current adaptation factor, times the
        /// storyteller's own days-passed curve — clamped to [<see cref="MinThreatPoints"/>, <see cref="MaxThreatPoints"/>].
        /// <para/>
        /// <b>Addition:</b> one more multiplier, the current era's <see cref="Research.EraDef.threatPointsFactor"/>,
        /// applied the same way difficulty and adaptation already are. That factor is SimWorld's own and
        /// stands in for the wealth term while nothing computes real wealth — see the field's own comment.
        /// </summary>
        public static float DefaultThreatPointsNow(IIncidentTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            float wealth = target.PlayerWealthForStoryteller;
            float points = PointsPerWealthCurve.Evaluate(wealth);
            float perColonist = PointsPerColonistByWealthCurve.Evaluate(wealth);
            foreach (Pawn pawn in target.PlayerPawnsForStoryteller)
            {
                float factor = pawn.Dead ? 0f : GenMath.Lerp(0.5f, 1f, pawn.health.summaryHealth.SummaryHealthPercent);
                points += perColonist * factor;
            }

            Storyteller storyteller = Find.Storyteller;
            if (storyteller.difficulty != null) points *= storyteller.difficulty.threatScale;
            points *= storyteller.adaptation.TotalThreatPointsFactor(storyteller.difficulty);
            points *= Research.EraTransitionUtility.CurrentEraThreatPointsFactor();
            if (storyteller.def != null) points *= storyteller.def.pointsFactorFromDaysPassed.Evaluate(GenDate.DaysPassedAt(Find.TickManager.TicksGame));

            return GenMath.Clamp(points, MinThreatPoints, MaxThreatPoints);
        }

        /// <summary>Builds the parms a comp hands to a def: threat categories get today's threat points, everything else gets none.</summary>
        public static IncidentParms DefaultParmsNow(IncidentCategoryDef category, IIncidentTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var parms = new IncidentParms { target = target };
            if (IsThreatCategory(category))
            {
                parms.points = DefaultThreatPointsNow(target);
            }
            return parms;
        }

        private static bool IsThreatCategory(IncidentCategoryDef category) =>
            category == IncidentCategoryDefOf.ThreatBig || category == IncidentCategoryDefOf.ThreatSmall;
    }

    /// <summary>
    /// Population-driven scaling of threat frequency (RimWorld: <c>RimWorld.StorytellerUtilityPopulation</c>).
    /// RimWorld smooths this over <c>StorytellerDef.populationIntentFactorFromPopAdaptDays</c> so a momentary
    /// population change doesn't snap the threat rate instantly; that smoothing isn't ported (documented
    /// deviation) — this evaluates the population curve directly against the current head count.
    /// </summary>
    public static class StorytellerUtilityPopulation
    {
        public static float PopulationIntentFactor(StorytellerDef? def, int currentPopulation)
        {
            if (def?.populationIntentFactorFromPopCurve == null) return 1f;
            return def.populationIntentFactorFromPopCurve.Evaluate(currentPopulation);
        }
    }

    /// <summary>
    /// On/off cycle math shared by cycle-shaped storyteller comps (RimWorld: <c>RimWorld.IncidentCycleUtility</c>).
    /// </summary>
    public static class IncidentCycleUtility
    {
        /// <summary>
        /// RimWorld's <c>Rand.MTBEventOccurs</c> is a half-life check: repeatedly probed at a mean time T, the
        /// expected number of hits over a duration D is <c>D * Ln2 / T</c>, not <c>D / T</c>. This converts a
        /// target average count back into the T to feed it, so tuning numbers (numIncidentsRange) mean what
        /// they say.
        /// </summary>
        private const float Ln2 = 0.6931472f;

        /// <summary>
        /// How many incidents a cycle-shaped comp should fire this interval: 0 outside the on-phase or before
        /// <paramref name="minDaysPassed"/>; otherwise a mean-time-between roll tuned so that, averaged over many
        /// on-phases, roughly <paramref name="numIncidentsMin"/>..<paramref name="numIncidentsMax"/> incidents
        /// happen per on-phase (spaced at least <paramref name="minSpacingDays"/> apart), scaled down early on by
        /// <paramref name="acceptFractionByDaysPassedCurve"/> when given.
        /// </summary>
        public static int IncidentCountThisInterval(
            IIncidentTarget target, RandomStream random, float minDaysPassed,
            float onDays, float offDays, float minSpacingDays,
            float numIncidentsMin, float numIncidentsMax,
            SimpleCurve? acceptFractionByDaysPassedCurve)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (random == null) throw new ArgumentNullException(nameof(random));

            float daysPassedFloat = Find.TickManager.TicksGame / (float)GenDate.TicksPerDay;
            if (daysPassedFloat < minDaysPassed) return 0;

            float cycleLength = onDays + offDays;
            if (cycleLength <= 0f || onDays <= 0f) return 0;

            float posInCycle = daysPassedFloat % cycleLength;
            if (posInCycle >= onDays) return 0; // off phase

            float avgCount = (numIncidentsMin + numIncidentsMax) / 2f;
            if (avgCount <= 0f) return 0;

            float acceptFraction = acceptFractionByDaysPassedCurve != null
                ? GenMath.Clamp01(acceptFractionByDaysPassedCurve.Evaluate(daysPassedFloat))
                : 1f;
            if (acceptFraction <= 0f) return 0;

            float mtbDays = Math.Max(onDays / avgCount * Ln2, Math.Max(minSpacingDays, 0.01f)) / acceptFraction;
            return random.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, Storyteller.IncidentCycleLengthTicks) ? 1 : 0;
        }
    }
}
