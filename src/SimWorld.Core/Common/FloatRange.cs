using System;
using System.Globalization;

namespace SimWorld
{
    /// <summary>
    /// Inclusive float range. XML form is <c>min~max</c>, or a single number for a
    /// degenerate range (RimWorld: <c>Verse.FloatRange</c>).
    /// </summary>
    public struct FloatRange : IEquatable<FloatRange>
    {
        public float min;
        public float max;

        public FloatRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public static FloatRange Zero => new FloatRange(0f, 0f);
        public static FloatRange One => new FloatRange(1f, 1f);
        public static FloatRange ZeroToOne => new FloatRange(0f, 1f);

        public float Span => max - min;
        public float Average => (min + max) / 2f;

        public bool Includes(float value) => value >= min && value <= max;

        public float ClampToRange(float value) => value < min ? min : (value > max ? max : value);

        /// <summary>Linear interpolation from min (t=0) to max (t=1).</summary>
        public float LerpThroughRange(float t) => min + (max - min) * t;

        /// <summary>Inverse of <see cref="LerpThroughRange"/>; 0 when the range is empty.</summary>
        public float InverseLerpThroughRange(float value)
        {
            if (value <= min) return 0f;
            if (value >= max) return 1f;
            float span = max - min;
            return span <= 0f ? 0f : (value - min) / span;
        }

        /// <summary>Parses "min~max" or "n".</summary>
        public static FloatRange FromString(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string[] parts = text.Split('~');
            if (parts.Length == 1)
            {
                float v = ParseFloat(parts[0]);
                return new FloatRange(v, v);
            }
            if (parts.Length == 2)
            {
                return new FloatRange(ParseFloat(parts[0]), ParseFloat(parts[1]));
            }
            throw new FormatException("FloatRange expects 'min~max' but got '" + text + "'.");
        }

        private static float ParseFloat(string s) =>
            float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        public bool Equals(FloatRange other) => min == other.min && max == other.max;
        public override bool Equals(object? obj) => obj is FloatRange other && Equals(other);
        public override int GetHashCode() => unchecked(min.GetHashCode() * 397) ^ max.GetHashCode();
        public static bool operator ==(FloatRange a, FloatRange b) => a.Equals(b);
        public static bool operator !=(FloatRange a, FloatRange b) => !a.Equals(b);

        public override string ToString() =>
            min.ToString(CultureInfo.InvariantCulture) + "~" + max.ToString(CultureInfo.InvariantCulture);
    }
}
