using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

using CoreSettlement = SimWorld.World.Settlement;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="StandingReader"/>: one tension — relative standing against rival civilizations — read off
    /// live state and returned as values. <c>docs/design/goal-renewal.md</c> §8 step 1.
    ///
    /// <para/><b>What this file can and cannot answer.</b> It pins the reading's <i>shape</i>: parity, both
    /// directions, symmetry about parity, which rival the headline picks, what happens when the world settles
    /// to one civilization, and that the same state reads the same every time. It deliberately does <b>not</b>
    /// try to answer "does the tension decay over a long run", which is the question the design document
    /// actually turns on. That needs centuries of simulated time — a single in-game year is 3,600,000 ticks —
    /// and the suite cannot absorb it. <c>TensionSuite</c> in <c>tools/bench</c> is the measurement; this file
    /// is the contract it measures against. A short real-game run at the end checks the reader survives
    /// contact with a real world and reads the same twice from one seed, which is as much as a test can
    /// honestly claim.
    /// </summary>
    public class StandingReaderTests : ContentTestBase
    {
        public StandingReaderTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            Find.World = new CoreWorld();
        }

        // ---- fixtures ----

        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");

        /// <summary>Registers a civilization of <paramref name="population"/> people with both the faction
        /// roster and the world, exactly as <c>DiplomacyAITests</c> does — the reader has to see the same
        /// roster the world's own diplomacy does.</summary>
        private static Faction Civilization(FactionDef def, string name, int population, int tile)
        {
            var faction = new Faction(def, name, "F_" + name);
            Find.FactionManager.Add(faction);

            if (population > 0)
            {
                var settlement = new CoreSettlement(SimWorld.World.WorldObjectDefOf.Settlement, tile, faction, name, 0);
                settlement.AddStatisticalPeople(population);
                Find.World!.worldObjects.Add(settlement);
            }

            return faction;
        }

        private static StandingReading ReadForPlayer() => StandingReader.ReadForPlayer();

        // ---- the reading's shape ----

        [Fact]
        public void Equal_strength_reads_as_parity()
        {
            Civilization(PlayerDef, "Us", 200, 10);
            Civilization(TribalDef, "Them", 200, 20);

            StandingReading r = ReadForPlayer();

            Assert.True(r.IsDefined);
            Assert.Equal(1, r.RivalCount);
            Assert.Equal(0f, r.Gap);
            Assert.Equal(0f, r.Distance);
        }

        [Fact]
        public void A_rival_half_our_size_and_a_rival_twice_it_are_the_same_distance_from_parity_in_opposite_directions()
        {
            // Two separate worlds, same shape mirrored: the whole reason the scale is a log ratio is that
            // "twice" and "half" should be one step from parity either way, which a plain difference cannot say.
            Civilization(PlayerDef, "Us", 400, 10);
            Civilization(TribalDef, "Them", 200, 20);
            StandingReading ahead = ReadForPlayer();

            Find.FactionManager = new FactionManager();
            Find.World = new CoreWorld();
            Civilization(PlayerDef, "Us", 200, 10);
            Civilization(TribalDef, "Them", 400, 20);
            StandingReading behind = ReadForPlayer();

            Assert.True(ahead.Ahead);
            Assert.False(behind.Ahead);
            Assert.Equal(ahead.Distance, behind.Distance, 4);
            Assert.Equal(ahead.Gap, -behind.Gap, 4);
        }

        [Fact]
        public void A_doubling_is_one_step_from_parity()
        {
            // Pinned as a property of the scale rather than as a literal: the contract is "one step per
            // doubling", and that is what a caller reading these numbers is entitled to assume.
            Civilization(PlayerDef, "Us", 100, 10);
            Civilization(TribalDef, "Them", 50, 20);

            Assert.Equal(1f, ReadForPlayer().Gap, 4);

            Find.FactionManager = new FactionManager();
            Find.World = new CoreWorld();
            Civilization(PlayerDef, "Us", 400, 10);
            Civilization(TribalDef, "Them", 50, 20);

            Assert.Equal(3f, ReadForPlayer().Gap, 4); // three doublings.
        }

        [Fact]
        public void The_headline_is_the_rival_furthest_from_parity_whichever_side_that_is()
        {
            // Far behind one, slightly ahead of the other: the headline must be the distant one, and negative.
            Civilization(PlayerDef, "Us", 100, 10);
            Civilization(TribalDef, "Giant", 1600, 20);
            Civilization(OutlanderDef, "Smaller", 80, 30);

            StandingReading r = ReadForPlayer();

            Assert.Equal(2, r.RivalCount);
            Assert.Equal(80, r.WeakestRivalStrength);
            Assert.Equal(1600, r.StrongestRivalStrength);
            Assert.False(r.Ahead);
            Assert.Equal(-4f, r.Gap, 4);
            Assert.True(r.GapToWeakest > 0f);
            Assert.True(r.GapToStrongest < 0f);
        }

        [Fact]
        public void The_two_bracketing_gaps_always_bracket_and_the_headline_is_one_of_them()
        {
            Civilization(PlayerDef, "Us", 300, 10);
            Civilization(TribalDef, "Big", 900, 20);
            Civilization(OutlanderDef, "Small", 150, 30);

            StandingReading r = ReadForPlayer();

            Assert.True(r.GapToWeakest >= r.GapToStrongest);
            Assert.True(r.Gap == r.GapToWeakest || r.Gap == r.GapToStrongest);
        }

        [Fact]
        public void Several_settlements_of_one_civilization_count_as_one_civilizations_strength()
        {
            Faction us = Civilization(PlayerDef, "Us", 100, 10);
            var second = new CoreSettlement(SimWorld.World.WorldObjectDefOf.Settlement, 11, us, "Second", 0);
            second.AddStatisticalPeople(100);
            Find.World!.worldObjects.Add(second);

            Civilization(TribalDef, "Them", 200, 20);

            StandingReading r = ReadForPlayer();

            Assert.Equal(200, r.OwnStrength);
            Assert.Equal(0f, r.Gap); // parity: one rival of 200 against our two settlements of 100.
        }

        // ---- the world settling: question 3 of the measurement, pinned as behaviour ----

        [Fact]
        public void With_no_rival_left_the_reading_is_undefined_rather_than_a_very_large_number()
        {
            Civilization(PlayerDef, "Us", 500, 10);

            StandingReading r = ReadForPlayer();

            Assert.False(r.IsDefined);
            Assert.Equal(0, r.RivalCount);
            Assert.Equal(0f, r.Gap);
            Assert.Equal(500, r.OwnStrength); // still reports what it could read; it is the comparison that is missing.
        }

        [Fact]
        public void A_defeated_civilization_is_not_a_rival()
        {
            Civilization(PlayerDef, "Us", 500, 10);
            Faction beaten = Civilization(TribalDef, "Beaten", 100, 20);
            beaten.defeated = true;

            Assert.False(ReadForPlayer().IsDefined);
        }

        [Fact]
        public void A_civilization_with_nobody_left_in_it_is_not_a_rival()
        {
            // Not defeated, just empty: a ratio against zero is undefined, and DiplomacyAI skips these for the
            // same reason rather than treating them as infinitely weak.
            Civilization(PlayerDef, "Us", 500, 10);
            Civilization(TribalDef, "Ghost", 0, 20);

            Assert.False(ReadForPlayer().IsDefined);
        }

        [Fact]
        public void With_our_own_civilization_gone_the_reading_is_undefined_too()
        {
            Civilization(PlayerDef, "Us", 0, 10);
            Civilization(TribalDef, "Them", 300, 20);

            StandingReading r = ReadForPlayer();

            Assert.False(r.IsDefined);
            Assert.Equal(0, r.OwnStrength);
        }

        [Fact]
        public void Before_a_game_exists_there_is_nothing_to_read()
        {
            Find.World = null;
            Assert.False(StandingReader.ReadForPlayer().IsDefined);
        }

        // ---- strength is DiplomacyAI's, not a second one ----

        [Fact]
        public void Strength_is_the_same_number_the_world_acts_on()
        {
            // The reader must not hold a second opinion about how strong a civilization is — the world would
            // then declare war on one reading and report another. Asserted against DiplomacyAI.StrengthOf
            // directly, which is the call this reader makes.
            Faction us = Civilization(PlayerDef, "Us", 250, 10);
            Faction them = Civilization(TribalDef, "Them", 125, 20);

            StandingReading r = ReadForPlayer();

            Assert.Equal(DiplomacyAI.StrengthOf(us, Find.World!), r.OwnStrength);
            Assert.Equal(DiplomacyAI.StrengthOf(them, Find.World!), r.WeakestRivalStrength);
        }

        // ---- determinism ----

        [Fact]
        public void The_same_state_reads_the_same_every_time()
        {
            Civilization(PlayerDef, "Us", 313, 10);
            Civilization(TribalDef, "Them", 971, 20);
            Civilization(OutlanderDef, "Others", 44, 30);

            StandingReading first = ReadForPlayer();
            StandingReading second = ReadForPlayer();

            Assert.Equal(first.Gap, second.Gap);
            Assert.Equal(first.OwnStrength, second.OwnStrength);
            Assert.Equal(first.RivalCount, second.RivalCount);
            Assert.Equal(first.WeakestRivalStrength, second.WeakestRivalStrength);
            Assert.Equal(first.StrongestRivalStrength, second.StrongestRivalStrength);
        }

        /// <summary>
        /// The reader against a real seeded game rather than a hand-built roster: two runs of one seed, ticked
        /// the same number of ticks, sampled at the same points, must produce the same series.
        ///
        /// <para/><b>Deliberately short, and it says so.</b> Two in-game years is 7,200,000 ticks and is
        /// already near what this suite can absorb; it is nowhere near long enough to say anything about
        /// whether the tension decays. It is here to check the reader survives a real world at all and that
        /// the series is reproducible — the long measurement lives in <c>tools/bench --suite tension</c>.
        ///
        /// <para/>The world starts with rival civilizations already in it, because a solo start has nobody to
        /// compare against for its first decades and two identical series of "undefined" would pass this test
        /// while proving nothing. The assertion that at least one sample was a real reading is what stops it
        /// degenerating that way later.
        /// </summary>
        [Fact]
        public void A_real_seeded_game_produces_the_same_series_twice()
        {
            const int Years = 2;

            List<StandingReading> first = SeriesFromRealGame("standing-determinism", Years);
            List<StandingReading> second = SeriesFromRealGame("standing-determinism", Years);

            Assert.Equal(Years + 1, first.Count);
            Assert.Contains(first, r => r.IsDefined);
            Assert.Equal(first.ConvertAll(Line), second.ConvertAll(Line));
        }

        private static List<StandingReading> SeriesFromRealGame(string seed, int years)
        {
            Game game = Game.NewGame(
                SimWorld.Scenario.ScenarioDefOf.TribalStart.scenario,
                seed,
                subdivisionOverride: 3,
                soloStart: false,
                // SettlementFounder.Found throws outside SettlementTuning.FoundingBandRange, which spec §5b.3
                // fixes at 20-40. This is the low end of what it will accept, for speed.
                bandSize: 20);

            // Unwatched: no interior map is generated, so the run costs world ticks rather than pawn ticks.
            game.God.Attention.ClearFocus();

            var series = new List<StandingReading> { StandingReader.ReadForPlayer() };
            for (int year = 0; year < years; year++)
            {
                for (int i = 0; i < GenDate.TicksPerYear; i++) game.TickManager.DoSingleTick();
                series.Add(StandingReader.ReadForPlayer());
            }

            return series;
        }

        private static string Line(StandingReading r) =>
            r.OwnStrength + "|" + r.RivalCount + "|" + r.WeakestRivalStrength + "|" +
            r.StrongestRivalStrength + "|" + r.Gap.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
    }
}
