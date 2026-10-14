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
        /// storyteller's starting factor (<see cref="StorytellerDef.pointsFactorFromDaysPassed"/>, a
        /// discount that fades out by day 40) — clamped to [<see cref="MinThreatPoints"/>, <see cref="MaxThreatPoints"/>].
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
}
