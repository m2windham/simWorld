using System;
using System.Collections.Generic;
using SimWorld.Noise;
using SimWorld.Sim;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Elevation, hilliness, temperature, rainfall and swampiness for every tile (RimWorld:
    /// <c>Verse.WorldGenStep_Terrain</c>). RimWorld's exact elevation/noise constants are not published; this
    /// reproduces the same shape (continents + ridged mountains, latitude-driven climate) and calibrates the
    /// land/water split by quantile instead, so land fraction stays in a plausible band at any seed or grid size.
    /// </summary>
    public class WorldGenStep_Terrain : WorldGenStep
    {
        /// <summary>Share of tiles that end up at or below sea level; picked to land in RimWorld's usual ~30-35% range.</summary>
        public const float TargetLandFraction = 0.32f;

        /// <summary>RimWorld: temperature falls roughly 0.0033°C per metre of elevation gain; used verbatim.</summary>
        public const float ElevationTemperatureLapseRate = 0.0033f;

        public const float MinElevation = -1500f;
        public const float MaxElevation = 2200f;

        /// <summary>Peaks near 28°C at the equator, falls to -40°C at the poles (our own approximation of RimWorld's latitude curve; exact control points are not published).</summary>
        private static readonly SimpleCurve BaseTemperatureByLatitude = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 28f),
            new CurvePoint(20f, 26f),
            new CurvePoint(40f, 16f),
            new CurvePoint(60f, -4f),
            new CurvePoint(80f, -30f),
            new CurvePoint(90f, -40f),
        });

        /// <summary>Equatorial peak, a dry "horse latitudes" dip near 25°, a temperate rise, then a fall toward the poles.</summary>
        private static readonly SimpleCurve BaseRainfallByLatitude = new SimpleCurve(new[]
        {
            new CurvePoint(0f, 4000f),
            new CurvePoint(12f, 2000f),
            new CurvePoint(25f, 200f),
            new CurvePoint(40f, 1000f),
            new CurvePoint(60f, 600f),
            new CurvePoint(90f, 50f),
        });

        /// <summary>RimWorld's world-gen "overall temperature" setting as a flat °C offset (approximate; symmetric steps around Normal).</summary>
        private static readonly Dictionary<OverallTemperature, float> TemperatureOffsetC = new Dictionary<OverallTemperature, float>
        {
            [OverallTemperature.VeryCold] = -14f,
            [OverallTemperature.Cold] = -7f,
            [OverallTemperature.LittleBitColder] = -3.5f,
            [OverallTemperature.Normal] = 0f,
            [OverallTemperature.LittleBitWarmer] = 3.5f,
            [OverallTemperature.Hot] = 7f,
            [OverallTemperature.VeryHot] = 14f,
        };

        /// <summary>RimWorld's world-gen "overall rainfall" setting as a multiplier (approximate).</summary>
        private static readonly Dictionary<OverallRainfall, float> RainfallFactor = new Dictionary<OverallRainfall, float>
        {
            [OverallRainfall.AlmostNone] = 0.15f,
            [OverallRainfall.Little] = 0.5f,
            [OverallRainfall.LittleBitLess] = 0.75f,
            [OverallRainfall.Normal] = 1.0f,
            [OverallRainfall.LittleBitMore] = 1.25f,
            [OverallRainfall.High] = 1.5f,
            [OverallRainfall.VeryHigh] = 2.0f,
        };

        public override void GenerateFresh(string seed, World world)
        {
            RandomStream rand = SeededStream(seed);
            WorldGrid grid = world.grid;
            int n = grid.TilesCount;
            int baseSeed = rand.Int;

            var continents = new Perlin(frequency: 1.6, lacunarity: 2.0, persistence: 0.55, octaveCount: 6, seed: baseSeed, quality: NoiseQuality.Standard);
            var mountains = new RidgedMultifractal(frequency: 2.4, lacunarity: 2.1, octaveCount: 5, seed: baseSeed + 1000, quality: NoiseQuality.Standard);
            var hillsNoise = new Perlin(frequency: 5.0, lacunarity: 2.0, persistence: 0.5, octaveCount: 4, seed: baseSeed + 2000, quality: NoiseQuality.Standard);
            var rainNoise = new Perlin(frequency: 2.2, lacunarity: 2.0, persistence: 0.5, octaveCount: 4, seed: baseSeed + 3000, quality: NoiseQuality.Standard);
            var swampNoise = new Perlin(frequency: 6.0, lacunarity: 2.0, persistence: 0.5, octaveCount: 3, seed: baseSeed + 4000, quality: NoiseQuality.Standard);

            var raw = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = grid.GetTileCenter(i);
                raw[i] = continents.GetValue(p.x, p.y, p.z) * 0.65 + mountains.GetValue(p.x, p.y, p.z) * 0.35;
            }

            double threshold = QuantileThreshold(raw, TargetLandFraction);
            float[] elevations = ElevationsFromRaw(raw, threshold);

            float temperatureOffset = TemperatureOffsetC[world.info.overallTemperature];
            float rainfallFactor = RainfallFactor[world.info.overallRainfall];

            for (int i = 0; i < n; i++)
            {
                Tile tile = grid.Tiles[i];
                tile.elevation = elevations[i];

                (double lat, _) = grid.LongLatOf(i);
                float absLat = (float)Math.Abs(lat);
                float baseTemp = BaseTemperatureByLatitude.Evaluate(absLat);
                tile.temperature = baseTemp - tile.elevation * ElevationTemperatureLapseRate + temperatureOffset;

                if (tile.WaterCovered)
                {
                    tile.hilliness = Hilliness.Flat;
                    tile.rainfall = 0f;
                    tile.swampiness = 0f;
                    continue;
                }

                Vector3 p = grid.GetTileCenter(i);
                float normalizedElevation = GenMath.Clamp(tile.elevation / MaxElevation, 0f, 1f);
                float hillsSample = GenMath.Clamp((float)hillsNoise.GetValue(p.x, p.y, p.z) * 0.5f + 0.5f, 0f, 1f);
                float ruggedness = GenMath.Clamp(hillsSample * 0.5f + normalizedElevation * 0.5f, 0f, 1f);
                tile.hilliness = HillinessFromRuggedness(ruggedness);

                float baseRain = BaseRainfallByLatitude.Evaluate(absLat);
                float rainMultiplier = GenMath.Clamp(0.6f + (float)rainNoise.GetValue(p.x, p.y, p.z) * 0.8f, 0.1f, 2.2f);
                tile.rainfall = Math.Max(0f, baseRain * rainMultiplier * rainfallFactor);

                tile.swampiness = SwampinessFor(tile, swampNoise, p);
            }
        }

        /// <summary>The raw-noise value above which exactly <paramref name="landFraction"/> of tiles fall (land); everything else is water.</summary>
        private static double QuantileThreshold(double[] raw, float landFraction)
        {
            var sorted = (double[])raw.Clone();
            Array.Sort(sorted);
            int waterCount = (int)Math.Round((1.0 - landFraction) * sorted.Length);
            waterCount = Math.Max(0, Math.Min(sorted.Length - 1, waterCount));
            return sorted[waterCount];
        }

        private static float[] ElevationsFromRaw(double[] raw, double threshold)
        {
            double maxAbove = 1e-9;
            double minBelow = -1e-9;
            for (int i = 0; i < raw.Length; i++)
            {
                double d = raw[i] - threshold;
                if (d > maxAbove) maxAbove = d;
                if (d < minBelow) minBelow = d;
            }

            var elevations = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                double d = raw[i] - threshold;
                elevations[i] = d >= 0
                    ? (float)(d / maxAbove) * MaxElevation
                    : (float)(d / minBelow) * MinElevation;
            }
            return elevations;
        }

        private static Hilliness HillinessFromRuggedness(float r)
        {
            if (r < 0.2f) return Hilliness.Flat;
            if (r < 0.45f) return Hilliness.SmallHills;
            if (r < 0.7f) return Hilliness.LargeHills;
            if (r < 0.92f) return Hilliness.Mountainous;
            return Hilliness.Impassable;
        }

        /// <summary>Gated to low elevation and high rainfall, as RimWorld's real bogs/swamps are.</summary>
        private static float SwampinessFor(Tile tile, Perlin swampNoise, Vector3 p)
        {
            const float ElevationCeiling = 300f;
            const float RainfallFloor = 1500f;
            if (tile.elevation >= ElevationCeiling || tile.rainfall <= RainfallFloor)
            {
                return 0f;
            }
            float swampSample = GenMath.Clamp((float)swampNoise.GetValue(p.x, p.y, p.z) * 0.5f + 0.5f, 0f, 1f);
            float elevationFactor = 1f - GenMath.Clamp(tile.elevation / ElevationCeiling, 0f, 1f);
            float rainfallFactor = GenMath.Clamp((tile.rainfall - RainfallFloor) / RainfallFloor, 0f, 1f);
            return GenMath.Clamp(swampSample * elevationFactor * (0.5f + 0.5f * rainfallFactor), 0f, 1f);
        }
    }
}
