using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Shared plant-growth math (RimWorld: <c>RimWorld.PlantUtility</c> / <c>Verse.GenTemperature</c>,
    /// trimmed to what <see cref="Plant"/>'s growth tick needs): the light and temperature factors of
    /// RimWorld's fertility × light × temperature growth-rate product. Fertility itself needs no helper —
    /// <see cref="Plant"/> reads <see cref="Map.TerrainDef.fertility"/> straight off the terrain grid.
    /// </summary>
    public static class PlantUtility
    {
        /// <summary>
        /// Longitude used for every plant's day/night light curve, until a world-tile-linked longitude
        /// exists on <see cref="Map.Map"/>. <b>Deviation (see this module's report):</b> no per-map longitude
        /// is wired yet, so every map reads the same day/night clock regardless of where its world tile
        /// actually sits — a placeholder, not a modelled value.
        /// </summary>
        public const float AssumedLongitudeDegrees = 0f;

        /// <summary>
        /// RimWorld's real sun-glow-by-hour curve shape, recalled from memory and <b>not verified against
        /// decompiled source in this sandbox</b> (see this module's report): no light through the night,
        /// ramping up over dawn, full light through the day, ramping down over dusk. Pinned by trend tests —
        /// 0 at midnight, 1 at noon, monotonic ramps either side — rather than by these literal hours.
        /// </summary>
        private static readonly SimpleCurve GlowByHour = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(6f, 0f),
            new CurvePoint(7f, 0.5f),
            new CurvePoint(8f, 1f),
            new CurvePoint(18f, 1f),
            new CurvePoint(19f, 0.5f),
            new CurvePoint(20f, 0f),
            new CurvePoint(24f, 0f),
        });

        /// <summary>0 (full dark) to 1 (full daylight) at the given absolute tick, from <see cref="GlowByHour"/>.</summary>
        public static float GrowthRateFactor_Light(long absTicks) =>
            GlowByHour.Evaluate(GenDate.HourFloat(absTicks, AssumedLongitudeDegrees));

        /// <summary>
        /// RimWorld's real temperature/growth-rate curve shape, recalled from memory and <b>not verified
        /// against decompiled source in this sandbox</b> (see this module's report): no growth at or below
        /// freezing or above <see cref="MaxGrowTemperatureC"/>, full rate across a temperate optimal band,
        /// linear ramps either side. Pinned by trend tests (monotonic ramps, zero outside the outer bounds,
        /// full rate inside the optimal band) rather than by these literal degrees.
        /// </summary>
        public const float MinGrowTemperatureC = 0f;
        public const float MinOptimalGrowTemperatureC = 10f;
        public const float MaxOptimalGrowTemperatureC = 42f;
        public const float MaxGrowTemperatureC = 58f;

        public static float GrowthRateFactor_Temperature(float ambientTempC)
        {
            if (ambientTempC <= MinGrowTemperatureC || ambientTempC >= MaxGrowTemperatureC) return 0f;
            if (ambientTempC < MinOptimalGrowTemperatureC)
            {
                return GenMath.InverseLerp(MinGrowTemperatureC, MinOptimalGrowTemperatureC, ambientTempC);
            }
            if (ambientTempC > MaxOptimalGrowTemperatureC)
            {
                return GenMath.InverseLerp(MaxGrowTemperatureC, MaxOptimalGrowTemperatureC, ambientTempC);
            }
            return 1f;
        }

        /// <summary>
        /// Same unsourced shape as <see cref="JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel"/>,
        /// reused here for the Plants skill (sowing and harvesting both scale by it) rather than duplicated
        /// per driver with different literals implying a precision neither has.
        /// </summary>
        public static readonly SimpleCurve WorkSpeedFactorFromPlantsLevel = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.4f),
            new CurvePoint(20, 2.0f),
        });
    }
}
