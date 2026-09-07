using System;
using System.Globalization;

namespace SimWorld
{
    /// <summary>
    /// Minimal engine-free 3D vector (RimWorld/Unity: <c>UnityEngine.Vector3</c>). Float precision, matching
    /// the host engine's type; used here for unit-sphere tile positions on the world grid.
    /// </summary>
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 Zero => new Vector3(0f, 0f, 0f);

        public float SqrMagnitude => x * x + y * y + z * z;

        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public Vector3 Normalized
        {
            get
            {
                float mag = Magnitude;
                return mag > 1e-12f ? new Vector3(x / mag, y / mag, z / mag) : Zero;
            }
        }

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);

        /// <summary>Angle between two directions from the origin, in radians (used for great-circle distance on a unit sphere).</summary>
        public static float AngleBetween(Vector3 a, Vector3 b)
        {
            float denom = a.Magnitude * b.Magnitude;
            if (denom <= 1e-12f) return 0f;
            float cos = GenMath.Clamp(Dot(a, b) / denom, -1f, 1f);
            return (float)Math.Acos(cos);
        }

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);
        public override int GetHashCode() => unchecked((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode());
        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

        public override string ToString() =>
            "(" + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + ", " + z.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
