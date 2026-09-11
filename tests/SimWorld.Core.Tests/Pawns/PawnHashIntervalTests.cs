using System.Collections.Generic;
using System.Linq;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// <see cref="Pawn.HashOffsetTicks"/> is what keeps every hash-interval system from doing all of its work
    /// on one tick: health's heal (600) and bleed (60) passes, needs and mental-break checks (150), the
    /// constant think tree (30). Whether it actually spreads the load is a property of the *distribution* of
    /// offsets over consecutive thing ids, so that is what these tests assert — not the offset of any one
    /// pawn, which is a hash output nobody should pin.
    ///
    /// <para/>The defect they exist for: the offset used to be <c>thingIDNumber * 3</c>. Ids are consecutive,
    /// so the offsets were an arithmetic progression of step 3, and against any interval divisible by 3 —
    /// which is every interval above — <c>gcd(3, interval) == 3</c> left only a third of the phases reachable,
    /// with three times as many pawns on each of them.
    /// </summary>
    public class PawnHashIntervalTests : ContentTestBase
    {
        public PawnHashIntervalTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>The hash intervals the sim actually uses, all divisible by 3 and so all affected.</summary>
        public static IEnumerable<object[]> Intervals => new[]
        {
            new object[] { 30 },   // ConstantThinkTreeTuning.IntervalTicks / JobInterrupts
            new object[] { 60 },   // HealthTuning.BleedInterval
            new object[] { 150 },  // Need.IntervalTicks, MentalState.CheckIntervalTicks
            new object[] { 600 },  // HealthTuning.HealInterval
        };

        /// <summary>
        /// Every phase of the interval is reachable. With ids far outnumbering phases, a spreading function
        /// that leaves any phase empty is leaving capacity on the table; the old <c>* 3</c> left two thirds of
        /// them empty.
        /// </summary>
        [Theory]
        [MemberData(nameof(Intervals))]
        public void Offsets_cover_every_phase_of_the_interval(int interval)
        {
            int[] phases = PhasesFor(interval, idCount: interval * 8);
            Assert.Equal(interval, phases.Distinct().Count());
        }

        /// <summary>
        /// Peak load is what the spreading is for: the worst tick should cost about
        /// <c>population / interval</c> pawns, not a multiple of it. Asserted as a ratio to the even share
        /// rather than as a count — the old offset put exactly <c>3x</c> the even share on every occupied
        /// phase, and the bound sits between that and the ordinary lumpiness of a hash (measured at 1.5-1.8x
        /// here). The population scales with the interval so the even share is the same 20 pawns per phase in
        /// every case; a population thinner than the interval has nothing to concentrate and would make the
        /// ratio meaningless.
        /// </summary>
        [Theory]
        [MemberData(nameof(Intervals))]
        public void Peak_phase_load_stays_near_the_even_share(int interval)
        {
            const int perPhase = 20;
            int population = interval * perPhase;
            int[] phases = PhasesFor(interval, population);

            int peak = phases.GroupBy(p => p).Max(g => g.Count());

            Assert.True(
                peak < 2.5 * perPhase,
                "peak phase holds " + peak + " of " + population + " pawns at interval " + interval +
                "; an even spread would be " + perPhase + " per phase.");
        }

        /// <summary>
        /// The same property at the population the sim is actually budgeted for
        /// (<see cref="TieringTuning.FullTierBudget"/>) and the cadence that costs the most — the constant
        /// think tree's 30 ticks, which every Full-tier pawn pays.
        /// </summary>
        [Fact]
        public void Peak_load_at_the_full_tier_budget_is_not_a_multiple_of_the_even_share()
        {
            const int interval = 30;
            int population = TieringTuning.FullTierBudget;
            int[] phases = PhasesFor(interval, population);

            int peak = phases.GroupBy(p => p).Max(g => g.Count());
            double evenShare = population / (double)interval;

            Assert.True(
                peak < 2.5 * evenShare,
                "peak phase holds " + peak + " of " + population + " pawns; an even spread would be " +
                evenShare.ToString("0.00") + " per phase.");
        }

        /// <summary>
        /// What the old offset looked like, stated once so the tests above are demonstrably sensitive to it:
        /// <c>id * 3</c> against an interval divisible by 3 reaches a third of the phases and stacks three
        /// deep. If this ever stops being true the assertions above are no longer proving anything.
        /// <para/>Also the guard on the bench's escape hatch: it must be off in a shipping process, or every
        /// hash-interval system quietly goes back to the clustered phasing.
        /// </summary>
        [Fact]
        public void The_old_multiply_by_three_offset_really_did_cluster_and_is_off_by_default()
        {
            Assert.False(HashOffsetTuning.UseLegacyMultiplyOffset, "the bench's legacy-offset hatch is open");

            const int interval = 30;
            int[] oldPhases = Enumerable.Range(0, TieringTuning.FullTierBudget)
                .Select(id => GenMath.PositiveMod(HashOffsetTuning.LegacyOffsetTicks(id), interval))
                .ToArray();

            Assert.Equal(interval / 3, oldPhases.Distinct().Count());
            Assert.True(oldPhases.GroupBy(p => p).Max(g => g.Count()) >= 3 * (TieringTuning.FullTierBudget / (double)interval));
        }

        /// <summary>
        /// Determinism and sign. The offset has to be the same every time for a given id — a pawn that changed
        /// phase between calls would fire at random rather than on a schedule — and non-negative, so that
        /// <see cref="GenMath.PositiveMod"/> in <see cref="Pawn.IsHashIntervalTick"/> is a safety net rather
        /// than the thing making it work.
        /// </summary>
        [Fact]
        public void The_offset_is_stable_for_an_id_and_never_negative()
        {
            Pawn pawn = NewHuman();
            for (int id = 0; id < 2000; id++)
            {
                pawn.thingIDNumber = id;
                int first = pawn.HashOffsetTicks();
                Assert.True(first >= 0, "offset for id " + id + " was negative: " + first);
                Assert.Equal(first, pawn.HashOffsetTicks());
            }
        }

        /// <summary>
        /// The consumer's view: over one full interval window each pawn's <see cref="Pawn.IsHashIntervalTick"/>
        /// is true exactly once, and the population's firings are spread across the window rather than landing
        /// on a third of it.
        /// </summary>
        [Fact]
        public void Each_pawn_fires_once_per_window_and_the_window_is_fully_used()
        {
            const int interval = 30;
            int population = TieringTuning.FullTierBudget;
            Pawn probe = NewHuman();
            var firingsPerId = new int[population];
            var ticksUsed = new HashSet<int>();

            // The real method, against the real clock, rather than a re-derivation of its arithmetic.
            for (int t = 0; t < interval; t++)
            {
                for (int id = 0; id < population; id++)
                {
                    probe.thingIDNumber = id;
                    if (!probe.IsHashIntervalTick(interval)) continue;
                    firingsPerId[id]++;
                    ticksUsed.Add(Find.TickManager.TicksGame);
                }
                Find.TickManager.DoSingleTick();
            }

            Assert.All(firingsPerId, count => Assert.Equal(1, count));
            Assert.Equal(interval, ticksUsed.Count);
        }

        /// <summary>Phases of the first <paramref name="idCount"/> consecutive thing ids — the real allocation
        /// pattern, since <c>Thing.AllocateThingId</c> hands them out in sequence.</summary>
        private static int[] PhasesFor(int interval, int idCount)
        {
            Pawn pawn = NewHuman();
            var phases = new int[idCount];
            for (int id = 0; id < idCount; id++)
            {
                pawn.thingIDNumber = id;
                phases[id] = GenMath.PositiveMod(pawn.HashOffsetTicks(), interval);
            }
            return phases;
        }
    }
}
