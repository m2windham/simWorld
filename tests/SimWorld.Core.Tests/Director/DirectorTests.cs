using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    public class DirectorTests : ContentTestBase
    {
        public DirectorTests(CoreContentFixture content) : base(content)
        {
        }

        // ---- helpers ----

        private static global::SimWorld.Director.Storyteller NewStoryteller(string storytellerName = "Cassandra_Classic", string difficultyName = "Medium")
        {
            StorytellerDef def = DefDatabase<StorytellerDef>.GetNamed(storytellerName);
            DifficultyDef difficulty = DefDatabase<DifficultyDef>.GetNamed(difficultyName);
            var storyteller = new global::SimWorld.Director.Storyteller(def, difficulty);
            Find.Storyteller = storyteller;
            return storyteller;
        }

        private static CivilizationTarget NewTarget(global::SimWorld.Director.Storyteller? storyteller = null, int pawnCount = 3, float wealth = 0f)
        {
            var target = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = wealth };
            for (int i = 0; i < pawnCount; i++) target.pawns.Add(NewHuman("Pawn" + i));
            return target;
        }

        private static IncidentDef MakeTestIncidentDef(
            IncidentCategoryDef category, Type workerClass,
            int earliestDay = 0, int minPopulation = 0, float minThreatPoints = 0f, float minRefireDays = 0f)
        {
            return new IncidentDef
            {
                defName = "Test_" + Guid.NewGuid().ToString("N"),
                category = category,
                workerClass = workerClass,
                earliestDay = earliestDay,
                minPopulation = minPopulation,
                minThreatPoints = minThreatPoints,
                minRefireDays = minRefireDays,
            };
        }

        private static DamageResult Hit(Pawn p, string damage, float amount, string partLabel)
        {
            DamageDef def = DefDatabase<DamageDef>.GetNamed(damage);
            BodyPartRecord part = p.RaceProps.body!.GetPartByLabel(partLabel) ?? throw new InvalidOperationException("no part " + partLabel);
            return def.Worker.Apply(new DamageInfo(def, amount, hitPart: part), p);
        }

        private static void AdvanceIntervals(global::SimWorld.Director.Storyteller storyteller, int intervals)
        {
            for (int i = 0; i < intervals; i++)
            {
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
                storyteller.StorytellerTick();
            }
        }

        // ---- content ----

        [Fact]
        public void Director_content_loads_with_expected_counts_and_defOfs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(11, DefDatabase<IncidentCategoryDef>.DefCount);
            Assert.Equal(6, DefDatabase<IncidentTargetTagDef>.DefCount);
            Assert.Equal(5, DefDatabase<DifficultyDef>.DefCount);
            Assert.Equal(3, DefDatabase<StorytellerDef>.DefCount);
            Assert.Equal(13, DefDatabase<IncidentDef>.DefCount); // +1: GiveQuest_Random (Quests & Scenario port)

            Assert.NotNull(IncidentCategoryDefOf.ThreatBig);
            Assert.NotNull(IncidentCategoryDefOf.ThreatSmall);
            Assert.NotNull(IncidentCategoryDefOf.Misc);
            Assert.NotNull(IncidentCategoryDefOf.DiseaseHuman);
            Assert.NotNull(IncidentTargetTagDefOf.Map_PlayerHome);
            Assert.NotNull(IncidentTargetTagDefOf.World);
            Assert.NotNull(StorytellerDefOf.Cassandra_Classic);
            Assert.NotNull(DifficultyDefOf.Medium);
            Assert.NotNull(IncidentDefOf.RaidEnemy);
            Assert.NotNull(IncidentDefOf.Disease_Flu);
            Assert.Equal("The Chronicle", StorytellerDefOf.Cassandra_Classic.persona);
        }

        // ---- threat points ----

        [Fact]
        public void DefaultThreatPointsNow_zero_wealth_three_healthy_pawns_floors_at_35()
        {
            NewStoryteller();
            CivilizationTarget target = NewTarget(Find.Storyteller, pawnCount: 3, wealth: 0f);
            Find.TickManager.DebugSetTicksGame(0);

            float points = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.Equal(StorytellerUtility.MinThreatPoints, points);
        }

        [Fact]
        public void DefaultThreatPointsNow_scales_with_wealth()
        {
            NewStoryteller();
            CivilizationTarget target = NewTarget(Find.Storyteller, pawnCount: 3, wealth: 400000f);
            Find.TickManager.DebugSetTicksGame(0);

            float points = StorytellerUtility.DefaultThreatPointsNow(target);

            // wealth curve (2400) + 3 * colonist curve (140) = 2820, * threatScale(1) * adaptation(0.7 at day 0) * pointsFactor(1 at day 0)
            Assert.Equal(2820f * 0.7f, points, 1);
        }

        [Fact]
        public void DefaultThreatPointsNow_wounded_pawn_counts_less()
        {
            NewStoryteller();
            CivilizationTarget healthyTarget = NewTarget(Find.Storyteller, pawnCount: 0, wealth: 400000f);
            healthyTarget.pawns.Add(NewHuman("Healthy"));

            CivilizationTarget woundedTarget = new CivilizationTarget { PlayerWealthForStoryteller = 400000f };
            Pawn wounded = NewHuman("Wounded");
            Hit(wounded, "Cut", 15f, "left leg");
            Assert.True(wounded.health.summaryHealth.SummaryHealthPercent < 1f);
            woundedTarget.pawns.Add(wounded);

            Find.TickManager.DebugSetTicksGame(0);
            float healthyPoints = StorytellerUtility.DefaultThreatPointsNow(healthyTarget);
            float woundedPoints = StorytellerUtility.DefaultThreatPointsNow(woundedTarget);

            Assert.True(woundedPoints < healthyPoints);
        }

        [Fact]
        public void DefaultThreatPointsNow_dead_pawn_counts_zero()
        {
            NewStoryteller();
            CivilizationTarget aliveTarget = new CivilizationTarget { PlayerWealthForStoryteller = 400000f };
            aliveTarget.pawns.Add(NewHuman("Alive"));

            CivilizationTarget deadTarget = new CivilizationTarget { PlayerWealthForStoryteller = 400000f };
            Pawn dead = NewHuman("Dead");
            Hit(dead, "Cut", 10f, "brain");
            Assert.True(dead.Dead);
            deadTarget.pawns.Add(dead);

            Find.TickManager.DebugSetTicksGame(0);
            float alivePoints = StorytellerUtility.DefaultThreatPointsNow(aliveTarget);
            float deadPoints = StorytellerUtility.DefaultThreatPointsNow(deadTarget);

            // A dead pawn contributes nothing, so the dead-pawn target should score the same as an empty roster.
            CivilizationTarget emptyTarget = new CivilizationTarget { PlayerWealthForStoryteller = 400000f };
            float emptyPoints = StorytellerUtility.DefaultThreatPointsNow(emptyTarget);
            Assert.Equal(emptyPoints, deadPoints, 2);
            Assert.True(deadPoints < alivePoints);
        }

        [Fact]
        public void DefaultThreatPointsNow_threatScale_scales_points()
        {
            NewStoryteller("Cassandra_Classic", "Medium");
            CivilizationTarget mediumTarget = NewTarget(Find.Storyteller, pawnCount: 3, wealth: 700000f);
            Find.TickManager.DebugSetTicksGame(0);
            float mediumPoints = StorytellerUtility.DefaultThreatPointsNow(mediumTarget);

            NewStoryteller("Cassandra_Classic", "Rough");
            CivilizationTarget roughTarget = NewTarget(Find.Storyteller, pawnCount: 3, wealth: 700000f);
            Find.TickManager.DebugSetTicksGame(0);
            float roughPoints = StorytellerUtility.DefaultThreatPointsNow(roughTarget);

            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            DifficultyDef rough = DefDatabase<DifficultyDef>.GetNamed("Rough");
            Assert.Equal(mediumPoints * (rough.threatScale / medium.threatScale), roughPoints, 1);
        }

        [Fact]
        public void DefaultThreatPointsNow_clamps_at_max()
        {
            NewStoryteller("Cassandra_Classic", "Extreme");
            CivilizationTarget target = NewTarget(Find.Storyteller, pawnCount: 5, wealth: 2000000f);
            for (int i = 0; i < 3600; i++) Find.Storyteller.adaptation.AdaptationTick(Find.Storyteller.difficulty); // push adaptation to its ceiling
            Find.TickManager.DebugSetTicksGame(0);

            float points = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.Equal(StorytellerUtility.MaxThreatPoints, points);
        }

        // ---- adaptation ----

        [Fact]
        public void Adaptation_factor_in_expected_range_and_grows_with_quiet_days()
        {
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            var adaptation = new StoryWatcher_Adaptation();

            float atZero = adaptation.TotalThreatPointsFactor(medium);
            Assert.Equal(0.7f, atZero, 3);

            for (int i = 0; i < 600; i++) adaptation.AdaptationTick(medium); // 10 days
            Assert.Equal(10f, adaptation.AdaptDays, 1);
            float atTenDays = adaptation.TotalThreatPointsFactor(medium);
            Assert.Equal(0.9f, atTenDays, 2);
            Assert.True(atTenDays > atZero);

            for (int i = 0; i < 1200; i++) adaptation.AdaptationTick(medium); // + 20 more days = 30 total
            Assert.Equal(30f, adaptation.AdaptDays, 1);
            float atThirtyDays = adaptation.TotalThreatPointsFactor(medium);
            Assert.Equal(1.0f, atThirtyDays, 2);
            Assert.True(atThirtyDays > atTenDays);

            for (int i = 0; i < 1800; i++) adaptation.AdaptationTick(medium); // + 30 more days = 60 total
            float atSixtyDays = adaptation.TotalThreatPointsFactor(medium);
            Assert.Equal(1.2f, atSixtyDays, 2);
            Assert.True(atSixtyDays > atThirtyDays);
        }

        // ---- CanFireNow gating ----

        [Fact]
        public void CanFireNow_gates_on_earliestDay()
        {
            IncidentDef def = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder), earliestDay: 10);
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 0f };

            Find.TickManager.DebugSetTicksGame(0);
            Assert.False(def.Worker.CanFireNow(parms));

            Find.TickManager.DebugSetTicksGame(10 * GenDate.TicksPerDay);
            Assert.True(def.Worker.CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_gates_on_minPopulation()
        {
            IncidentDef def = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder), minPopulation: 3);
            var target = new CivilizationTarget();
            var parms = new IncidentParms { target = target, points = 0f };

            Assert.False(def.Worker.CanFireNow(parms));

            target.pawns.Add(NewHuman());
            target.pawns.Add(NewHuman());
            target.pawns.Add(NewHuman());
            Assert.True(def.Worker.CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_gates_on_minThreatPoints()
        {
            IncidentDef def = MakeTestIncidentDef(IncidentCategoryDefOf.ThreatBig, typeof(IncidentWorker_ThreatEvent), minThreatPoints: 100f);
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 50f };

            Assert.False(def.Worker.CanFireNow(parms));

            parms.points = 150f;
            Assert.True(def.Worker.CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_gates_on_minRefireDays_unless_forced()
        {
            IncidentDef def = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder), minRefireDays: 5f);
            var parms = new IncidentParms { target = new CivilizationTarget(), points = 0f };

            Find.TickManager.DebugSetTicksGame(0);
            Assert.True(def.Worker.TryExecute(parms));
            Assert.False(def.Worker.CanFireNow(parms));

            parms.forced = true;
            Assert.True(def.Worker.CanFireNow(parms));

            parms.forced = false;
            Find.TickManager.DebugSetTicksGame(5 * GenDate.TicksPerDay);
            Assert.True(def.Worker.CanFireNow(parms));
        }

        // ---- comps ----

        [Fact]
        public void OnOffCycle_fires_only_in_on_phase_and_about_numIncidentsRange_per_cycle()
        {
            IncidentDef testIncident = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder));
            var props = new StorytellerCompProperties_OnOffCycle { onDays = 4f, offDays = 2f, minSpacingDays = 0.5f, numIncidentsRange = new FloatRange(1f, 1f), incident = testIncident };
            var comp = new StorytellerComp_OnOffCycle { props = props };
            var target = new CivilizationTarget();
            Rand.Current = new RandomStream(999);

            float cycleLength = props.onDays + props.offDays;
            int cycles = 40;
            int totalIntervals = (int)(cycles * cycleLength * 60);
            int fired = 0;
            int firedDuringOff = 0;

            for (int i = 1; i <= totalIntervals; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
                float daysPassed = Find.TickManager.TicksGame / (float)GenDate.TicksPerDay;
                bool offPhase = daysPassed % cycleLength >= props.onDays;
                int count = comp.MakeIntervalIncidents(target).Count();
                fired += count;
                if (offPhase && count > 0) firedDuringOff++;
            }

            Assert.Equal(0, firedDuringOff);
            float perCycle = (float)fired / cycles;
            Assert.InRange(perCycle, 0.4f, 1.8f);
        }

        [Fact]
        public void RandomMain_fires_at_about_expected_rate_and_picks_categories_by_weight()
        {
            var rateProps = new StorytellerCompProperties_RandomMain
            {
                mtbDays = 2f,
                categoryWeights = new List<IncidentCategoryEntry> { new IncidentCategoryEntry { category = IncidentCategoryDefOf.Misc, weight = 1f } },
            };
            var rateComp = new StorytellerComp_RandomMain { props = rateProps };
            var target = NewTarget(pawnCount: 3);
            Rand.Current = new RandomStream(42);

            int days = 200;
            int fired = 0;
            for (int i = 1; i <= days * 60; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
                fired += rateComp.MakeIntervalIncidents(target).Count();
            }

            float expectedProbabilityPerCheck = RandomStream.MTBEventProbability(rateProps.mtbDays, GenDate.TicksPerDay, global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
            float expectedPerDay = expectedProbabilityPerCheck * 60f;
            float actualPerDay = (float)fired / days;
            Assert.InRange(actualPerDay, expectedPerDay * 0.5f, expectedPerDay * 1.5f);

            // Category weighting: heavily favour Misc over DiseaseHuman and confirm the pick rate reflects it.
            var weightProps = new StorytellerCompProperties_RandomMain
            {
                mtbDays = 0.0001f,
                categoryWeights = new List<IncidentCategoryEntry>
                {
                    new IncidentCategoryEntry { category = IncidentCategoryDefOf.Misc, weight = 95f },
                    new IncidentCategoryEntry { category = IncidentCategoryDefOf.DiseaseHuman, weight = 5f },
                },
            };
            var weightComp = new StorytellerComp_RandomMain { props = weightProps };
            Find.TickManager.DebugSetTicksGame(0);
            Rand.Current = new RandomStream(7);

            int miscPicks = 0, diseasePicks = 0;
            for (int i = 1; i <= 2000; i++)
            {
                Find.TickManager.DebugSetTicksGame(i * global::SimWorld.Director.Storyteller.IncidentCycleLengthTicks);
                foreach (FiringIncident fi in weightComp.MakeIntervalIncidents(target))
                {
                    if (fi.def.category == IncidentCategoryDefOf.Misc) miscPicks++;
                    else if (fi.def.category == IncidentCategoryDefOf.DiseaseHuman) diseasePicks++;
                }
            }

            Assert.True(miscPicks > diseasePicks * 5, $"expected Misc ({miscPicks}) to dominate DiseaseHuman ({diseasePicks}) picks");
        }

        [Fact]
        public void ClassicIntro_fires_first_threat_at_its_day()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            IncidentDef testIncident = MakeTestIncidentDef(IncidentCategoryDefOf.ThreatBig, typeof(IncidentWorker_ThreatEvent));
            var props = new StorytellerCompProperties_ClassicIntro { incident = testIncident, day = 4 };
            var comp = new StorytellerComp_ClassicIntro { props = props };
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 3);

            Find.TickManager.DebugSetTicksGame(0);
            Assert.Empty(comp.MakeIntervalIncidents(target));

            Find.TickManager.DebugSetTicksGame(3 * GenDate.TicksPerDay);
            Assert.Empty(comp.MakeIntervalIncidents(target));

            Find.TickManager.DebugSetTicksGame(4 * GenDate.TicksPerDay);
            List<FiringIncident> fired = comp.MakeIntervalIncidents(target).ToList();
            Assert.Single(fired);
            Assert.Same(testIncident, fired[0].def);

            Assert.True(storyteller.TryFire(fired[0]));

            Find.TickManager.DebugSetTicksGame(20 * GenDate.TicksPerDay);
            Assert.Empty(comp.MakeIntervalIncidents(target));
        }

        [Fact]
        public void Disease_Flu_gives_one_pawn_flu_and_does_not_target_a_pawn_who_already_has_it()
        {
            IncidentDef fluDef = DefDatabase<IncidentDef>.GetNamed("Disease_Flu");
            HediffDef flu = DefDatabase<HediffDef>.GetNamed("Flu");
            var target = new CivilizationTarget();
            Pawn already = NewHuman("Already");
            already.health.AddHediff(flu);
            Pawn healthy1 = NewHuman("H1");
            Pawn healthy2 = NewHuman("H2");
            target.pawns.Add(already);
            target.pawns.Add(healthy1);
            target.pawns.Add(healthy2);

            var parms = new IncidentParms { target = target, points = 0f };
            Assert.True(fluDef.Worker.CanFireNow(parms));
            Assert.True(fluDef.Worker.TryExecute(parms));

            int newlyInfected = new[] { healthy1, healthy2 }.Count(p => p.HasHediff(flu));
            Assert.Equal(1, newlyInfected);
            Assert.True(already.HasHediff(flu));
        }

        [Fact]
        public void IncidentQueue_fires_at_its_tick()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 3);
            IncidentDef testIncident = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder));
            var fi = new FiringIncident(testIncident, null, new IncidentParms { target = target, points = 0f });

            Find.TickManager.DebugSetTicksGame(1000);
            storyteller.incidentQueue.Add(fi, fireTick: 5000);

            Find.TickManager.DebugSetTicksGame(4000);
            storyteller.incidentQueue.IncidentQueueTick();
            Assert.False(target.StoryState.HasFired(testIncident));
            Assert.Single(storyteller.incidentQueue.Queued);

            Find.TickManager.DebugSetTicksGame(5000);
            storyteller.incidentQueue.IncidentQueueTick();
            Assert.True(target.StoryState.HasFired(testIncident));
            Assert.Empty(storyteller.incidentQueue.Queued);
        }

        [Fact]
        public void StorytellerTick_over_many_days_records_chronicle_entries()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller("Randy_Random", "Medium");
            CivilizationTarget target = NewTarget(storyteller, pawnCount: 5, wealth: 200000f);
            Find.TickManager.DebugSetTicksGame(0);

            AdvanceIntervals(storyteller, 60 * 30); // 30 days

            Assert.NotEmpty(storyteller.Chronicle);
            Assert.All(storyteller.Chronicle, e => Assert.True(e.tick > 0 && e.incidentDefName.Length > 0));
        }

        // ---- scribe round trips ----

        [Fact]
        public void Scribe_round_trip_of_story_state()
        {
            IncidentDef raidDef = DefDatabase<IncidentDef>.GetNamed("RaidEnemy");
            Find.TickManager.DebugSetTicksGame(12345);
            var state = new StoryState();
            var target = new CivilizationTarget();
            state.Notify_IncidentFired(new FiringIncident(raidDef, null, new IncidentParms { target = target, points = 100f }));

            string xml = Scribe.SaveToString(state, "state");
            StoryState loaded = Scribe.Load<StoryState>(xml, "state", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.True(loaded.HasFired(raidDef));
            Assert.Equal(12345, loaded.LastFireTick(raidDef));
            Assert.Equal(12345, loaded.LastThreatBigTick);
        }

        [Fact]
        public void Scribe_round_trip_of_adaptation()
        {
            DifficultyDef medium = DefDatabase<DifficultyDef>.GetNamed("Medium");
            var adaptation = new StoryWatcher_Adaptation();
            for (int i = 0; i < 600; i++) adaptation.AdaptationTick(medium);

            string xml = Scribe.SaveToString(adaptation, "adaptation");
            StoryWatcher_Adaptation loaded = Scribe.Load<StoryWatcher_Adaptation>(xml, "adaptation");

            Assert.Equal(adaptation.AdaptDays, loaded.AdaptDays, 3);
            Assert.Equal(adaptation.TotalThreatPointsFactor(medium), loaded.TotalThreatPointsFactor(medium), 3);
        }

        private sealed class QueueRoundTripRoot : IExposable
        {
            public CivilizationTarget target = null!;
            public IncidentQueue queue = null!;

            public void ExposeData()
            {
                CivilizationTarget? t = target;
                Scribe_Deep.Look(ref t, "target");
                target = t!;
                IncidentQueue? q = queue;
                Scribe_Deep.Look(ref q, "queue");
                queue = q!;
            }
        }

        [Fact]
        public void Scribe_round_trip_of_incident_queue()
        {
            var target = new CivilizationTarget();
            IncidentDef testIncident = DefDatabase<IncidentDef>.GetNamed("RaidEnemy");
            var root = new QueueRoundTripRoot { target = target, queue = new IncidentQueue() };
            root.queue.Add(new FiringIncident(testIncident, null, new IncidentParms { target = target, points = 250f, forced = true }), fireTick: 9999);

            string xml = Scribe.SaveToString(root, "root");
            QueueRoundTripRoot loaded = Scribe.Load<QueueRoundTripRoot>(xml, "root", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Single(loaded.queue.Queued);
            QueuedIncident q = loaded.queue.Queued[0];
            Assert.Same(testIncident, q.def);
            Assert.Equal(250f, q.points);
            Assert.True(q.forced);
            Assert.Equal(9999, q.fireTick);
            Assert.Same(loaded.target, q.target);
        }

        // ---- quests.moments: curated history ----

        [Fact]
        public void First_occurrence_of_a_free_form_category_is_a_moment_but_repeats_are_not()
        {
            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;

            storyteller.RecordChronicle("Birth: Ada joins family 1.");
            storyteller.RecordChronicle("Birth: Bea joins family 2.");
            storyteller.RecordChronicle("Birth: Cid joins family 3.");

            Assert.Equal(3, storyteller.Chronicle.Count);
            Assert.Single(storyteller.Moments);
            Assert.True(storyteller.Chronicle[0].isMoment);
            Assert.False(storyteller.Chronicle[1].isMoment);
            Assert.False(storyteller.Chronicle[2].isMoment);
        }

        [Fact]
        public void Distinct_free_form_categories_each_earn_their_own_first_moment()
        {
            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;

            storyteller.RecordChronicle("Birth: Ada joins family 1.");
            storyteller.RecordChronicle("Edict issued: Hunter's Mandate.");
            storyteller.RecordChronicle("Edict rescinded: Hunter's Mandate.");

            Assert.Equal(3, storyteller.Moments.Count);
        }

        [Fact]
        public void Every_era_transition_is_a_moment_not_only_the_first()
        {
            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;

            storyteller.RecordChronicle("Era reached: Sticks and Stones.");
            storyteller.RecordChronicle("Era reached: Agrarian, after the Sticks and Stones.");
            storyteller.RecordChronicle("Era reached: Bronze, after the Agrarian.");

            Assert.Equal(3, storyteller.Moments.Count);
            Assert.All(storyteller.Chronicle, e => Assert.True(e.isMoment));
        }

        [Fact]
        public void First_firing_of_an_incident_is_a_moment_but_a_repeat_firing_of_the_same_def_is_not()
        {
            global::SimWorld.Director.Storyteller storyteller = NewStoryteller();
            CivilizationTarget target = NewTarget(storyteller);
            IncidentDef testIncident = MakeTestIncidentDef(IncidentCategoryDefOf.Misc, typeof(IncidentWorker_Placeholder));

            var fi1 = new FiringIncident(testIncident, null, new IncidentParms { target = target, points = 0f });
            var fi2 = new FiringIncident(testIncident, null, new IncidentParms { target = target, points = 0f });

            Assert.True(storyteller.TryFire(fi1));
            Assert.True(storyteller.TryFire(fi2));

            Assert.Equal(1, storyteller.Moments.Count(e => e.incidentDefName == testIncident.defName));
        }

        [Fact]
        public void First_death_by_a_cause_and_a_new_longevity_record_are_each_moments_a_routine_death_is_not()
        {
            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;

            Pawn young = NewHuman("Young");
            young.ageTracker.DebugSetAge(40f);
            storyteller.RecordDeath(young, DeathCause.Age); // first death ever (and first "Death:Age") -> moment; also the first longevity record

            Pawn older = NewHuman("Older");
            older.ageTracker.DebugSetAge(70f);
            storyteller.RecordDeath(older, DeathCause.Age); // not a "first" any more, but 70 beats 40 -> a new record

            Pawn younger = NewHuman("Younger");
            younger.ageTracker.DebugSetAge(50f);
            storyteller.RecordDeath(younger, DeathCause.Age); // neither a first nor a record (50 < 70) -> routine

            Assert.Equal(3, storyteller.Chronicle.Count(e => e.incidentDefName == "Death"));
            Assert.Equal(2, storyteller.Moments.Count);
            Assert.True(young.tier.ChronicleNamed);
            Assert.True(older.tier.ChronicleNamed);
            Assert.False(younger.tier.ChronicleNamed);
        }

        [Fact]
        public void A_routine_century_of_chronicle_activity_produces_far_fewer_than_a_hundred_moments()
        {
            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;
            var rand = new RandomStream(999);

            // A century of routine demographic/political noise: a birth every year (all distinct text, the
            // way FamilyManager actually writes them), an edict toggling on and off every few years, and a
            // death every year at a mixed, mostly-non-record age — the kind of activity that would produce
            // hundreds of chronicle lines but should not produce hundreds of moments.
            for (int year = 0; year < 100; year++)
            {
                storyteller.RecordChronicle("Birth: Citizen" + year + " joins family " + (year % 20) + ".");
                if (year % 5 == 0) storyteller.RecordChronicle("Edict issued: SomeEdict.");
                if (year % 5 == 2) storyteller.RecordChronicle("Edict rescinded: SomeEdict.");

                Pawn p = NewHuman("Deceased" + year);
                p.ageTracker.DebugSetAge(rand.Range(40f, 90f));
                storyteller.RecordDeath(p, DeathCause.Age);
            }

            Assert.True(storyteller.Chronicle.Count(e => e.incidentDefName.StartsWith("Birth:")) == 100,
                "sanity check: a century of routine births should still all land in the chronicle");
            Assert.True(storyteller.Moments.Count < 20,
                "expected a small, bounded number of moments from a routine century; got " + storyteller.Moments.Count);
        }

        [Fact]
        public void Scribe_round_trip_of_moment_curator()
        {
            var curator = new MomentCurator();
            var birth = new ChronicleEntry(100, "Birth: Ada joins family 1.", "", 0f);
            curator.Consider(birth, "Birth");
            var death = new ChronicleEntry(200, "Death", "Old Bea", 0f) { deathCause = DeathCause.Age };
            curator.ConsiderDeathRecord(death, 90f);

            string xml = Scribe.SaveToString(curator, "moments");
            MomentCurator loaded = Scribe.Load<MomentCurator>(xml, "moments", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(2, loaded.Moments.Count);
            Assert.Contains(loaded.Moments, e => e.incidentDefName.StartsWith("Birth:") && e.isMoment);
            Assert.Contains(loaded.Moments, e => e.incidentDefName == "Death" && e.isMoment && e.deathCause == DeathCause.Age);

            // The curated *state* round-tripped too, not merely the entries already in Moments: a repeat of an
            // already-seen category is no longer a "first" on the loaded curator, and a death that does not
            // beat the loaded longevity record is not treated as a new one either.
            var repeatBirth = new ChronicleEntry(300, "Birth: Cid joins family 2.", "", 0f);
            loaded.Consider(repeatBirth, "Birth");
            Assert.False(repeatBirth.isMoment);

            var lowerAgeDeath = new ChronicleEntry(400, "Death", "Young Cal", 0f) { deathCause = DeathCause.Age };
            loaded.ConsiderDeathRecord(lowerAgeDeath, 50f);
            Assert.False(lowerAgeDeath.isMoment);
        }
    }
}
