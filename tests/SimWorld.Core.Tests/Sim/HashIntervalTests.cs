using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// <see cref="HashInterval"/> — the one phase function every periodic system now spreads itself with.
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> The <c>thingIDNumber * 3</c> idiom was
    /// written out three times: on <see cref="Pawn"/>, on <see cref="CompTurretGun"/> and on
    /// <see cref="Fire"/>. Pawn was fixed and measured; the other two were left, and a tracker item recorded
    /// that they "cluster identically". They did. Turret scans run every 15 ticks and fire's complex calcs
    /// every 150, and both are divisible by 3, so both had exactly a third of their phases reachable with
    /// three times the intended peak load on each — the same defect, in two places nobody would have thought
    /// to re-measure.
    ///
    /// <para/>These tests assert the *distribution* over consecutive ids and the fact that the three sites
    /// share one function, never the offset of any single id, which is a hash output nobody should pin.
    /// <c>PawnHashIntervalTests</c> covers the pawn population's own intervals; this covers the helper and
    /// the two sites that were still open-coding it.
    /// </summary>
    public class HashIntervalTests : ContentTestBase
    {
        public HashIntervalTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>The intervals of the two sites this lane brought in, both divisible by 3 and so both
        /// affected. 15 is <c>CompTurretGun.ScanIntervalTicks</c>; 150 is <see cref="Fire.ComplexCalcsInterval"/>.</summary>
        public static IEnumerable<object[]> Intervals => new[]
        {
            new object[] { 15 },
            new object[] { Fire.ComplexCalcsInterval },
        };

        /// <summary>
        /// One function, not three copies of one. This is the whole point of the change: a pawn and a fire
        /// with the same id land on the same phase because they ask the same helper, so a future fix to the
        /// hash reaches every site at once instead of two of three.
        /// </summary>
        [Fact]
        public void Every_site_phases_through_the_same_function()
        {
            Pawn pawn = NewHuman();
            for (int id = 0; id < 500; id++)
            {
                pawn.thingIDNumber = id;
                Assert.Equal(HashInterval.OffsetTicks(id), pawn.HashOffsetTicks());
            }
        }

        /// <summary>
        /// Every phase of the interval is reachable. A settlement's turrets and a burning quarter's fires
        /// outnumber the phases many times over, so a spreading function that leaves phases empty is leaving
        /// capacity unused — and the old offset left two thirds of them empty at both these intervals.
        /// </summary>
        [Theory]
        [MemberData(nameof(Intervals))]
        public void Offsets_cover_every_phase_of_the_interval(int interval)
        {
            int[] phases = PhasesFor(interval, idCount: interval * 8);
            Assert.Equal(interval, phases.Distinct().Count());
        }

        /// <summary>
        /// Peak load is what the spreading buys. The worst phase should hold roughly the even share rather
        /// than a multiple of it; the bound sits between the old offset's exact 3x and the ordinary lumpiness
        /// of a hash, the same bound <c>PawnHashIntervalTests</c> uses for the pawn intervals.
        /// </summary>
        [Theory]
        [MemberData(nameof(Intervals))]
        public void Peak_phase_load_stays_near_the_even_share(int interval)
        {
            const int perPhase = 20;
            int population = interval * perPhase;

            int peak = PhasesFor(interval, population).GroupBy(p => p).Max(g => g.Count());

            Assert.True(
                peak < 2.5 * perPhase,
                "peak phase holds " + peak + " of " + population + " things at interval " + interval +
                "; an even spread would be " + perPhase + " per phase.");
        }

        /// <summary>
        /// The two tests above are only worth anything if the thing they rule out was real, so this states it:
        /// at both of these intervals the old <c>id * 3</c> reached exactly a third of the phases and stacked
        /// three deep on each. If this ever stops holding, the assertions above have stopped proving anything.
        /// </summary>
        [Theory]
        [MemberData(nameof(Intervals))]
        public void The_old_multiply_by_three_offset_really_did_cluster_at_these_intervals(int interval)
        {
            const int perPhase = 20;
            int population = interval * perPhase;

            int[] old = Enumerable.Range(0, population)
                .Select(id => GenMath.PositiveMod(id * 3, interval))
                .ToArray();

            Assert.Equal(interval / 3, old.Distinct().Count());
            Assert.True(old.GroupBy(p => p).Max(g => g.Count()) >= 3 * perPhase);
        }

        /// <summary>
        /// Phasing costs no determinism. The offset is a pure function of the id — the hash is a bijection,
        /// not a draw — so spreading work across ticks can never shift a later roll. A helper that took from
        /// the stream would make every subsequent roll in the game depend on how many turrets were standing.
        /// </summary>
        [Fact]
        public void Asking_for_a_phase_takes_no_roll_at_all()
        {
            uint before = Rand.Current.Iterations;

            for (int id = 0; id < 1000; id++)
            {
                HashInterval.OffsetTicks(id);
                HashInterval.IsHashIntervalTick(id, Fire.ComplexCalcsInterval);
            }

            Assert.Equal(before, Rand.Current.Iterations);
        }

        /// <summary>
        /// The offset is stable for a given id and never negative. A thing that changed phase between calls
        /// would fire at random rather than on a schedule, and the hash really can return
        /// <see cref="int.MinValue"/> — which is why the helper masks rather than taking an absolute value.
        /// </summary>
        [Fact]
        public void The_offset_is_stable_for_an_id_and_never_negative()
        {
            for (int id = 0; id < 2000; id++)
            {
                int first = HashInterval.OffsetTicks(id);
                Assert.Equal(first, HashInterval.OffsetTicks(id));
                Assert.True(first >= 0, "offset for id " + id + " was negative");
            }
        }

        /// <summary>
        /// Over one whole interval every id gets exactly one turn — no thing is skipped and none fires twice.
        /// The phase coverage above says the work is spread; this says none of it is lost.
        /// </summary>
        [Fact]
        public void Every_id_gets_exactly_one_turn_per_interval()
        {
            const int interval = 15;
            int start = Find.TickManager.TicksGame;

            for (int id = 0; id < 200; id++)
            {
                int turns = 0;
                for (int t = 0; t < interval; t++)
                {
                    Find.TickManager.DebugSetTicksGame(start + t);
                    if (HashInterval.IsHashIntervalTick(id, interval)) turns++;
                }
                Assert.Equal(1, turns);
            }
        }

        private static int[] PhasesFor(int interval, int idCount) =>
            Enumerable.Range(0, idCount)
                .Select(id => GenMath.PositiveMod(HashInterval.OffsetTicks(id), interval))
                .ToArray();
    }
}
