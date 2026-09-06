using System;

namespace SimWorld
{
    /// <summary>Small numeric helpers used across the sim (RimWorld: parts of <c>Verse.GenMath</c>).</summary>
    public static class GenMath
    {
        /// <summary>Modulo that is never negative: PositiveMod(-1, 5) == 4.</summary>
        public static int PositiveMod(int x, int m)
        {
            int r = x % m;
            return r < 0 ? r + m : r;
        }

        public static long PositiveMod(long x, long m)
        {
            long r = x % m;
            return r < 0 ? r + m : r;
        }

        /// <summary>Division rounding toward negative infinity: FloorDiv(-1, 5) == -1.</summary>
        public static long FloorDiv(long x, long m)
        {
            long q = x / m;
            if ((x % m != 0) && ((x < 0) != (m < 0))) q--;
            return q;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public static int RoundRandom(float f, Sim.RandomStream rand)
        {
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            int floor = (int)Math.Floor(f);
            return floor + (rand.Value < f - floor ? 1 : 0);
        }
    }
}
