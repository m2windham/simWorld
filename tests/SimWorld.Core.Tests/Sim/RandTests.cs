using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Sim
{
    public class RandTests
    {
        [Fact]
        public void MurmurHash_is_deterministic_and_spreads_inputs()
        {
            Assert.Equal(MurmurHash.GetInt(7u, 3u), MurmurHash.GetInt(7u, 3u));
            Assert.NotEqual(MurmurHash.GetInt(7u, 3u), MurmurHash.GetInt(7u, 4u));
            Assert.NotEqual(MurmurHash.GetInt(7u, 3u), MurmurHash.GetInt(8u, 3u));
            var seen = new HashSet<int>();
            for (uint i = 0; i < 10000; i++) seen.Add(MurmurHash.GetInt(1u, i));
            Assert.Equal(10000, seen.Count);
        }

        [Fact]
        public void Value_is_in_unit_interval_and_roughly_uniform()
        {
            var rand = new RandomStream(42);
            const int n = 20000;
            var buckets = new int[10];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                float v = rand.Value;
                Assert.InRange(v, 0f, 0.99999994f);
                buckets[(int)(v * 10)]++;
                sum += v;
            }
            Assert.InRange(sum / n, 0.48, 0.52);
            Assert.All(buckets, b => Assert.InRange(b, 1700, 2300));
        }

        [Fact]
        public void Same_seed_replays_the_same_sequence()
        {
            var a = new RandomStream(123);
            var b = new RandomStream(123);
            var c = new RandomStream(124);
            var seqA = Enumerable.Range(0, 50).Select(_ => a.Int).ToArray();
            var seqB = Enumerable.Range(0, 50).Select(_ => b.Int).ToArray();
            var seqC = Enumerable.Range(0, 50).Select(_ => c.Int).ToArray();
            Assert.Equal(seqA, seqB);
            Assert.NotEqual(seqA, seqC);
        }

        [Fact]
        public void Setting_the_seed_restarts()
        {
            var rand = new RandomStream(5);
            var first = Enumerable.Range(0, 5).Select(_ => rand.Value).ToArray();
            rand.Seed = 5;
            Assert.Equal(0u, rand.Iterations);
            Assert.Equal(first, Enumerable.Range(0, 5).Select(_ => rand.Value).ToArray());
        }

        [Fact]
        public void Int_range_covers_exactly_its_bounds()
        {
            var rand = new RandomStream(9);
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++) seen.Add(rand.Range(3, 7));
            Assert.Equal(new[] { 3, 4, 5, 6 }, seen.OrderBy(x => x));
            Assert.Equal(3, rand.Range(3, 3));
            Assert.Equal(3, rand.Range(3, 1));

            seen.Clear();
            for (int i = 0; i < 5000; i++) seen.Add(rand.RangeInclusive(3, 7));
            Assert.Equal(new[] { 3, 4, 5, 6, 7 }, seen.OrderBy(x => x));

            seen.Clear();
            for (int i = 0; i < 5000; i++) seen.Add(rand.Range(new IntRange(1, 2)));
            Assert.Equal(new[] { 1, 2 }, seen.OrderBy(x => x));
        }

        [Fact]
        public void Float_range_stays_inside_bounds()
        {
            var rand = new RandomStream(11);
            for (int i = 0; i < 5000; i++)
            {
                Assert.InRange(rand.Range(-2f, 3f), -2f, 3f);
                Assert.InRange(rand.Range(new FloatRange(0.5f, 0.75f)), 0.5f, 0.75f);
            }
            Assert.Equal(4f, rand.Range(4f, 4f));
        }

        [Fact]
        public void Chance_edges_and_rate()
        {
            var rand = new RandomStream(3);
            for (int i = 0; i < 100; i++)
            {
                Assert.False(rand.Chance(0f));
                Assert.True(rand.Chance(1f));
                Assert.False(rand.Chance(-0.5f));
                Assert.True(rand.Chance(1.5f));
            }
            int hits = 0;
            for (int i = 0; i < 10000; i++) if (rand.Chance(0.3f)) hits++;
            Assert.InRange(hits, 2700, 3300);
        }

        [Fact]
        public void Bool_Sign_and_Element_are_balanced()
        {
            var rand = new RandomStream(21);
            int trues = 0, positives = 0, firsts = 0, thirds = 0;
            var list = new List<string> { "a", "b", "c", "d" };
            var listCounts = new Dictionary<string, int>();
            for (int i = 0; i < 12000; i++)
            {
                if (rand.Bool) trues++;
                if (rand.Sign > 0) positives++;
                if (rand.Element(1, 2) == 1) firsts++;
                if (rand.Element(1, 2, 3) == 3) thirds++;
                string e = rand.Element(list);
                listCounts[e] = listCounts.TryGetValue(e, out int c) ? c + 1 : 1;
            }
            Assert.InRange(trues, 5600, 6400);
            Assert.InRange(positives, 5600, 6400);
            Assert.InRange(firsts, 5600, 6400);
            Assert.InRange(thirds, 3600, 4400);
            Assert.Equal(4, listCounts.Count);
            Assert.All(listCounts.Values, v => Assert.InRange(v, 2600, 3400));
            Assert.Throws<InvalidOperationException>(() => rand.Element(new List<int>()));
        }

        [Fact]
        public void Gaussian_has_unit_mean_and_spread()
        {
            var rand = new RandomStream(77);
            const int n = 20000;
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                float g = rand.Gaussian();
                sum += g;
                sumSq += g * g;
            }
            double mean = sum / n;
            double std = Math.Sqrt(sumSq / n - mean * mean);
            Assert.InRange(mean, -0.05, 0.05);
            Assert.InRange(std, 0.95, 1.05);

            double shifted = 0;
            for (int i = 0; i < n; i++) shifted += rand.Gaussian(10f, 2f);
            Assert.InRange(shifted / n, 9.9, 10.1);
        }

        [Fact]
        public void MTBEventOccurs_matches_its_half_life_probability()
        {
            var rand = new RandomStream(5);
            const float mtbDays = 10f;
            float expected = RandomStream.MTBEventProbability(mtbDays, GenDate.TicksPerDay, GenDate.TicksPerDay);
            Assert.InRange(expected, 0.066f, 0.068f); // 1 - 0.5^(1/10)

            int hits = 0;
            const int checks = 20000;
            for (int i = 0; i < checks; i++)
            {
                if (rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, GenDate.TicksPerDay)) hits++;
            }
            Assert.InRange(hits, checks * expected * 0.85, checks * expected * 1.15);

            Assert.False(rand.MTBEventOccurs(float.PositiveInfinity, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rand.MTBEventOccurs(0f, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rand.MTBEventOccurs(1f, 0f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rand.MTBEventOccurs(1f, 1f, 0f));
        }

        [Fact]
        public void MTBEventProbability_is_linear_for_tiny_windows()
        {
            Assert.Equal(0.00001f, RandomStream.MTBEventProbability(100000f, 1f, 1f), 7);
        }

        [Fact]
        public void PushState_and_PopState_restore_the_sequence()
        {
            var rand = new RandomStream(8);
            var reference = new RandomStream(8);
            float a = rand.Value;
            rand.PushState(99);
            float inner = rand.Value;
            Assert.Equal(1, rand.StateStackDepth);
            rand.PopState();
            float b = rand.Value;

            Assert.Equal(reference.Value, a);
            Assert.Equal(reference.Value, b);
            Assert.NotEqual(inner, b);
            Assert.Throws<InvalidOperationException>(() => rand.PopState());
        }

        [Fact]
        public void Seeded_helpers_do_not_advance_the_stream()
        {
            var rand = new RandomStream(1);
            uint before = rand.Iterations;
            Assert.Equal(RandomStream.ValueSeeded(50), RandomStream.ValueSeeded(50));
            Assert.NotEqual(RandomStream.ValueSeeded(50), RandomStream.ValueSeeded(51));
            Assert.InRange(RandomStream.RangeSeeded(2, 5, 50), 2, 4);
            Assert.True(RandomStream.ChanceSeeded(1f, 5));
            Assert.False(RandomStream.ChanceSeeded(0f, 5));
            Assert.Equal(before, rand.Iterations);
        }

        [Fact]
        public void Restore_reproduces_an_exact_position()
        {
            var rand = new RandomStream(13);
            for (int i = 0; i < 10; i++) rand.Value.GetHashCode();
            int seed = rand.Seed;
            uint iterations = rand.Iterations;
            float next = rand.Value;
            var other = new RandomStream(0);
            other.Restore(seed, iterations);
            Assert.Equal(next, other.Value);
        }

        [Fact]
        public void Static_Rand_is_isolated_per_thread()
        {
            float[] fromThread1 = new float[5];
            float[] fromThread2 = new float[5];
            var t1 = new Thread(() => { Rand.Seed = 1; for (int i = 0; i < 5; i++) fromThread1[i] = Rand.Value; });
            var t2 = new Thread(() => { Rand.Seed = 2; for (int i = 0; i < 5; i++) fromThread2[i] = Rand.Value; });
            t1.Start(); t2.Start(); t1.Join(); t2.Join();

            var expected1 = new RandomStream(1);
            var expected2 = new RandomStream(2);
            Assert.Equal(Enumerable.Range(0, 5).Select(_ => expected1.Value).ToArray(), fromThread1);
            Assert.Equal(Enumerable.Range(0, 5).Select(_ => expected2.Value).ToArray(), fromThread2);
        }

        [Fact]
        public void Static_Rand_routes_to_the_current_stream()
        {
            var stream = new RandomStream(31);
            Rand.Current = stream;
            var expected = new RandomStream(31);
            Assert.Equal(expected.Int, Rand.Int);
            Rand.PushState(4);
            Rand.PopState();
            Rand.EnsureStateStackEmpty();
            Rand.PushState();
            Assert.Throws<InvalidOperationException>(Rand.EnsureStateStackEmpty);
            Rand.PopState();
            Assert.Equal(MurmurHash.GetInt(12u, 0u), Rand.HashInt(12));
        }
    }
}
