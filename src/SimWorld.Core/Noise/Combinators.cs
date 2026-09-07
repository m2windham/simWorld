using System;

namespace SimWorld.Noise
{
    /// <summary>Constant field (RimWorld/libnoise: <c>Verse.Noise.Const</c>).</summary>
    public sealed class Const : ModuleBase
    {
        public double value;

        public Const()
        {
        }

        public Const(double value)
        {
            this.value = value;
        }

        public override double GetValue(double x, double y, double z) => value;
    }

    /// <summary><c>source0 + source1</c> (RimWorld/libnoise: <c>Verse.Noise.Add</c>).</summary>
    public sealed class Add : ModuleBase
    {
        public ModuleBase source0 = null!;
        public ModuleBase source1 = null!;

        public Add()
        {
        }

        public Add(ModuleBase source0, ModuleBase source1)
        {
            this.source0 = source0;
            this.source1 = source1;
        }

        public override double GetValue(double x, double y, double z) => source0.GetValue(x, y, z) + source1.GetValue(x, y, z);
    }

    /// <summary><c>source0 × source1</c> (RimWorld/libnoise: <c>Verse.Noise.Multiply</c>).</summary>
    public sealed class Multiply : ModuleBase
    {
        public ModuleBase source0 = null!;
        public ModuleBase source1 = null!;

        public Multiply()
        {
        }

        public Multiply(ModuleBase source0, ModuleBase source1)
        {
            this.source0 = source0;
            this.source1 = source1;
        }

        public override double GetValue(double x, double y, double z) => source0.GetValue(x, y, z) * source1.GetValue(x, y, z);
    }

    /// <summary><c>source0 × scale + bias</c> (RimWorld/libnoise: <c>Verse.Noise.ScaleBias</c>).</summary>
    public sealed class ScaleBias : ModuleBase
    {
        public ModuleBase source0 = null!;
        public double scale = 1.0;
        public double bias;

        public ScaleBias()
        {
        }

        public ScaleBias(ModuleBase source0, double scale, double bias)
        {
            this.source0 = source0;
            this.scale = scale;
            this.bias = bias;
        }

        public override double GetValue(double x, double y, double z) => source0.GetValue(x, y, z) * scale + bias;
    }

    /// <summary>Clamps <c>source0</c> into [<see cref="lowerBound"/>, <see cref="upperBound"/>] (RimWorld/libnoise: <c>Verse.Noise.Clamp</c>).</summary>
    public sealed class Clamp : ModuleBase
    {
        public ModuleBase source0 = null!;
        public double lowerBound = -1.0;
        public double upperBound = 1.0;

        public Clamp()
        {
        }

        public Clamp(ModuleBase source0, double lowerBound, double upperBound)
        {
            this.source0 = source0;
            this.lowerBound = lowerBound;
            this.upperBound = upperBound;
        }

        public override double GetValue(double x, double y, double z)
        {
            double v = source0.GetValue(x, y, z);
            return v < lowerBound ? lowerBound : (v > upperBound ? upperBound : v);
        }
    }

    /// <summary><c>|source0|</c> (RimWorld/libnoise: <c>Verse.Noise.Abs</c>).</summary>
    public sealed class Abs : ModuleBase
    {
        public ModuleBase source0 = null!;

        public Abs()
        {
        }

        public Abs(ModuleBase source0)
        {
            this.source0 = source0;
        }

        public override double GetValue(double x, double y, double z) => Math.Abs(source0.GetValue(x, y, z));
    }

    /// <summary><c>-source0</c> (RimWorld/libnoise: <c>Verse.Noise.Invert</c>).</summary>
    public sealed class Invert : ModuleBase
    {
        public ModuleBase source0 = null!;

        public Invert()
        {
        }

        public Invert(ModuleBase source0)
        {
            this.source0 = source0;
        }

        public override double GetValue(double x, double y, double z) => -source0.GetValue(x, y, z);
    }

    /// <summary>
    /// Perturbs the sample point with three independently-seeded <see cref="Perlin"/> fields before reading
    /// <see cref="source0"/>, so straight features (coastlines, bands) come out organic
    /// (RimWorld/libnoise: <c>Verse.Noise.Turbulence</c>).
    /// </summary>
    public sealed class Turbulence : ModuleBase
    {
        public ModuleBase source0 = null!;
        public double frequency = 1.0;
        public double power = 1.0;
        public int roughness = 3;
        public int seed;

        private Perlin? xDistort;
        private Perlin? yDistort;
        private Perlin? zDistort;
        private double builtFrequency = double.NaN;
        private int builtRoughness = -1;
        private int builtSeed;

        public Turbulence()
        {
        }

        public override double GetValue(double x, double y, double z)
        {
            EnsureDistortModules();
            double dx = x + xDistort!.GetValue(x, y, z) * power;
            double dy = y + yDistort!.GetValue(x, y, z) * power;
            double dz = z + zDistort!.GetValue(x, y, z) * power;
            return source0.GetValue(dx, dy, dz);
        }

        private void EnsureDistortModules()
        {
            if (xDistort != null && builtFrequency == frequency && builtRoughness == roughness && builtSeed == seed)
            {
                return;
            }
            xDistort = new Perlin(frequency, 2.0, 0.5, roughness, seed, NoiseQuality.Standard);
            yDistort = new Perlin(frequency, 2.0, 0.5, roughness, seed + 1, NoiseQuality.Standard);
            zDistort = new Perlin(frequency, 2.0, 0.5, roughness, seed + 2, NoiseQuality.Standard);
            builtFrequency = frequency;
            builtRoughness = roughness;
            builtSeed = seed;
        }
    }
}
