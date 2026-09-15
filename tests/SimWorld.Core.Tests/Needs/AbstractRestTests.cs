using System.Collections.Generic;
using System.Linq;

using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Needs
{
    /// <summary>Scribe holder for round-tripping a pawn mid-sleep-cycle.</summary>
    public class RestHolder : IExposable
    {
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    /// <summary>
    /// Sleep for a citizen with no map (<c>Needs.AbstractRest</c>) — the third instance of the seam
    /// <c>Economy.SettlementLarder</c> opened for food and <c>Needs.AbstractRecreation</c> mirrored for
    /// recreation.
    ///
    /// <para/><b>What was measured before it existed</b> (<c>docs/WORK-REGISTER.md</c> §10): on a
    /// <c>TribalStart</c> settlement of twenty-five founders nobody opened, across three seeds and eight
    /// in-game days, mean rest fell 0.95 → 0.00 by day two and stayed there, leaving <c>Tired</c> at −20 mood
    /// points a head as the largest single term in the settlement's mood ledger. Every route to sleep in this
    /// port runs through <c>AI.JobGiver_GetRest</c>, whose first line refuses a pawn with no map.
    ///
    /// <para/>Per CLAUDE.md nothing here is asserted against a literal that could not be sourced: the length
    /// of an abstract night is derived (<see cref="AbstractRest.SleepTicks"/>) and is pinned as a
    /// <i>behaviour</i> — one night repays about one night — rather than as its number.
    /// </summary>
    public class AbstractRestTests : ContentTestBase
    {
        public AbstractRestTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 16) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // ---- the defect ----

        [Fact]
        public void A_citizen_with_no_map_still_sleeps()
        {
            Pawn p = NewHuman();
            Assert.False(p.Spawned);
            p.needs.rest!.CurLevel = 0f;

            RunTicks(Need.IntervalTicks * 4, p);

            Assert.True(p.needs.rest.CurLevel > 0f,
                "an unspawned citizen — every citizen of every settlement nobody has opened — got no sleep at all");
        }

        /// <summary>
        /// The measured regression, at the scale it was measured at: a citizen left alone for a fortnight
        /// used to end every day of it at 0.00 rest. Asserted as a band and a trend, never as a level.
        /// </summary>
        [Fact]
        public void A_citizen_with_no_map_is_not_ground_down_to_exhaustion_over_a_fortnight()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;

            const int Days = 14;
            const int IntervalsPerDay = GenDate.TicksPerDay / Need.IntervalTicks;

            // The day's *mean* level rather than the level it happens to end on: an abstract citizen
            // sawtooths between the rest tier's threshold and full on a cycle that is not a whole day long,
            // so an end-of-day reading is a sample of the phase and says nothing about the trend.
            var dailyMean = new List<float>();
            var worstCategoryPerDay = new List<RestCategory>();
            for (int day = 0; day < Days; day++)
            {
                RestCategory worst = RestCategory.Rested;
                float sum = 0f;
                for (int i = 0; i < IntervalsPerDay; i++)
                {
                    rest.NeedInterval();
                    sum += rest.CurLevel;
                    if (rest.CurCategory > worst) worst = rest.CurCategory;
                }
                dailyMean.Add(sum / IntervalsPerDay);
                worstCategoryPerDay.Add(worst);
            }

            Assert.DoesNotContain(RestCategory.Exhausted, worstCategoryPerDay);
            Assert.True(rest.TicksAtZero == 0,
                "the citizen ended the fortnight pinned at zero rest, which is the whole of the defect");

            // And it is not a slow slide either: the last few days are no worse than the first few.
            Assert.True(dailyMean.Skip(Days - 4).Average() > dailyMean.Take(4).Average() * 0.75f,
                "rest is decaying away over the fortnight: " + string.Join(", ", dailyMean.Select(v => v.ToString("F2"))));
        }

        /// <summary>
        /// The same span again, asked of the bulk twin the Interval tier actually calls
        /// (<see cref="Need.NeedIntervalBulk"/> at the Long tick's cadence): a citizen nobody is watching
        /// closely must not sleep differently from one nobody is watching at all.
        /// </summary>
        [Fact]
        public void The_bulk_path_sleeps_too_and_lands_in_the_same_band_as_the_interval_path()
        {
            Pawn perTick = NewHuman("PerTick");
            Pawn bulk = NewHuman("Bulk");
            perTick.needs.rest!.CurLevel = 1f;
            bulk.needs.rest!.CurLevel = 1f;

            const int Days = 7;
            const int Coarse = GenTicks.TickLongInterval;
            int coarseTicks = GenDate.TicksPerDay * Days / Coarse;
            int perTickIntervals = coarseTicks * Coarse / Need.IntervalTicks;

            float bulkSum = 0f;
            for (int i = 0; i < coarseTicks; i++)
            {
                bulk.needs.rest.NeedIntervalBulk(Coarse);
                bulkSum += bulk.needs.rest.CurLevel;
            }
            float perTickSum = 0f;
            for (int i = 0; i < perTickIntervals; i++)
            {
                perTick.needs.rest.NeedInterval();
                perTickSum += perTick.needs.rest.CurLevel;
            }

            Assert.True(bulk.needs.rest.CurLevel > 0f, "the Interval tier's citizens never sleep");
            Assert.NotEqual(RestCategory.Exhausted, bulk.needs.rest.CurCategory);

            // Both sawtooth between the rest tier's threshold and full, so they need not agree on a level at
            // any one moment — the contract Need.NeedIntervalBulk states is that the two paths land in the
            // same place over a span, and the mean across a week is that question asked honestly.
            float bulkMean = bulkSum / coarseTicks;
            float perTickMean = perTickSum / perTickIntervals;
            Assert.True(System.Math.Abs(bulkMean - perTickMean) < 0.1f,
                "a week of sleep at the Interval tier's cadence (" + bulkMean.ToString("F2")
                + ") does not match the Full tier's (" + perTickMean.ToString("F2") + ")");
        }

        // ---- the predicate ----

        [Fact]
        public void A_citizen_on_a_map_is_not_handed_abstract_sleep_on_top()
        {
            CoreMap map = NewMap();
            Pawn p = SpawnHuman(map, new IntVec3(8, 0, 8));
            Need_Rest rest = p.needs.rest!;
            rest.CurLevel = 0f;

            AbstractRest.RestInterval(p, rest);

            Assert.Equal(0f, rest.CurLevel);
        }

        [Fact]
        public void A_suspended_citizen_is_owed_nothing_because_the_need_is_not_falling_either()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.CurLevel = 0f;
            p.Suspended = true;

            AbstractRest.RestInterval(p, rest);

            Assert.Equal(0f, rest.CurLevel);
        }

        [Fact]
        public void A_rested_citizen_does_not_sleep_again()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.CurLevel = Need_Rest.ThreshTired + 0.01f;
            float before = rest.CurLevel;

            AbstractRest.RestInterval(p, rest);

            Assert.Equal(before, rest.CurLevel, 5);
        }

        // ---- how much a night repays ----

        /// <summary>
        /// One night, not a refill and not a trickle: a citizen who went to bed only just below the rest
        /// tier's own threshold wakes full, and one run all the way down to nothing gets a night's worth and
        /// has to sleep again. Both are the map path's own behaviour — <c>AI.JobDriver_LayDown</c> pays
        /// <c>RestGainPerTick</c> for as long as the pawn lies there — and neither is asserted as a number.
        /// </summary>
        [Fact]
        public void A_night_repays_about_a_nights_rest()
        {
            Pawn justTired = NewHuman("JustTired");
            justTired.needs.rest!.CurLevel = Need_Rest.ThreshTired - 0.001f;
            AbstractRest.RestInterval(justTired, justTired.needs.rest);
            Assert.Equal(RestCategory.Rested, justTired.needs.rest.CurCategory);
            Assert.True(justTired.needs.rest.CurLevel > 0.99f, "a full night left a citizen short of rested");

            Pawn exhausted = NewHuman("Exhausted");
            exhausted.needs.rest!.CurLevel = 0f;
            AbstractRest.RestInterval(exhausted, exhausted.needs.rest);
            Assert.True(exhausted.needs.rest.CurLevel < 1f,
                "one night brought a citizen back from nothing to completely rested — that is a refill, not a night");
            Assert.Equal(RestCategory.Rested, exhausted.needs.rest.CurCategory);
        }

        /// <summary>
        /// The recorded translation in <c>AbstractRest</c>'s doc, pinned: there is no bed off a map, and the
        /// last bed a citizen slept in before being taken off an interior does not follow them.
        /// </summary>
        [Fact]
        public void A_bed_left_behind_on_a_map_does_not_follow_a_citizen_off_it()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.lastRestEffectiveness = Need_Rest.BedRestEffectiveness;
            rest.CurLevel = 0f;

            AbstractRest.RestInterval(p, rest);

            Assert.Equal(1f, rest.lastRestEffectiveness);

            Pawn onTheGround = NewHuman("Ground");
            onTheGround.needs.rest!.CurLevel = 0f;
            AbstractRest.RestInterval(onTheGround, onTheGround.needs.rest);
            Assert.Equal(onTheGround.needs.rest.CurLevel, rest.CurLevel, 5);
        }

        // ---- the tick-loop rules both reference solutions hold themselves to ----

        [Fact]
        public void Abstract_sleep_never_draws_from_the_random_stream()
        {
            Pawn p = NewHuman();
            p.needs.rest!.CurLevel = 0f;
            Rand.Current = new RandomStream(1234);
            uint before = Rand.Current.Iterations;

            for (int i = 0; i < 50; i++) AbstractRest.RestInterval(p, p.needs.rest);

            Assert.Equal(before, Rand.Current.Iterations);
        }

        // ---- Scribe ----

        /// <summary>
        /// <c>AbstractRest</c> holds no state of its own — the whole answer is re-derived from the need and
        /// the pawn every pass — so what a round trip has to preserve is the need it writes into, mid-cycle:
        /// the level, the ground effectiveness it resets, and the exhaustion clock.
        /// </summary>
        [Fact]
        public void Scribe_round_trips_a_citizen_mid_sleep_cycle()
        {
            Pawn p = NewHuman();
            Need_Rest rest = p.needs.rest!;
            rest.lastRestEffectiveness = Need_Rest.BedRestEffectiveness;
            rest.CurLevel = 0f;
            rest.NeedInterval();

            float levelBefore = rest.CurLevel;
            float effectivenessBefore = rest.lastRestEffectiveness;
            int ticksAtZeroBefore = rest.TicksAtZero;
            Assert.True(levelBefore > 0f);

            var holder = new RestHolder { pawns = new List<Pawn> { p } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            RestHolder loaded = Scribe.Load<RestHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Need_Rest restored = loaded.pawns![0].needs.rest!;
            Assert.Equal(levelBefore, restored.CurLevel, 4);
            Assert.Equal(effectivenessBefore, restored.lastRestEffectiveness, 4);
            Assert.Equal(ticksAtZeroBefore, restored.TicksAtZero);

            // And it carries on sleeping on the other side of the save rather than starting a fresh night.
            restored.CurLevel = 0f;
            restored.NeedInterval();
            Assert.True(restored.CurLevel > 0f);
        }
    }
}
