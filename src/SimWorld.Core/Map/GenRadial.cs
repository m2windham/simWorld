using System;
using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// Every cell offset within a fixed maximum radius, precomputed once and sorted nearest-first
    /// (RimWorld: <c>Verse.GenRadial</c>). Radius-limited enumeration (explosions, AI search rings, mining
    /// yield spreads) walks a prefix of this array instead of scanning a square each time.
    /// </summary>
    public static class GenRadial
    {
        /// <summary>No radius query above this is supported; mirrors RimWorld's own ceiling.</summary>
        public const float MaxRadius = 60f;

        private static readonly IntVec3[] pattern = BuildPattern();

        /// <summary>Every offset within <see cref="MaxRadius"/> of the origin, ascending by distance.</summary>
        public static IReadOnlyList<IntVec3> RadialPattern => pattern;

        public static IEnumerable<IntVec3> RadialPatternInRadius(float radius)
        {
            int count = NumCellsInRadius(radius);
            for (int i = 0; i < count; i++)
            {
                yield return pattern[i];
            }
        }

        /// <summary>How many leading entries of <see cref="RadialPattern"/> fall within <paramref name="radius"/>.</summary>
        public static int NumCellsInRadius(float radius)
        {
            if (radius < 0f) return 0;
            if (radius >= MaxRadius) return pattern.Length;
            return CountUpTo(radius * radius);
        }

        private static int CountUpTo(float rSq)
        {
            int lo = 0, hi = pattern.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pattern[mid].LengthHorizontalSquared <= rSq) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Cells within <paramref name="radius"/> of <paramref name="center"/>; includes the center cell only when <paramref name="useCenter"/>.</summary>
        public static IEnumerable<IntVec3> RadialCellsAround(IntVec3 center, float radius, bool useCenter)
        {
            int count = NumCellsInRadius(radius);
            for (int i = 0; i < count; i++)
            {
                IntVec3 offset = pattern[i];
                if (!useCenter && offset == IntVec3.Zero) continue;
                yield return center + offset;
            }
        }

        private static IntVec3[] BuildPattern()
        {
            int ceil = (int)Math.Ceiling(MaxRadius);
            float maxSq = MaxRadius * MaxRadius;
            var list = new List<IntVec3>();
            for (int x = -ceil; x <= ceil; x++)
            {
                for (int z = -ceil; z <= ceil; z++)
                {
                    var v = new IntVec3(x, 0, z);
                    if (v.LengthHorizontalSquared <= maxSq) list.Add(v);
                }
            }
            list.Sort((a, b) => a.LengthHorizontalSquared.CompareTo(b.LengthHorizontalSquared));
            return list.ToArray();
        }
    }
}
