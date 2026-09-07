using System;

namespace SimWorld.Noise
{
    /// <summary>
    /// Ridged-multifractal noise: each octave is folded to <c>|signal|</c>, inverted about 1, squared, and
    /// weighted by the previous octave's output, producing sharp connected ridges — RimWorld/libnoise's
    /// mountain-range generator (<c>Verse.Noise.RidgedMultifractal</c>). Ported faithfully, including its
    /// closing <c>value * 1.25 - 1.0</c> rescale back into roughly [-1, 1].
    /// </summary>
    public sealed class RidgedMultifractal : ModuleBase
    {
        private const double H = 1.0;
        private const double Offset = 1.0;
        private const double Gain = 2.0;

        public double frequency = 1.0;
        public double lacunarity = 2.0;
        public int octaveCount = 6;
        public int seed;
        public NoiseQuality quality = NoiseQuality.Standard;

        private double[]? weights;
        private int weightsOctaveCount = -1;
        private double weightsLacunarity = double.NaN;

        public RidgedMultifractal()
        {
        }

        public RidgedMultifractal(double frequency, double lacunarity, int octaveCount, int seed, NoiseQuality quality)
        {
            this.frequency = frequency;
            this.lacunarity = lacunarity;
            this.octaveCount = octaveCount;
            this.seed = seed;
            this.quality = quality;
        }

        public override double GetValue(double x, double y, double z)
        {
            EnsureWeights();
            double[] w = weights!;

            x *= frequency;
            y *= frequency;
            z *= frequency;

            double value = 0.0;
            double weight = 1.0;

            for (int octave = 0; octave < octaveCount; octave++)
            {
                double signal = GradientNoise.Coherent3D(x, y, z, seed + octave, quality);
                signal = Math.Abs(signal);
                signal = Offset - signal;
                signal *= signal;
                signal *= weight;

                weight = GenMath.Clamp((float)(signal * Gain), 0f, 1f);
                value += signal * w[octave];

                x *= lacunarity;
                y *= lacunarity;
                z *= lacunarity;
            }
            return value * 1.25 - 1.0;
        }

        private void EnsureWeights()
        {
            if (weights != null && weightsOctaveCount == octaveCount && weightsLacunarity == lacunarity)
            {
                return;
            }
            var built = new double[octaveCount];
            double freq = 1.0;
            for (int i = 0; i < octaveCount; i++)
            {
                built[i] = Math.Pow(freq, -H);
                freq *= lacunarity;
            }
            weights = built;
            weightsOctaveCount = octaveCount;
            weightsLacunarity = lacunarity;
        }
    }
}
