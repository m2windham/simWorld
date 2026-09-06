using System;
using System.Globalization;

namespace SimWorld
{
    /// <summary>
    /// Inclusive integer range. XML form is <c>min~max</c>, or a single number for a
    /// degenerate range (RimWorld: <c>Verse.IntRange</c>).
    /// </summary>
    public struct IntRange : IEquatable<IntRange>
    {
        public int min;
        public int max;

        public IntRange(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        public static IntRange Zero => new IntRange(0, 0);
        public static IntRange One => new IntRange(1, 1);

        /// <summary>max - min.</summary>
        public int Span => max - min;

        public float Average => (min + max) / 2f;

        public bool Includes(int value) => value >= min && value <= max;

        public int ClampToRange(int value) => value < min ? min : (value > max ? max : value);

        /// <summary>Parses "min~max" or "n".</summary>
        public static IntRange FromString(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string[] parts = text.Split('~');
            if (parts.Length == 1)
            {
                int v = ParseInt(parts[0]);
                return new IntRange(v, v);
            }
            if (parts.Length == 2)
            {
                return new IntRange(ParseInt(parts[0]), ParseInt(parts[1]));
            }
            throw new FormatException("IntRange expects 'min~max' but got '" + text + "'.");
        }

        private static int ParseInt(string s) =>
            int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        public bool Equals(IntRange other) => min == other.min && max == other.max;
        public override bool Equals(object? obj) => obj is IntRange other && Equals(other);
        public override int GetHashCode() => unchecked(min * 397) ^ max;
        public static bool operator ==(IntRange a, IntRange b) => a.Equals(b);
        public static bool operator !=(IntRange a, IntRange b) => !a.Equals(b);

        public override string ToString() =>
            min.ToString(CultureInfo.InvariantCulture) + "~" + max.ToString(CultureInfo.InvariantCulture);
    }
}
