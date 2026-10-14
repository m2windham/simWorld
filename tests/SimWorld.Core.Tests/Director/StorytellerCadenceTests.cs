using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// How often the three storytellers send a big threat, against what the RimWorld wiki prints for them:
    /// <c>https://rimworldwiki.com/wiki/Cassandra_Classic</c>, <c>/Phoebe_Chillax</c> and <c>/Randy_Random</c>
    /// (none names a game version). Two halves: the shipped numbers are the sourced ones (a pin on sourced
    /// literals is a pin on a source, not on a tuning guess), and the cycle machinery honours them — a plan
    /// that never starts early, never bunches closer than <c>minSpacingDays</c>, and keeps to its on-phases.
    ///
    /// <para/>The old port rolled a mean-time-between check inside the on-phase, which promised "roughly" the
    /// right count and bound neither the count nor the spacing. These tests are what the replacement
    /// (<see cref="IncidentCycleUtility"/>, RimWorld's seeded hit list) is held to.
    /// </summary>
    public class StorytellerCadenceTests : ContentTestBase
    {
        public StorytellerCadenceTests(CoreContentFixture content) : base(content)
        {
        }

        // ---- helpers ----

        private static StorytellerCompProperties_OnOffCycle ThreatBigCycle(string storyteller)
        {
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(storyteller);
            return def.comps!.OfType<StorytellerCompProperties_OnOffCycle>()
                .Single(c => c.category == IncidentCategoryDefOf.ThreatBig);
        }

        private static int Intervals(float days) => (int)Math.Round(days * 60f, MidpointRounding.ToEven);

        /// <summary>Every interval (counted from the cycle start) at which the cycle's plan fires, over
        /// <paramref name="cycles"/> whole cycles, read as the plan rather than sampled.</summary>
        private static List<int> Plan(
            StorytellerCompProperties_OnOffCycle p, IIncidentTarget target, int cycles, int salt = 0)
        {
            int cycleIntervals = Intervals(p.onDays) + Intervals(p.offDays);
            var all = new List<int>();
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                all.AddRange(IncidentCycleUtility.PlannedHitsOfCycleContaining(
                    target, salt, p.onDays, p.offDays, p.minSpacingDays,
                    p.numIncidentsRange.min, p.numIncidentsRange.max, 1f, cycle * cycleIntervals));
            }
            return all;
        }

        public static IEnumerable<object[]> CyclingStorytellers() =>
            new[] { "Cassandra_Classic", "Phoebe_Chillax" }.Select(n => new object[] { n });

        // ---- the shipped numbers are the wiki's ----

        [Fact]
        public void Cassandras_big_threat_cycle_is_the_one_the_wiki_prints()
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle("Cassandra_Classic");

            Assert.Equal(11f, p.minDaysPassed);
            Assert.Equal(4.6f, p.onDays);
            Assert.Equal(6f, p.offDays);
            Assert.Equal(1.9f, p.minSpacingDays);
            Assert.Equal(1f, p.numIncidentsRange.min);
            Assert.Equal(2f, p.numIncidentsRange.max);
        }

        [Fact]
        public void Phoebes_big_threat_cycle_is_the_one_the_wiki_prints()
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle("Phoebe_Chillax");

            Assert.Equal(13f, p.minDaysPassed);
            Assert.Equal(8f, p.onDays);
            Assert.Equal(8f, p.offDays);
            Assert.Equal(1f, p.numIncidentsRange.min);
            Assert.Equal(1f, p.numIncidentsRange.max);
        }

        [Fact]
        public void Randys_one_comp_is_the_one_the_wiki_prints()
        {
            StorytellerDef randy = DefDatabase<StorytellerDef>.GetNamed("Randy_Random");
            var comp = (StorytellerCompProperties_RandomMain)Assert.Single(randy.comps!);

            Assert.Equal(1f, comp.minDaysPassed);
            Assert.Equal(1.35f, comp.mtbDays);
            Assert.Equal(13f, comp.maxThreatBigIntervalDays);
            Assert.Equal(0.5f, comp.randomPointsFactorRange.min);
            Assert.Equal(1.5f, comp.randomPointsFactorRange.max);

            float Weight(string category) => comp.categoryWeights!.Single(e => e.category.defName == category).weight;
            Assert.Equal(3.5f, Weight("Misc"));
            Assert.Equal(1.4f, Weight("ThreatBig"));
            Assert.Equal(1.1f, Weight("OrbitalVisitor"));
            Assert.Equal(2.4f, Weight("FactionArrival"));
            Assert.Equal(0.6f, Weight("ThreatSmall"));
            Assert.Equal(0.22f, Weight("ShipChunkDrop"));
        }

        [Theory]
        [MemberData(nameof(CyclingStorytellers))]
        public void Misc_events_come_on_the_wikis_four_point_eight_day_clock(string storyteller)
        {
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(storyteller);
            StorytellerCompProperties_RandomMain misc = def.comps!.OfType<StorytellerCompProperties_RandomMain>()
                .Single(c => c.categoryWeights!.Count == 1 && c.categoryWeights[0].category == IncidentCategoryDefOf.Misc);

            Assert.Equal(4.8f, misc.mtbDays);
        }

        [Fact]
        public void The_big_threat_cycle_is_on_for_a_minority_of_Cassandras_time_and_half_of_Phoebes()
        {
            // The first defect the lane found: threats "on" 82% of the time (6.8 of 8.3 days) against the
            // wiki's 43% (4.6 of 10.6). Said as a relation to the def rather than as the numbers.
            StorytellerCompProperties_OnOffCycle cassandra = ThreatBigCycle("Cassandra_Classic");
            StorytellerCompProperties_OnOffCycle phoebe = ThreatBigCycle("Phoebe_Chillax");

            float cassandraOn = cassandra.onDays / (cassandra.onDays + cassandra.offDays);
            float phoebeOn = phoebe.onDays / (phoebe.onDays + phoebe.offDays);

            Assert.InRange(cassandraOn, 0.3f, 0.5f);
            Assert.Equal(0.5f, phoebeOn);
            Assert.True(phoebeOn > cassandraOn, "Phoebe is the one who sends her threats in the longer window.");
        }

        // ---- the plan honours them ----

        [Theory]
        [MemberData(nameof(CyclingStorytellers))]
        public void No_big_threat_comes_before_minDaysPassed(string storyteller)
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle(storyteller);
            var target = new CivilizationTarget();
            int firstOnPhaseTick = (int)Math.Round(p.minDaysPassed * GenDate.TicksPerDay);

            // Sampled the way the game samples it: the clock at each storyteller interval, through the
            // comp-facing utility, from the first tick to the last interval before the first on-phase.
            int firstIntervalBefore = firstOnPhaseTick / Storyteller.IncidentCycleLengthTicks - 1;
            for (int interval = 0; interval <= firstIntervalBefore; interval++)
            {
                Find.TickManager.DebugSetTicksGame(interval * Storyteller.IncidentCycleLengthTicks);
                Assert.Equal(0, IncidentCycleUtility.IncidentCountThisInterval(
                    target, 0, p.minDaysPassed, p.onDays, p.offDays, p.minSpacingDays,
                    p.numIncidentsRange.min, p.numIncidentsRange.max));
            }
        }

        [Theory]
        [MemberData(nameof(CyclingStorytellers))]
        public void Two_big_threats_are_never_closer_than_minSpacingDays(string storyteller)
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle(storyteller);
            int minSpacing = Intervals(p.minSpacingDays);

            // Several worlds' worth of plan, so the spacing is not an accident of one seed.
            for (int salt = 0; salt < 8; salt++)
            {
                var target = new CivilizationTarget { ConstantRandSeed = 100 + salt };
                List<int> hits = Plan(p, target, cycles: 120, salt: salt);

                Assert.NotEmpty(hits);
                for (int i = 1; i < hits.Count; i++)
                {
                    Assert.True(hits[i] - hits[i - 1] >= minSpacing,
                        $"{storyteller}: hits at intervals {hits[i - 1]} and {hits[i]} are {hits[i] - hits[i - 1]} apart, under {minSpacing}.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(CyclingStorytellers))]
        public void Every_big_threat_falls_inside_an_on_phase(string storyteller)
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle(storyteller);
            int on = Intervals(p.onDays);
            int cycle = on + Intervals(p.offDays);
            var target = new CivilizationTarget();

            foreach (int hit in Plan(p, target, cycles: 200))
            {
                Assert.True(hit % cycle < on, $"{storyteller}: a hit at interval {hit} lands in the off-phase.");
            }
        }

        [Fact]
        public void Cassandra_sends_one_or_two_per_on_phase_about_half_each()
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle("Cassandra_Classic");
            int cycleIntervals = Intervals(p.onDays) + Intervals(p.offDays);
            int cycles = 600;
            var target = new CivilizationTarget();

            int ones = 0, twos = 0;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                int count = IncidentCycleUtility.PlannedHitsOfCycleContaining(
                    target, 0, p.onDays, p.offDays, p.minSpacingDays, 1f, 2f, 1f, cycle * cycleIntervals).Count;
                if (count == 1) ones++;
                else if (count == 2) twos++;
                else Assert.Fail($"cycle {cycle} planned {count} threats; the range is 1~2.");
            }

            // "There is a 50-50 chance either 1 or 2 major threats will be sent" (the wiki). A band wide
            // enough that a fair coin over 600 flips passes every time it will ever be asked.
            Assert.InRange(twos / (float)cycles, 0.40f, 0.60f);
            Assert.Equal(cycles, ones + twos);
        }

        [Fact]
        public void Phoebe_sends_exactly_one_per_on_phase()
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle("Phoebe_Chillax");
            int cycleIntervals = Intervals(p.onDays) + Intervals(p.offDays);
            var target = new CivilizationTarget();

            for (int cycle = 0; cycle < 100; cycle++)
            {
                Assert.Single(IncidentCycleUtility.PlannedHitsOfCycleContaining(
                    target, 0, p.onDays, p.offDays, p.minSpacingDays, 1f, 1f, 1f, cycle * cycleIntervals));
            }
        }

        [Theory]
        [MemberData(nameof(CyclingStorytellers))]
        public void Sampling_the_clock_interval_by_interval_agrees_with_the_plan(string storyteller)
        {
            // The utility answers one interval at a time; the plan is the same thing read whole. If they
            // disagreed the comp would fire on days the schedule does not say.
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle(storyteller);
            var target = new CivilizationTarget();
            int start = Intervals(p.minDaysPassed);
            int cycles = 6;

            var planned = new HashSet<int>(Plan(p, target, cycles));
            var sampled = new List<int>();
            int last = cycles * (Intervals(p.onDays) + Intervals(p.offDays));
            for (int sinceStart = 0; sinceStart < last; sinceStart++)
            {
                Find.TickManager.DebugSetTicksGame((start + sinceStart) * Storyteller.IncidentCycleLengthTicks);
                int count = IncidentCycleUtility.IncidentCountThisInterval(
                    target, 0, p.minDaysPassed, p.onDays, p.offDays, p.minSpacingDays,
                    p.numIncidentsRange.min, p.numIncidentsRange.max);
                for (int i = 0; i < count; i++) sampled.Add(sinceStart);
            }

            Assert.Equal(planned.OrderBy(x => x), sampled.OrderBy(x => x));
        }

        [Fact]
        public void The_plan_is_the_same_every_time_it_is_asked_and_differs_between_worlds()
        {
            StorytellerCompProperties_OnOffCycle p = ThreatBigCycle("Cassandra_Classic");
            var target = new CivilizationTarget();

            Assert.Equal(Plan(p, target, 30), Plan(p, target, 30));

            var other = new CivilizationTarget { ConstantRandSeed = 2 };
            Assert.NotEqual(Plan(p, target, 30), Plan(p, other, 30));
        }

        [Fact]
        public void The_real_comp_fires_on_the_planned_days_and_nowhere_else()
        {
            // Through StorytellerComp_OnOffCycle itself, with Cassandra's numbers and a harmless incident, so
            // the pipeline from the def's props to a FiringIncident is the thing measured.
            StorytellerCompProperties_OnOffCycle shipped = ThreatBigCycle("Cassandra_Classic");
            var props = new StorytellerCompProperties_OnOffCycle
            {
                minDaysPassed = shipped.minDaysPassed,
                onDays = shipped.onDays,
                offDays = shipped.offDays,
                minSpacingDays = shipped.minSpacingDays,
                numIncidentsRange = shipped.numIncidentsRange,
                incident = new IncidentDef
                {
                    defName = "Test_Cadence_" + Guid.NewGuid().ToString("N"),
                    category = IncidentCategoryDefOf.Misc,
                    workerClass = typeof(IncidentWorker_Placeholder),
                },
            };
            var comp = new StorytellerComp_OnOffCycle { props = props };
            var target = new CivilizationTarget();
            Find.Storyteller = new global::SimWorld.Director.Storyteller(
                DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic"), DefDatabase<DifficultyDef>.GetNamed("Medium"));

            int start = Intervals(shipped.minDaysPassed);
            int cycles = 5;
            int last = cycles * (Intervals(shipped.onDays) + Intervals(shipped.offDays));
            var planned = Plan(props, target, cycles, salt: -1);

            var fired = new List<int>();
            for (int interval = 0; interval < start + last; interval++)
            {
                Find.TickManager.DebugSetTicksGame(interval * Storyteller.IncidentCycleLengthTicks);
                int count = comp.MakeIntervalIncidents(target).Count();
                for (int i = 0; i < count; i++) fired.Add(interval - start);
            }

            Assert.True(fired.All(f => f >= 0), "the comp fired before minDaysPassed.");
            Assert.Equal(planned.OrderBy(x => x), fired.OrderBy(x => x));
        }

        // ---- config and the minDaysPassed gate ----

        [Fact]
        public void A_spacing_the_on_phase_cannot_hold_is_a_config_error()
        {
            var props = new StorytellerCompProperties_OnOffCycle
            {
                category = IncidentCategoryDefOf.ThreatBig,
                onDays = 4f,
                minSpacingDays = 3f,
                numIncidentsRange = new FloatRange(1f, 2f),
            };

            Assert.Contains(props.ConfigErrors(DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic")),
                e => e.Contains("minSpacingDays"));
        }

        [Fact]
        public void The_shipped_cycles_hold_their_own_spacing()
        {
            foreach (string name in new[] { "Cassandra_Classic", "Phoebe_Chillax" })
            {
                StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(name);
                Assert.Empty(def.ConfigErrors());
            }
        }

        [Fact]
        public void A_comp_is_not_consulted_before_its_minDaysPassed()
        {
            // Randy's comp reads minDaysPassed 1; the gate that enforces it is the base props' (RimWorld puts
            // it on StorytellerCompProperties and checks it for every comp in Storyteller itself).
            var comp = new StorytellerCompProperties_RandomMain
            {
                minDaysPassed = 5f,
                mtbDays = 0.0001f, // fires on essentially every consultation, so silence means "not asked"
                categoryWeights = new List<IncidentCategoryEntry>
                {
                    new IncidentCategoryEntry { category = IncidentCategoryDefOf.Misc, weight = 1f },
                },
            };
            var def = new StorytellerDef { defName = "Test_Gate", comps = new List<StorytellerCompProperties> { comp } };
            var storyteller = new global::SimWorld.Director.Storyteller(def, DefDatabase<DifficultyDef>.GetNamed("Medium"));
            Find.Storyteller = storyteller;
            storyteller.RegisterTarget(new CivilizationTarget());

            int asked = 0;
            storyteller.IncidentFired += _ => asked++;

            Find.TickManager.DebugSetTicksGame(4 * GenDate.TicksPerDay);
            for (int i = 0; i < 20; i++) storyteller.MakeIncidentsForInterval();
            Assert.Equal(0, asked);

            Find.TickManager.DebugSetTicksGame(6 * GenDate.TicksPerDay);
            for (int i = 0; i < 20; i++) storyteller.MakeIncidentsForInterval();
            Assert.True(asked > 0, "past minDaysPassed the comp should have been consulted.");
        }
    }
}
