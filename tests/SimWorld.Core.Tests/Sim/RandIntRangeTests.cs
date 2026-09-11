using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// <see cref="Rand.Range(IntRange)"/> and <see cref="RandomStream.Range(IntRange)"/> are the same
    /// operation reached two ways, and for a while they were not: the facade forwarded to
    /// <c>Range(min, max)</c> (max-exclusive) while the stream forwarded to <c>Range(min, max + 1)</c>
    /// (max-inclusive, as RimWorld's <c>IntRange.RandomInRange</c> is). Same name, same parameter type,
    /// different distribution — an off-by-one nobody could see, and one a refactor between the two would
    /// silently flip. These tests hold the two against each other so they cannot drift apart again.
    /// </summary>
    public class RandIntRangeTests
    {
        /// <summary>Ranges worth checking: ordinary, degenerate, single-step, negative, spanning zero, and
        /// one real tuned range from content (<c>HealthTuning.InfectionDelayRange</c>).</summary>
        private static readonly IntRange[] Cases =
        {
            new IntRange(1, 2),
            new IntRange(3, 7),
            new IntRange(0, 0),
            new IntRange(5, 5),
            new IntRange(-4, -1),
            new IntRange(-2, 3),
            new IntRange(15000, 45000),
        };

        [Fact]
        public void The_facade_and_the_stream_draw_the_same_sequence_for_the_same_range()
        {
            foreach (IntRange range in Cases)
            {
                var stream = new RandomStream(4242);
                Rand.Current = new RandomStream(4242);

                for (int i = 0; i < 200; i++)
                {
                    Assert.Equal(stream.Range(range), Rand.Range(range));
                }

                // Not just the values: the same number of draws, or the two would desynchronise any stream
                // they share with the rest of the sim.
                Assert.Equal(stream.Iterations, Rand.Current.Iterations);
            }
        }

        [Fact]
        public void Both_overloads_reach_the_max_of_the_range_and_never_pass_it()
        {
            // Small enough that 5,000 draws cover every value, so the support can be asserted exactly.
            foreach (IntRange range in Cases.Where(r => r.Span <= 8))
            {
                int[] inclusive = Enumerable.Range(range.min, range.Span + 1).ToArray();

                var stream = new RandomStream(7);
                Assert.Equal(inclusive, Support(() => stream.Range(range)));

                Rand.Current = new RandomStream(7);
                Assert.Equal(inclusive, Support(() => Rand.Range(range)));
            }
        }

        /// <summary>A range too wide to enumerate still has to stay inside its own bounds, both ways.</summary>
        [Fact]
        public void Neither_overload_leaves_the_bounds_of_a_wide_range()
        {
            var range = new IntRange(15000, 45000);
            var stream = new RandomStream(13);
            Rand.Current = new RandomStream(13);
            for (int i = 0; i < 5000; i++)
            {
                Assert.InRange(stream.Range(range), range.min, range.max);
                Assert.InRange(Rand.Range(range), range.min, range.max);
            }
        }

        /// <summary>
        /// The boundary the split was about, stated on its own: <see cref="IntRange"/> is documented inclusive,
        /// so <c>max</c> has to be drawable. Before the fix the facade could never return it — for
        /// <c>1~2</c> it returned 1, every time, forever.
        /// </summary>
        [Fact]
        public void The_max_of_a_two_value_range_is_actually_reachable_through_the_facade()
        {
            Rand.Current = new RandomStream(99);
            var range = new IntRange(1, 2);
            var counts = new Dictionary<int, int> { [1] = 0, [2] = 0 };
            for (int i = 0; i < 4000; i++) counts[Rand.Range(range)]++;

            Assert.True(counts[2] > 0, "max of an inclusive IntRange was never drawn through Rand.Range");
            // Roughly even, not merely non-zero: a hit or two would also pass the line above.
            Assert.InRange(counts[2] / 4000.0, 0.45, 0.55);
        }

        /// <summary>
        /// A collapsed range still advances the stream. RimWorld's <c>RandomInRange</c> goes through
        /// <c>Rand.Range(min, min + 1)</c>, whose <c>max &gt; min</c> so it draws; the max-exclusive facade
        /// took the <c>max &lt;= min</c> early-out and drew nothing. That difference is invisible in the value
        /// and loud everywhere downstream, because every later draw on that stream shifts by one — and tuned
        /// content really does collapse ranges (<c>HediffDef.disappearsAfterTicks</c> defaults to
        /// <c>60000~60000</c>).
        /// </summary>
        [Fact]
        public void A_degenerate_range_returns_its_only_value_and_still_costs_one_draw()
        {
            var stream = new RandomStream(5);
            Assert.Equal(60000, stream.Range(new IntRange(60000, 60000)));
            Assert.Equal(1u, stream.Iterations);

            Rand.Current = new RandomStream(5);
            Assert.Equal(60000, Rand.Range(new IntRange(60000, 60000)));
            Assert.Equal(1u, Rand.Current.Iterations);
        }

        /// <summary>The set of values a draw can produce, over enough draws to make the support exact.</summary>
        private static int[] Support(Func<int> draw)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++) seen.Add(draw());
            return seen.OrderBy(x => x).ToArray();
        }
    }
}
