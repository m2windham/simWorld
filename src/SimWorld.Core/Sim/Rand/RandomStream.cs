using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// A deterministic random stream: <c>(seed, iterations)</c> hashed through <see cref="MurmurHash"/>.
    /// One instance per system (world gen, combat, director…) keeps their sequences independent, so a
    /// change in one system never perturbs another — the spec's "seeded stream per system".
    /// The API mirrors RimWorld's static <c>Verse.Rand</c>; see <see cref="Rand"/> for that facade.
    /// </summary>
    public sealed class RandomStream
    {
        private struct State
        {
            public uint Seed;
            public uint Iterations;
        }

        private uint seed;
        private uint iterations;
        private readonly Stack<State> stateStack = new Stack<State>();

        public RandomStream(int seed)
        {
            this.seed = (uint)seed;
        }

        /// <summary>Setting the seed restarts the sequence.</summary>
        public int Seed
        {
            get => (int)seed;
            set
            {
                seed = (uint)value;
                iterations = 0u;
            }
        }

        /// <summary>Number of values drawn since the seed was set; with the seed this is the whole state.</summary>
        public uint Iterations => iterations;

        public int StateStackDepth => stateStack.Count;

        /// <summary>Uniform in [0, 1).</summary>
        public float Value => (float)(((double)MurmurHash.GetInt(seed, iterations++) - int.MinValue) / 4294967296.0);

        public int Int => MurmurHash.GetInt(seed, iterations++);

        public bool Bool => Value < 0.5f;

        public int Sign => Bool ? 1 : -1;

        /// <summary>Uniform in [min, max) for ints; returns min when the range is empty.</summary>
        public int Range(int min, int max)
        {
            if (max <= min) return min;
            return min + Math.Abs(Int % (max - min));
        }

        public int RangeInclusive(int min, int max)
        {
            if (max <= min) return min;
            return Range(min, max + 1);
        }

        public float Range(float min, float max)
        {
            if (max <= min) return min;
            return Value * (max - min) + min;
        }

        public int Range(IntRange range) => Range(range.min, range.max + 1);

        public float Range(FloatRange range) => Range(range.min, range.max);

        /// <summary>True with probability <paramref name="chance"/>; 0 never draws, 1 always succeeds.</summary>
        public bool Chance(float chance)
        {
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            return Value < chance;
        }

        public T Element<T>(T a, T b) => Bool ? a : b;

        public T Element<T>(T a, T b, T c)
        {
            float v = Value;
            if (v < 1f / 3f) return a;
            if (v < 2f / 3f) return b;
            return c;
        }

        public T Element<T>(IReadOnlyList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new InvalidOperationException("Cannot pick an element from an empty list.");
            return list[Range(0, list.Count)];
        }

        /// <summary>Normal distribution via Box–Muller, as RimWorld's <c>Rand.Gaussian</c>.</summary>
        public float Gaussian(float centerX = 0f, float widthFactor = 1f)
        {
            float u1 = Value;
            float u2 = Value;
            if (u1 < 1e-7f) u1 = 1e-7f;
            double r = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return (float)r * widthFactor + centerX;
        }

        /// <summary>
        /// Mean-time-between check (RimWorld: <c>Rand.MTBEventOccurs</c>). Returns true if an event with
        /// mean interval <paramref name="mtb"/> (in units of <paramref name="mtbUnit"/> ticks) fires during a
        /// window of <paramref name="checkDuration"/> ticks. RimWorld's "mean" is really a half-life:
        /// P = 1 - 0.5^(duration / (mtb * unit)). Kept identical so tuned content behaves the same.
        /// </summary>
        public bool MTBEventOccurs(float mtb, float mtbUnit, float checkDuration)
        {
            if (float.IsPositiveInfinity(mtb)) return false;
            if (mtb <= 0f) throw new ArgumentOutOfRangeException(nameof(mtb), "mtb must be positive.");
            if (mtbUnit <= 0f) throw new ArgumentOutOfRangeException(nameof(mtbUnit), "mtbUnit must be positive.");
            if (checkDuration <= 0f) throw new ArgumentOutOfRangeException(nameof(checkDuration), "checkDuration must be positive.");
            double exponent = (double)checkDuration / ((double)mtb * mtbUnit);
            double probability = exponent < 0.0001 ? exponent : 1.0 - Math.Pow(0.5, exponent);
            return Value < probability;
        }

        /// <summary>Probability that <see cref="MTBEventOccurs"/> returns true, for tests and UI.</summary>
        public static float MTBEventProbability(float mtb, float mtbUnit, float checkDuration)
        {
            double exponent = (double)checkDuration / ((double)mtb * mtbUnit);
            return (float)(exponent < 0.0001 ? exponent : 1.0 - Math.Pow(0.5, exponent));
        }

        /// <summary>A value that depends only on <paramref name="seed"/>; does not advance this stream.</summary>
        public static float ValueSeeded(int seed) => (float)(((double)MurmurHash.GetInt((uint)seed, 0u) - int.MinValue) / 4294967296.0);

        public static int RangeSeeded(int min, int max, int seed)
        {
            if (max <= min) return min;
            return min + Math.Abs(MurmurHash.GetInt((uint)seed, 0u) % (max - min));
        }

        public static float RangeSeeded(float min, float max, int seed) => ValueSeeded(seed) * (max - min) + min;

        public static bool ChanceSeeded(float chance, int seed)
        {
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            return ValueSeeded(seed) < chance;
        }

        /// <summary>Saves the current state and continues with a new seed; <see cref="PopState"/> restores it.</summary>
        public void PushState(int newSeed)
        {
            stateStack.Push(new State { Seed = seed, Iterations = iterations });
            seed = (uint)newSeed;
            iterations = 0u;
        }

        /// <summary>Saves the current state without reseeding.</summary>
        public void PushState()
        {
            stateStack.Push(new State { Seed = seed, Iterations = iterations });
        }

        public void PopState()
        {
            if (stateStack.Count == 0)
            {
                throw new InvalidOperationException("PopState without a matching PushState.");
            }
            State state = stateStack.Pop();
            seed = state.Seed;
            iterations = state.Iterations;
        }

        /// <summary>Restores the exact position of another capture of this stream.</summary>
        public void Restore(int seedValue, uint iterationCount)
        {
            seed = (uint)seedValue;
            iterations = iterationCount;
        }
    }
}
