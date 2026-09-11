using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// RimWorld-shaped static facade (<c>Verse.Rand</c>) over a per-thread default <see cref="RandomStream"/>.
    /// Prefer explicit streams for systems; use this where ported code expects <c>Rand.Value</c>.
    /// Thread-static so tests and worker threads never share a sequence.
    /// </summary>
    public static class Rand
    {
        [ThreadStatic] private static RandomStream? current;

        /// <summary>The stream behind the facade on this thread. Swap it to route ported code to a system stream.</summary>
        public static RandomStream Current
        {
            get => current ??= new RandomStream(Environment.TickCount);
            set => current = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static int Seed
        {
            get => Current.Seed;
            set => Current.Seed = value;
        }

        public static float Value => Current.Value;
        public static int Int => Current.Int;
        public static bool Bool => Current.Bool;
        public static int Sign => Current.Sign;

        public static int Range(int min, int max) => Current.Range(min, max);
        public static int RangeInclusive(int min, int max) => Current.RangeInclusive(min, max);
        public static float Range(float min, float max) => Current.Range(min, max);
        /// <summary>Inclusive of <c>max</c>, as RimWorld's <c>IntRange.RandomInRange</c> is. Delegates to
        /// <see cref="RandomStream.Range(IntRange)"/> rather than re-deriving the bound, so the facade and
        /// the stream cannot drift apart again — they did, and this one was the max-exclusive odd one out.</summary>
        public static int Range(IntRange range) => Current.Range(range);
        /// <summary>Delegates for the same reason the <see cref="IntRange"/> overload above does: the two
        /// happen to agree today, and re-deriving the bound here is how that stopped being true last time.</summary>
        public static float Range(FloatRange range) => Current.Range(range);
        public static bool Chance(float chance) => Current.Chance(chance);
        public static T Element<T>(T a, T b) => Current.Element(a, b);
        public static T Element<T>(T a, T b, T c) => Current.Element(a, b, c);
        public static T Element<T>(IReadOnlyList<T> list) => Current.Element(list);
        public static float Gaussian(float centerX = 0f, float widthFactor = 1f) => Current.Gaussian(centerX, widthFactor);
        public static bool MTBEventOccurs(float mtb, float mtbUnit, float checkDuration) => Current.MTBEventOccurs(mtb, mtbUnit, checkDuration);
        public static float ByCurve(SimpleCurve curve) => Current.ByCurve(curve);

        public static float ValueSeeded(int seed) => RandomStream.ValueSeeded(seed);
        public static int RangeSeeded(int min, int max, int seed) => RandomStream.RangeSeeded(min, max, seed);
        public static float RangeSeeded(float min, float max, int seed) => RandomStream.RangeSeeded(min, max, seed);
        public static bool ChanceSeeded(float chance, int seed) => RandomStream.ChanceSeeded(chance, seed);

        public static void PushState() => Current.PushState();
        public static void PushState(int seed) => Current.PushState(seed);
        public static void PopState() => Current.PopState();

        /// <summary>Guards against a PushState left dangling by ported code.</summary>
        public static void EnsureStateStackEmpty()
        {
            if (Current.StateStackDepth != 0)
            {
                throw new InvalidOperationException("Rand state stack is not empty (" + Current.StateStackDepth + " pushed).");
            }
        }

        public static int HashInt(int value) => MurmurHash.GetInt((uint)value, 0u);
    }
}
