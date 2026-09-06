using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld
{
    /// <summary>Weighted random selection helpers (RimWorld: <c>Verse.GenCollection</c>).</summary>
    public static class GenCollection
    {
        /// <summary>
        /// Picks an element with probability proportional to its weight. Elements with weight ≤ 0 are
        /// never chosen; returns false when nothing is selectable.
        /// </summary>
        public static bool TryRandomElementByWeight<T>(IReadOnlyList<T> source, Func<T, float> weightSelector, RandomStream rand, out T result)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (weightSelector == null) throw new ArgumentNullException(nameof(weightSelector));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            float total = 0f;
            for (int i = 0; i < source.Count; i++)
            {
                float w = weightSelector(source[i]);
                if (w < 0f) throw new ArgumentOutOfRangeException(nameof(weightSelector), "Negative weight for " + source[i] + ".");
                total += w;
            }
            if (total <= 0f)
            {
                result = default!;
                return false;
            }

            float pick = rand.Value * total;
            float acc = 0f;
            for (int i = 0; i < source.Count; i++)
            {
                float w = weightSelector(source[i]);
                if (w <= 0f) continue;
                acc += w;
                if (pick < acc)
                {
                    result = source[i];
                    return true;
                }
            }
            // Floating error: fall back to the last positive-weight element.
            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (weightSelector(source[i]) > 0f)
                {
                    result = source[i];
                    return true;
                }
            }
            result = default!;
            return false;
        }

        public static T RandomElementByWeight<T>(IReadOnlyList<T> source, Func<T, float> weightSelector, RandomStream rand)
        {
            if (!TryRandomElementByWeight(source, weightSelector, rand, out T result))
            {
                throw new InvalidOperationException("No element has a positive weight.");
            }
            return result;
        }
    }
}
