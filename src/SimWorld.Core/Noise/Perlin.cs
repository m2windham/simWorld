namespace SimWorld.Noise
{
    /// <summary>
    /// Fractal-summed coherent noise: <paramref name="octaveCount"/> layers of <see cref="GradientNoise"/>,
    /// each at <c>frequency × lacunarity^n</c> and weighted by <c>persistence^n</c>
    /// (RimWorld/libnoise: <c>Verse.Noise.Perlin</c>). Each octave gets its own lattice via <c>seed + octave</c>,
    /// so octaves never repeat the same pattern at a different scale.
    /// </summary>
    public sealed class Perlin : ModuleBase
    {
        public double frequency = 1.0;
        public double lacunarity = 2.0;
        public double persistence = 0.5;
        public int octaveCount = 6;
        public int seed;
        public NoiseQuality quality = NoiseQuality.Standard;

        public Perlin()
        {
        }

        public Perlin(double frequency, double lacunarity, double persistence, int octaveCount, int seed, NoiseQuality quality)
        {
            this.frequency = frequency;
            this.lacunarity = lacunarity;
            this.persistence = persistence;
            this.octaveCount = octaveCount;
            this.seed = seed;
            this.quality = quality;
        }

        public override double GetValue(double x, double y, double z)
        {
            double value = 0.0;
            double curPersistence = 1.0;
            x *= frequency;
            y *= frequency;
            z *= frequency;

            for (int octave = 0; octave < octaveCount; octave++)
            {
                double signal = GradientNoise.Coherent3D(x, y, z, seed + octave, quality);
                value += signal * curPersistence;

                x *= lacunarity;
                y *= lacunarity;
                z *= lacunarity;
                curPersistence *= persistence;
            }
            return value;
        }
    }
}
