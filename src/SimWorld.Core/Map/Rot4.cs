using System;

namespace SimWorld.Map
{
    /// <summary>Which way to turn in <see cref="Rot4.Rotated"/> (RimWorld: <c>Verse.RotationDirection</c>).</summary>
    public enum RotationDirection
    {
        Clockwise,
        Counterclockwise,
    }

    /// <summary>
    /// One of the four cardinal facings (RimWorld: <c>Verse.Rot4</c>). Default (<c>default(Rot4)</c>) is
    /// North, matching RimWorld's byte-0 default.
    /// </summary>
    public struct Rot4 : IEquatable<Rot4>
    {
        public const int NorthInt = 0;
        public const int EastInt = 1;
        public const int SouthInt = 2;
        public const int WestInt = 3;

        private readonly byte rot;

        public Rot4(int asInt)
        {
            rot = (byte)(((asInt % 4) + 4) % 4);
        }

        public static readonly Rot4 North = new Rot4(NorthInt);
        public static readonly Rot4 East = new Rot4(EastInt);
        public static readonly Rot4 South = new Rot4(SouthInt);
        public static readonly Rot4 West = new Rot4(WestInt);

        public int AsInt => rot;

        /// <summary>East/West face sideways; North/South face up/down the map.</summary>
        public bool IsHorizontal => rot == EastInt || rot == WestInt;

        public Rot4 Opposite => new Rot4(AsInt + 2);

        public Rot4 Rotated(RotationDirection dir) =>
            new Rot4(AsInt + (dir == RotationDirection.Clockwise ? 1 : -1));

        /// <summary>Unit offset one step in the direction this rotation faces.</summary>
        public IntVec3 FacingCell
        {
            get
            {
                switch (rot)
                {
                    case NorthInt: return new IntVec3(0, 0, 1);
                    case EastInt: return new IntVec3(1, 0, 0);
                    case SouthInt: return new IntVec3(0, 0, -1);
                    default: return new IntVec3(-1, 0, 0);
                }
            }
        }

        public bool Equals(Rot4 other) => rot == other.rot;
        public override bool Equals(object? obj) => obj is Rot4 other && Equals(other);
        public override int GetHashCode() => rot;
        public static bool operator ==(Rot4 a, Rot4 b) => a.Equals(b);
        public static bool operator !=(Rot4 a, Rot4 b) => !a.Equals(b);

        public override string ToString()
        {
            switch (rot)
            {
                case NorthInt: return "North";
                case EastInt: return "East";
                case SouthInt: return "South";
                default: return "West";
            }
        }
    }
}
