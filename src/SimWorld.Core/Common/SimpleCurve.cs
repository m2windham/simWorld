using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace SimWorld
{
    /// <summary>One point of a <see cref="SimpleCurve"/>. XML form is <c>(x, y)</c>.</summary>
    public struct CurvePoint : IEquatable<CurvePoint>
    {
        public float x;
        public float y;

        public CurvePoint(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        /// <summary>Parses "(x, y)"; the parentheses are optional.</summary>
        public static CurvePoint FromString(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string s = text.Trim();
            if (s.StartsWith("(", StringComparison.Ordinal) && s.EndsWith(")", StringComparison.Ordinal))
            {
                s = s.Substring(1, s.Length - 2);
            }
            string[] parts = s.Split(',');
            if (parts.Length != 2)
            {
                throw new FormatException("CurvePoint expects '(x, y)' but got '" + text + "'.");
            }
            return new CurvePoint(
                float.Parse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        public bool Equals(CurvePoint other) => x == other.x && y == other.y;
        public override bool Equals(object? obj) => obj is CurvePoint other && Equals(other);
        public override int GetHashCode() => unchecked(x.GetHashCode() * 397) ^ y.GetHashCode();

        public override string ToString() =>
            "(" + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Piecewise-linear curve (RimWorld: <c>Verse.SimpleCurve</c>). Points are kept sorted by x;
    /// evaluation clamps to the first/last point outside the defined range.
    /// XML form:
    /// <code>
    /// &lt;curve&gt;&lt;points&gt;&lt;li&gt;(0, 0)&lt;/li&gt;&lt;li&gt;(10, 1)&lt;/li&gt;&lt;/points&gt;&lt;/curve&gt;
    /// </code>
    /// </summary>
    public class SimpleCurve : IEnumerable<CurvePoint>
    {
        /// <summary>Loaded from XML by reflection; may be assigned unsorted.</summary>
        public List<CurvePoint> points = new List<CurvePoint>();

        public SimpleCurve() { }

        public SimpleCurve(IEnumerable<CurvePoint> points)
        {
            SetPoints(points);
        }

        public int PointsCount => points.Count;

        public CurvePoint this[int index]
        {
            get
            {
                EnsureSorted();
                return points[index];
            }
        }

        public void Add(float x, float y) => Add(new CurvePoint(x, y));

        public void Add(CurvePoint point)
        {
            points.Add(point);
        }

        public void SetPoints(IEnumerable<CurvePoint> newPoints)
        {
            points.Clear();
            points.AddRange(newPoints);
            SortPoints();
        }

        public void SortPoints()
        {
            // Insertion sort: stable, and cheap for the small curves Defs describe.
            for (int i = 1; i < points.Count; i++)
            {
                CurvePoint key = points[i];
                int j = i - 1;
                while (j >= 0 && points[j].x > key.x)
                {
                    points[j + 1] = points[j];
                    j--;
                }
                points[j + 1] = key;
            }
        }

        /// <summary>Linear interpolation between neighbouring points; clamps outside the range.</summary>
        public float Evaluate(float x)
        {
            if (points.Count == 0)
            {
                return 0f;
            }
            EnsureSorted();
            if (x <= points[0].x)
            {
                return points[0].y;
            }
            int last = points.Count - 1;
            if (x >= points[last].x)
            {
                return points[last].y;
            }
            for (int i = 0; i < last; i++)
            {
                CurvePoint a = points[i];
                CurvePoint b = points[i + 1];
                if (x >= a.x && x <= b.x)
                {
                    float span = b.x - a.x;
                    if (span <= 0f)
                    {
                        return b.y;
                    }
                    float t = (x - a.x) / span;
                    return a.y + (b.y - a.y) * t;
                }
            }
            return points[last].y;
        }

        private void EnsureSorted()
        {
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i - 1].x > points[i].x)
                {
                    SortPoints();
                    return;
                }
            }
        }

        public IEnumerator<CurvePoint> GetEnumerator()
        {
            EnsureSorted();
            return points.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
