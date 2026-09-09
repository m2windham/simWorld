using System;

namespace SimWorld.Map
{
    /// <summary>Planar angle helpers (RimWorld: parts of <c>Verse.GenGeo</c> / <c>Verse.Vector3Utility</c>), used by <see cref="Combat.CoverUtility"/> to weigh how well a piece of cover sits between shooter and target.</summary>
    public static class GenGeo
    {
        /// <summary>
        /// Compass-style angle of <paramref name="v"/> on the map plane, in degrees, wrapped to [0, 360)
        /// (RimWorld: <c>Vector3Utility.AngleFlat</c>, which reads <c>Quaternion.LookRotation(v).eulerAngles.y</c>
        /// — that Unity call is not reproduced bit-for-bit here since this port has no <c>Quaternion</c>;
        /// <c>atan2(x, z)</c> is the same forward-is-+z, x-is-right convention and is exact for the flat
        /// case <c>LookRotation</c> reduces to when <paramref name="v"/>'s y is always 0). Zero for the zero
        /// vector, matching the source's explicit special case.
        /// </summary>
        public static float AngleFlat(IntVec3 v)
        {
            if (v.x == 0 && v.z == 0) return 0f;
            float degrees = (float)(Math.Atan2(v.x, v.z) * (180.0 / Math.PI));
            return degrees < 0f ? degrees + 360f : degrees;
        }

        /// <summary>Smallest angular distance between two compass angles, each in degrees (RimWorld: <c>Verse.GenGeo.AngleDifferenceBetween</c>).</summary>
        public static float AngleDifferenceBetween(float a, float b)
        {
            float aWrapped = a + 360f;
            float bWrapped = b + 360f;
            float best = Math.Abs(a - b);
            best = Math.Min(best, Math.Abs(aWrapped - b));
            best = Math.Min(best, Math.Abs(a - bWrapped));
            return best;
        }
    }
}
