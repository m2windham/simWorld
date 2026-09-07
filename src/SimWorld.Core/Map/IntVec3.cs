using System;
using System.Globalization;

namespace SimWorld.Map
{
    /// <summary>
    /// A map cell (RimWorld: <c>Verse.IntVec3</c>). The map is a horizontal plane: <see cref="x"/> and
    /// <see cref="z"/> are the two grid axes, <see cref="y"/> is altitude and stays 0 until multi-level maps
    /// exist. Distances below are therefore effectively planar even though the type carries all three axes.
    /// </summary>
    public struct IntVec3 : IEquatable<IntVec3>
    {
        public int x;
        public int y;
        public int z;

        public IntVec3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>Convenience for the common y=0 case.</summary>
        public IntVec3(int x, int z) : this(x, 0, z)
        {
        }

        /// <summary>Sentinel for "no cell" (RimWorld uses this exact value).</summary>
        public static readonly IntVec3 Invalid = new IntVec3(-1000, -1000, -1000);

        public static readonly IntVec3 Zero = new IntVec3(0, 0, 0);

        public bool IsValid => this != Invalid;

        /// <summary>Squared length of this vector projected onto the x/z plane.</summary>
        public int LengthHorizontalSquared => x * x + z * z;

        public float LengthHorizontal => (float)Math.Sqrt(LengthHorizontalSquared);

        public int LengthManhattan => Math.Abs(x) + Math.Abs(z);

        /// <summary>Full 3-axis squared distance; equal to the horizontal distance while y stays 0.</summary>
        public int DistanceToSquared(IntVec3 b)
        {
            int dx = x - b.x, dy = y - b.y, dz = z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }

        public float DistanceTo(IntVec3 b) => (float)Math.Sqrt(DistanceToSquared(b));

        /// <summary>True when <paramref name="b"/> is exactly one cardinal step away (not diagonal).</summary>
        public bool AdjacentToCardinal(IntVec3 b)
        {
            int dx = Math.Abs(x - b.x);
            int dz = Math.Abs(z - b.z);
            return (dx == 1 && dz == 0) || (dx == 0 && dz == 1);
        }

        /// <summary>True when <paramref name="b"/> is one of the 8 surrounding cells (cardinal or diagonal).</summary>
        public bool AdjacentTo8Way(IntVec3 b)
        {
            int dx = Math.Abs(x - b.x);
            int dz = Math.Abs(z - b.z);
            return dx <= 1 && dz <= 1 && (dx != 0 || dz != 0);
        }

        /// <summary>Horizontal distance to <paramref name="otherLoc"/> is at most <paramref name="distance"/>.</summary>
        public bool InHorDistOf(IntVec3 otherLoc, float distance) =>
            (this - otherLoc).LengthHorizontalSquared <= distance * distance;

        public IntVec2 ToIntVec2() => new IntVec2(x, z);

        public static IntVec3 operator +(IntVec3 a, IntVec3 b) => new IntVec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static IntVec3 operator -(IntVec3 a, IntVec3 b) => new IntVec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static IntVec3 operator -(IntVec3 a) => new IntVec3(-a.x, -a.y, -a.z);
        public static IntVec3 operator *(IntVec3 a, int scalar) => new IntVec3(a.x * scalar, a.y * scalar, a.z * scalar);

        public bool Equals(IntVec3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is IntVec3 other && Equals(other);
        public override int GetHashCode() => unchecked((x * 397 ^ y) * 397 ^ z);
        public static bool operator ==(IntVec3 a, IntVec3 b) => a.Equals(b);
        public static bool operator !=(IntVec3 a, IntVec3 b) => !a.Equals(b);

        public override string ToString() =>
            "(" + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + ", " + z.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>A 2D footprint or offset on the map plane (RimWorld: <c>Verse.IntVec2</c>); no altitude axis.</summary>
    public struct IntVec2 : IEquatable<IntVec2>
    {
        public int x;
        public int z;

        public IntVec2(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public static readonly IntVec2 Zero = new IntVec2(0, 0);
        public static readonly IntVec2 One = new IntVec2(1, 1);

        public int Area => x * z;

        public IntVec3 ToIntVec3() => new IntVec3(x, 0, z);

        public bool Equals(IntVec2 other) => x == other.x && z == other.z;
        public override bool Equals(object? obj) => obj is IntVec2 other && Equals(other);
        public override int GetHashCode() => unchecked(x * 397) ^ z;
        public static bool operator ==(IntVec2 a, IntVec2 b) => a.Equals(b);
        public static bool operator !=(IntVec2 a, IntVec2 b) => !a.Equals(b);

        public override string ToString() =>
            "(" + x.ToString(CultureInfo.InvariantCulture) + ", " + z.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
