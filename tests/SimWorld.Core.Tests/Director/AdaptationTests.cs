using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// The storyteller's memory of how the settlement is doing, held to RimWorld's own numbers: the decompile
    /// of <c>StoryWatcher_Adaptation</c> for the mechanics and <c>https://rimworldwiki.com/wiki/Raid_points</c>
    /// ("Adaption factor") for the curves and the clamp (the page names no game version).
    ///
    /// <para/>The behaviours the lane was asked for are the ones pinned as relations: a settlement that has
    /// just lost people gets a break (the factor falls), one that has gone quiet for long is pressed (it
    /// rises), and the score is held between -60 and 100 whatever happens to it. The curves themselves are
    /// the wiki's sourced numbers and are pinned as such, because a pin on a sourced literal is a pin on the
    /// source.
    /// </summary>
    public class AdaptationTests : ContentTestBase
    {
        public AdaptationTests(CoreContentFixture content) : base(content)
        {
        }

        // ---- helpers ----

        private static global::SimWorld.Director.Storyteller NewStoryteller(string difficulty = "Medium")
        {
            var storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"),
                DefDatabase<DifficultyDef>.GetNamed(difficulty));
            Find.Storyteller = storyteller;
            return storyteller;
        }

        /// <summary>Runs the watcher across the clock in its own 30,000-tick steps, from <paramref name="fromDay"/>
        /// (inclusive) up to <paramref name="toDay"/>, as the game calls it every tick.</summary>
        private static void RunWatcher(StoryWatcher_Adaptation watcher, DifficultyDef? difficulty, double fromDay, double toDay)
        {
            int step = StoryWatcher_Adaptation.UpdateInterval;
            int first = (int)System.Math.Round(fromDay * GenDate.TicksPerDay);
            int last = (int)System.Math.Round(toDay * GenDate.TicksPerDay);
            for (int tick = first; tick < last; tick += step)
            {
                Find.TickManager.DebugSetTicksGame(tick);
                watcher.AdaptationWatcherTick(difficulty);
            }
        }

        private static CivilizationTarget Colony(global::SimWorld.Director.Storyteller storyteller, int people, out List<Pawn> pawns)
        {
            var target = new CivilizationTarget(storyteller);
            pawns = new List<Pawn>();
            for (int i = 0; i < people; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                target.pawns.Add(p);
                pawns.Add(p);
            }
            return target;
        }

        // ---- the sourced numbers ----

        [Fact]
        public void A_new_game_starts_at_zero_days_and_the_wikis_point_eight()
        {
            var watcher = new StoryWatcher_Adaptation();

            Assert.Equal(0f, watcher.AdaptDays);
            Assert.Equal(0.8f, watcher.RawThreatPointsFactor, 4);
        }

        [Fact]
        public void The_factor_curve_is_the_wikis_and_tops_out_at_one_point_four_seven()
        {
            SimpleCurve curve = StoryWatcher_Adaptation.PointsFactorFromAdaptDays;

            Assert.Equal(0.4f, curve.Evaluate(-30f), 4);
            Assert.Equal(0.8f, curve.Evaluate(0f), 4);
            Assert.Equal(1f, curve.Evaluate(30f), 4);
            Assert.Equal(1.2f, curve.Evaluate(60f), 4);
            Assert.Equal(1.6f, curve.Evaluate(120f), 4);
            Assert.Equal(2f, curve.Evaluate(180f), 4);

            // "At the maximum value of 100 AdaptDays, the Adaption Factor is 1.47."
            Assert.Equal(1.47f, curve.Evaluate(StoryWatcher_Adaptation.AdaptDaysMax), 2);
            // ...and the floor of -60 is the curve's own floor, 0.4.
            Assert.Equal(0.4f, curve.Evaluate(StoryWatcher_Adaptation.AdaptDaysMin), 4);
        }

        [Fact]
        public void The_clamp_is_the_wikis_minus_sixty_to_one_hundred()
        {
            Assert.Equal(-60f, StoryWatcher_Adaptation.AdaptDaysMin);
            Assert.Equal(100f, StoryWatcher_Adaptation.AdaptDaysMax);
            Assert.Equal(30f, StoryWatcher_Adaptation.GameStartGraceDays);
        }

        // ---- quiet days raise it ----

        [Fact]
        public void Nothing_grows_from_zero_in_the_first_thirty_days_and_a_day_a_day_after()
        {
            var watcher = new StoryWatcher_Adaptation();
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");

            RunWatcher(watcher, medium, 0, 29.9);
            Assert.Equal(0f, watcher.AdaptDays);

            RunWatcher(watcher, medium, 30, 40);
            Assert.InRange(watcher.AdaptDays, 9.5f, 10.5f);
        }

        [Fact]
        public void A_score_below_zero_climbs_out_during_the_grace_period_and_faster_than_above_it()
        {
            var watcher = new StoryWatcher_Adaptation();
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            watcher.Debug_OffsetAdaptDays(-20f);

            RunWatcher(watcher, medium, 5, 7);

            // Three days a day below zero (wiki, "AdaptDays Added"): two days is six, not two.
            Assert.InRange(watcher.AdaptDays, -14.5f, -13.5f);
        }

        [Fact]
        public void A_long_quiet_raises_the_factor_and_stops_at_the_cap()
        {
            var watcher = new StoryWatcher_Adaptation();
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");

            float previous = watcher.TotalThreatPointsFactor(medium);
            for (int day = 40; day <= 400; day += 40)
            {
                RunWatcher(watcher, medium, day - 40, day);
                float factor = watcher.TotalThreatPointsFactor(medium);
                Assert.True(factor >= previous, $"the factor fell on a quiet stretch ending day {day}.");
                previous = factor;
            }

            Assert.Equal(StoryWatcher_Adaptation.AdaptDaysMax, watcher.AdaptDays);
            Assert.True(previous > 1.4f && previous < 1.5f, "the cap's factor should be about 1.47.");
        }

        [Fact]
        public void Growth_above_zero_follows_the_difficultys_rate_and_below_zero_does_not()
        {
            var fast = new DifficultyDef { defName = "Test_FastAdapt", adaptationGrowthRateFactorOverZero = 2f };

            var above = new StoryWatcher_Adaptation();
            RunWatcher(above, fast, 30, 40);

            var below = new StoryWatcher_Adaptation();
            below.Debug_OffsetAdaptDays(-30f);
            RunWatcher(below, fast, 30, 31);

            // Above zero the rate is doubled (10 days of quiet is 20); below zero RimWorld applies no factor.
            Assert.InRange(above.AdaptDays, 19f, 21f);
            Assert.InRange(below.AdaptDays, -27.5f, -26.5f);
        }

        [Fact]
        public void The_difficulty_blends_the_factor_toward_one()
        {
            var watcher = new StoryWatcher_Adaptation();
            watcher.Debug_OffsetAdaptDays(-30f); // raw factor 0.4, as far from 1 as it goes

            var none = new DifficultyDef { defName = "Test_NoEffect", adaptationEffectFactor = 0f };
            var half = new DifficultyDef { defName = "Test_HalfEffect", adaptationEffectFactor = 0.5f };
            var full = new DifficultyDef { defName = "Test_FullEffect", adaptationEffectFactor = 1f };

            Assert.Equal(1f, watcher.TotalThreatPointsFactor(none), 4);
            Assert.Equal(0.7f, watcher.TotalThreatPointsFactor(half), 4);
            Assert.Equal(0.4f, watcher.TotalThreatPointsFactor(full), 4);
        }

        // ---- losing people gives a break ----

        [Fact]
        public void A_death_lowers_the_factor()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            Colony(storyteller, 12, out List<Pawn> pawns);
            storyteller.adaptation.Debug_OffsetAdaptDays(60f);
            float before = storyteller.adaptation.TotalThreatPointsFactor(storyteller.difficulty);

            pawns[0].health.Kill(null, null);

            Assert.True(storyteller.adaptation.TotalThreatPointsFactor(storyteller.difficulty) < before);
            Assert.True(storyteller.adaptation.AdaptDays < 60f);
        }

        [Fact]
        public void A_settlement_that_lost_people_is_given_a_break_and_one_left_quiet_is_pressed()
        {
            // The lane's two sentences as one relation: after losses the factor is under what a new game
            // starts at; after a long quiet it is over it.
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            DifficultyDef difficulty = storyteller.difficulty;
            float atTheStart = storyteller.adaptation.TotalThreatPointsFactor(difficulty);

            Colony(storyteller, 15, out List<Pawn> pawns);
            for (int i = 0; i < 3; i++) pawns[i].health.Kill(null, null);
            float afterLosses = storyteller.adaptation.TotalThreatPointsFactor(difficulty);

            var quiet = new StoryWatcher_Adaptation();
            RunWatcher(quiet, difficulty, 0, 120);
            float afterQuiet = quiet.TotalThreatPointsFactor(difficulty);

            Assert.True(afterLosses < atTheStart, $"losses gave no break ({afterLosses} vs {atTheStart}).");
            Assert.True(afterQuiet > atTheStart, $"a long quiet did not press ({afterQuiet} vs {atTheStart}).");
        }

        [Fact]
        public void A_death_costs_a_small_colony_more_than_a_large_one()
        {
            // "Downed and death losses both shrink as population grows" (wiki).
            global::SimWorld.Director.Storyteller small = NewStoryteller();
            Colony(small, 5, out List<Pawn> fewPawns);
            small.adaptation.Debug_OffsetAdaptDays(50f);
            fewPawns[0].health.Kill(null, null);
            float lostFromFew = 50f - small.adaptation.AdaptDays;

            global::SimWorld.Director.Storyteller large = NewStoryteller();
            Colony(large, 30, out List<Pawn> manyPawns);
            large.adaptation.Debug_OffsetAdaptDays(50f);
            manyPawns[0].health.Kill(null, null);
            float lostFromMany = 50f - large.adaptation.AdaptDays;

            Assert.True(lostFromFew > lostFromMany, $"{lostFromFew} days from five people, {lostFromMany} from thirty.");
        }

        [Fact]
        public void A_violent_downing_costs_a_small_colony_more_than_a_large_one()
        {
            DamageDef bullet = DefDatabase<DamageDef>.GetNamed("Bullet");
            var blow = new DamageInfo(bullet, 35f);

            global::SimWorld.Director.Storyteller small = NewStoryteller();
            Colony(small, 3, out List<Pawn> fewPawns);
            small.adaptation.Debug_OffsetAdaptDays(50f);
            Assert.True(StorytellerPawnEvents.Notify_PawnDowned(fewPawns[0], blow));
            small.adaptation.AdaptationWatcherTick(small.difficulty);
            float lostFromFew = 50f - small.adaptation.AdaptDays;

            global::SimWorld.Director.Storyteller large = NewStoryteller();
            Colony(large, 30, out List<Pawn> manyPawns);
            large.adaptation.Debug_OffsetAdaptDays(50f);
            Assert.True(StorytellerPawnEvents.Notify_PawnDowned(manyPawns[0], blow));
            large.adaptation.AdaptationWatcherTick(large.difficulty);
            float lostFromMany = 50f - large.adaptation.AdaptDays;

            Assert.True(lostFromFew > lostFromMany, $"{lostFromFew} days from three people, {lostFromMany} from thirty.");
        }

        [Fact]
        public void A_colony_that_keeps_losing_people_is_held_at_the_floor_and_no_lower()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            Colony(storyteller, 20, out List<Pawn> pawns);

            foreach (Pawn p in pawns) p.health.Kill(null, null);

            Assert.Equal(StoryWatcher_Adaptation.AdaptDaysMin, storyteller.adaptation.AdaptDays);
            Assert.Equal(0.4f, storyteller.adaptation.RawThreatPointsFactor, 3);
        }

        [Fact]
        public void The_score_cannot_be_grown_past_the_ceiling_by_the_watcher()
        {
            var watcher = new StoryWatcher_Adaptation();
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            watcher.Debug_OffsetAdaptDays(99.9f);

            RunWatcher(watcher, medium, 100, 200);

            Assert.Equal(StoryWatcher_Adaptation.AdaptDaysMax, watcher.AdaptDays);
        }

        // ---- wiring ----

        [Fact]
        public void The_storyteller_ticks_the_watcher_every_tick()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();

            // Sixty days of the half-day checks, through Storyteller itself rather than the watcher.
            for (int tick = 0; tick <= 60 * GenDate.TicksPerDay; tick += StoryWatcher_Adaptation.UpdateInterval)
            {
                Find.TickManager.DebugSetTicksGame(tick);
                storyteller.StorytellerTick();
            }

            // Thirty days of grace, then about thirty days of growth.
            Assert.InRange(storyteller.adaptation.AdaptDays, 29f, 31f);
        }

        // ---- persistence ----

        [Theory]
        [InlineData(37.5f)]
        [InlineData(-42f)]
        public void Scribe_round_trip_of_the_score(float days)
        {
            var watcher = new StoryWatcher_Adaptation();
            watcher.Debug_OffsetAdaptDays(days);

            string xml = Scribe.SaveToString(watcher, "adaptation");
            StoryWatcher_Adaptation loaded = Scribe.Load<StoryWatcher_Adaptation>(xml, "adaptation");

            Assert.Equal(days, loaded.AdaptDays, 3);
            Assert.Equal(watcher.RawThreatPointsFactor, loaded.RawThreatPointsFactor, 4);
        }
    }
}
