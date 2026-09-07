using System;

namespace SimWorld.Crafting
{
    /// <summary>Craftsmanship tier of a quality-capable item (RimWorld: <c>RimWorld.QualityCategory</c>).</summary>
    public enum QualityCategory
    {
        Awful,
        Poor,
        Normal,
        Good,
        Excellent,
        Masterwork,
        Legendary,
    }

    /// <summary>Inclusive [min, max] band of qualities — e.g. a ThingFilter's allowed-quality gate (RimWorld: <c>RimWorld.QualityRange</c>).</summary>
    public struct QualityRange : IEquatable<QualityRange>
    {
        public QualityCategory min;
        public QualityCategory max;

        public QualityRange(QualityCategory min, QualityCategory max)
        {
            this.min = min;
            this.max = max;
        }

        public static QualityRange All => new QualityRange(QualityCategory.Awful, QualityCategory.Legendary);

        public bool Includes(QualityCategory q) => q >= min && q <= max;

        public bool Equals(QualityRange other) => min == other.min && max == other.max;
        public override bool Equals(object? obj) => obj is QualityRange other && Equals(other);
        public override int GetHashCode() => unchecked(((int)min * 397) ^ (int)max);
        public static bool operator ==(QualityRange a, QualityRange b) => a.Equals(b);
        public static bool operator !=(QualityRange a, QualityRange b) => !a.Equals(b);

        public override string ToString() => min + "~" + max;
    }
}
