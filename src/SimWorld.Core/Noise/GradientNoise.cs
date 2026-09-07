using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Noise
{
    /// <summary>Interpolation curve used between lattice points (RimWorld/libnoise: <c>Verse.Noise.NoiseQuality</c>).</summary>
    public enum NoiseQuality
    {
        /// <summary>Linear. Cheap, visibly faceted.</summary>
        Low,
        /// <summary>Smoothstep (3t² − 2t³). The usual choice.</summary>
        Standard,
        /// <summary>Quintic (6t⁵ − 15t⁴ + 10t³); zero 1st and 2nd derivative at the endpoints.</summary>
        High,
    }

    /// <summary>
    /// Coherent 3D gradient noise built from scratch (Ken Perlin's 2002 "improved noise"): a permutation
    /// table derived deterministically from an integer seed, classic lattice gradients, and a
    /// <see cref="NoiseQuality"/>-selected fade curve. Every <see cref="Perlin"/>/<see cref="RidgedMultifractal"/>
    /// octave samples this. Not LibNoise's exact gradient table — the port only needs deterministic,
    /// seed-varying coherent noise in roughly [-1, 1], which this provides.
    /// </summary>
    public static class GradientNoise
    {
        private const int TableSize = 256;
        private static readonly Dictionary<int, byte[]> PermutationCache = new Dictionary<int, byte[]>();
        private static readonly object Gate = new object();

        public static double Coherent3D(double x, double y, double z, int seed, NoiseQuality quality)
        {
            byte[] perm = GetPermutation(seed);

            int xi = FastFloor(x);
            int yi = FastFloor(y);
            int zi = FastFloor(z);
            int X = xi & 255;
            int Y = yi & 255;
            int Z = zi & 255;

            double xf = x - xi;
            double yf = y - yi;
            double zf = z - zi;

            double u = Fade(xf, quality);
            double v = Fade(yf, quality);
            double w = Fade(zf, quality);

            int a = perm[X] + Y;
            int aa = perm[a & 511] + Z;
            int ab = perm[(a + 1) & 511] + Z;
            int b = perm[(X + 1) & 511] + Y;
            int ba = perm[b & 511] + Z;
            int bb = perm[(b + 1) & 511] + Z;

            double x1 = Lerp(u, Grad(perm[aa & 511], xf, yf, zf), Grad(perm[ba & 511], xf - 1, yf, zf));
            double x2 = Lerp(u, Grad(perm[ab & 511], xf, yf - 1, zf), Grad(perm[bb & 511], xf - 1, yf - 1, zf));
            double y1 = Lerp(v, x1, x2);

            double x3 = Lerp(u, Grad(perm[(aa + 1) & 511], xf, yf, zf - 1), Grad(perm[(ba + 1) & 511], xf - 1, yf, zf - 1));
            double x4 = Lerp(u, Grad(perm[(ab + 1) & 511], xf, yf - 1, zf - 1), Grad(perm[(bb + 1) & 511], xf - 1, yf - 1, zf - 1));
            double y2 = Lerp(v, x3, x4);

            return Lerp(w, y1, y2);
        }

        private static byte[] GetPermutation(int seed)
        {
            lock (Gate)
            {
                if (PermutationCache.TryGetValue(seed, out byte[] cached))
                {
                    return cached;
                }
            }
            byte[] built = BuildPermutation(seed);
            lock (Gate)
            {
                PermutationCache[seed] = built;
            }
            return built;
        }

        /// <summary>Fisher–Yates shuffle of 0..255 driven by <see cref="MurmurHash"/>, then doubled to 512 entries so lattice lookups never need to wrap.</summary>
        private static byte[] BuildPermutation(int seed)
        {
            var p = new byte[TableSize];
            for (int i = 0; i < TableSize; i++)
            {
                p[i] = (byte)i;
            }
            uint s = unchecked((uint)seed);
            for (int i = TableSize - 1; i > 0; i--)
            {
                int j = (int)((uint)MurmurHash.GetInt(s, (uint)i) % (uint)(i + 1));
                (p[i], p[j]) = (p[j], p[i]);
            }
            var doubled = new byte[TableSize * 2];
            for (int i = 0; i < doubled.Length; i++)
            {
                doubled[i] = p[i & (TableSize - 1)];
            }
            return doubled;
        }

        private static double Fade(double t, NoiseQuality quality)
        {
            switch (quality)
            {
                case NoiseQuality.Low:
                    return t;
                case NoiseQuality.High:
                    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                default:
                    return t * t * (3.0 - 2.0 * t);
            }
        }

        private static double Lerp(double t, double a, double b) => a + t * (b - a);

        /// <summary>12 edge-of-cube gradient directions selected by the low 4 bits of the hashed lattice corner.</summary>
        private static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        private static int FastFloor(double v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }
    }
}
