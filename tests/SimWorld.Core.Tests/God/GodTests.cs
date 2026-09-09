using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>Edicts, the slot budget, the edict think-tree tier and the god-view rollup (system 12: the
    /// god layer, <c>docs/spec/simworld-spec.md</c> §10).</summary>
    public class GodTests : ContentTestBase
    {
        public GodTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Test")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnRock(CoreMap map, IntVec3 cell, string defName = "Sandstone")
        {
            Thing rock = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(rock, cell, map);
            return rock;
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static Thing SpawnFood(CoreMap map, IntVec3 cell, string defName = "RawPotatoes")
        {
            Thing food = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(food, cell, map);
            return food;
        }

        /// <summary>An ad-hoc edict, never registered in the DefDatabase, for tests that need a specific
        /// prioritizedWork/requiredEra combination the shipped content doesn't happen to carry (the same way
        /// <c>BuildingTests</c> builds a throwaway <c>ThingDef</c> rather than adding one to content).</summary>
        private static EdictDef TestEdict(string defName, WorkTypeDef workType, EraDef? requiredEra = null, EraDef? obsoleteEra = null) =>
            new EdictDef
            {
                defName = defName,
                requiredEra = requiredEra,
                obsoleteEra = obsoleteEra,
                prioritizedWork = new List<WorkTypeDef> { workType },
            };

        private static BodyPartRecord Part(Pawn p, string label) => p.RaceProps.body!.GetPartByLabel(label)!;

        /// <summary>Beats a pawn down to well below <see cref="TieringTuning.StatisticalHealthFractionMax"/>'s
        /// floor (0.55) without killing or requiring further setup — see the module's own report for how this
        /// figure (~0.27) was measured.</summary>
        private static void InflictSevereInjuries(Pawn p)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamed("Cut");
            cut.Worker.Apply(new DamageInfo(cut, 50f, hitPart: Part(p, "left leg")), p);
            cut.Worker.Apply(new DamageInfo(cut, 50f, hitPart: Part(p, "right leg")), p);
            cut.Worker.Apply(new DamageInfo(cut, 50f, hitPart: Part(p, "left arm")), p);
            cut.Worker.Apply(new DamageInfo(cut, 50f, hitPart: Part(p, "right arm")), p);
            cut.Worker.Apply(new DamageInfo(cut, 20f, hitPart: Part(p, "torso")), p);
        }

        /// <summary>A bare <see cref="Settlement"/> with no world behind it — the same minimal-construction
        /// shape <c>SettlementTests.PlainSettlement</c> uses for tests that only need the entity itself, not a
        /// generated world to place it on.</summary>
        private static Settlement PlainSettlement(int tile) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "PlainSettlement" + tile, 0);

        private static void FinishEra(EraDef era)
        {
            foreach (ResearchProjectDef project in era.Projects)
            {
                Find.ResearchManager.FinishProject(project);
            }
        }

        // ---- content ----

        [Fact]
        public void God_content_loads_with_no_errors()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(DefDatabase<EdictDef>.DefCount >= 5, "expected at least 5 seed edicts.");
            Assert.True(DefDatabase<global::SimWorld.Thoughts.ThoughtDef>.DefCount >= 5);

            EdictDef greatWorks = DefDatabase<EdictDef>.GetNamed("GreatWorksMandate");
            Assert.IsType<EdictWorker_ExemptMinors>(greatWorks.Worker);
            Assert.NotEmpty(greatWorks.prioritizedWork);
            Assert.NotNull(greatWorks.moodThought);
            Assert.IsType<ThoughtWorker_UnderEdict>(greatWorks.moodThought!.Worker);
            Assert.NotNull(greatWorks.researchFocus);
            Assert.NotNull(greatWorks.requiredEra);
        }

        // ---- era gating ----

        [Fact]
        public void CanActivate_refuses_an_edict_above_the_current_era_and_allows_it_once_reached()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            EdictDef breakTheSod = DefDatabase<EdictDef>.GetNamed("BreakTheSod");

            Assert.Same(sticksAndStones, Find.ResearchManager.CurrentEra);
            Assert.False(Find.God.CanActivate(breakTheSod));
            Assert.False(Find.God.Activate(breakTheSod));
            Assert.Empty(Find.God.ActiveEdicts);

            FinishEra(sticksAndStones);
            FinishEra(agrarian);

            Assert.True(Find.ResearchManager.CurrentEra!.order >= agrarian.order);
            Assert.True(Find.God.CanActivate(breakTheSod));
            Assert.True(Find.God.Activate(breakTheSod));
            Assert.True(Find.God.IsActive(breakTheSod));
        }

        [Fact]
        public void An_edict_with_no_requiredEra_can_activate_from_the_start()
        {
            EdictDef hunters = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            Assert.NotNull(hunters.requiredEra); // HuntersMandate itself does gate on SticksAndStones...
            // ...which is the era every fresh civilization already starts in, so it activates immediately.
            Assert.True(Find.God.CanActivate(hunters));
            Assert.True(Find.God.Activate(hunters));
        }

        [Fact]
        public void Activating_an_edict_sets_its_research_focus_and_records_the_Chronicle()
        {
            // An ad-hoc edict (see TestEdict's own doc) rather than a real one keeps this test's setup
            // independent of any particular era's own research graph: it only needs an unfinished project to
            // point at, not a whole era finished first.
            ResearchProjectDef focus = DefDatabase<ResearchProjectDef>.GetNamed("Fire");
            var edict = new EdictDef { defName = "TestResearchFocusEdict", researchFocus = focus };

            int chronicleCountBefore = Find.Storyteller.Chronicle.Count;
            Assert.False(Find.ResearchManager.IsFinished(focus));
            Assert.NotEqual(focus, Find.ResearchManager.CurrentProj);

            Assert.True(Find.God.Activate(edict));

            Assert.Equal(focus, Find.ResearchManager.CurrentProj);
            Assert.True(Find.Storyteller.Chronicle.Count > chronicleCountBefore);
            Assert.Contains(edict.LabelCap, Find.Storyteller.Chronicle[^1].incidentDefName);

            int chronicleCountAfterActivate = Find.Storyteller.Chronicle.Count;
            Assert.True(Find.God.Deactivate(edict));
            Assert.True(Find.Storyteller.Chronicle.Count > chronicleCountAfterActivate);
        }

        // ---- slot budget ----

        [Fact]
        public void Slot_budget_refuses_the_edict_past_the_cap()
        {
            var edicts = new List<EdictDef>();
            for (int i = 0; i < GodTuning.MaxActiveEdicts + 1; i++)
            {
                edicts.Add(TestEdict("TestSlotEdict" + i, WorkTypeDefOf.Hauling));
            }

            for (int i = 0; i < GodTuning.MaxActiveEdicts; i++)
            {
                Assert.True(Find.God.CanActivate(edicts[i]));
                Assert.True(Find.God.Activate(edicts[i]));
            }
            Assert.Equal(GodTuning.MaxActiveEdicts, Find.God.ActiveEdicts.Count);

            EdictDef overCap = edicts[GodTuning.MaxActiveEdicts];
            Assert.False(Find.God.CanActivate(overCap));
            Assert.False(Find.God.Activate(overCap));
            Assert.Equal(GodTuning.MaxActiveEdicts, Find.God.ActiveEdicts.Count);
            Assert.DoesNotContain(overCap, Find.God.ActiveEdicts);

            // Freeing a slot by deactivating one lets the refused edict in.
            Find.God.Deactivate(edicts[0]);
            Assert.True(Find.God.CanActivate(overCap));
            Assert.True(Find.God.Activate(overCap));
        }

        [Fact]
        public void Activating_an_already_active_edict_is_a_no_op_refusal_not_a_second_slot()
        {
            EdictDef edict = TestEdict("TestDoubleActivate", WorkTypeDefOf.Hauling);
            Assert.True(Find.God.Activate(edict));
            Assert.False(Find.God.CanActivate(edict));
            Assert.False(Find.God.Activate(edict));
            Assert.Single(Find.God.ActiveEdicts);
        }

        // ---- think-tree ordering ----

        [Fact]
        public void Hungry_pawn_eats_instead_of_obeying_an_active_edict()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnFood(map, new IntVec3(9, 0, 9));
            SpawnRock(map, new IntVec3(1, 0, 0));
            pawn.needs.food!.CurLevelPercentage = 0.1f;

            Find.God.Activate(TestEdict("TestHungerEdict", WorkTypeDefOf.Mining));

            Job? job = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).Job;
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.Ingest, job!.def);
        }

        [Fact]
        public void A_queued_directed_order_still_outranks_an_active_edict()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnRock(map, new IntVec3(1, 0, 0));
            var ordered = new Job(JobDefOf.GotoWander, new IntVec3(5, 0, 5)) { playerForced = true };
            pawn.jobs.QueueJob(ordered);

            Find.God.Activate(TestEdict("TestOrderEdict", WorkTypeDefOf.Mining));

            Job? job = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).Job;
            Assert.NotNull(job);
            Assert.Equal(JobDefOf.GotoWander, job!.def);
            Assert.Equal(new IntVec3(5, 0, 5), job.GetTarget(TargetIndex.A).Cell);
        }

        [Fact]
        public void Idle_pawn_under_an_edict_does_the_edicts_work_instead_of_its_own_top_priority_work()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            // Both a Construction candidate (naturalPriority 800, tried first by routine work) and a Mining
            // candidate (naturalPriority 700) are available, so routine work's own priority order picks
            // Construction every time absent an edict.
            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, new IntVec3(5, 0, 5), map);
            SpawnStack(map, new IntVec3(1, 0, 1), "WoodLog", 5);
            SpawnRock(map, new IntVec3(2, 0, 0));

            Job? baseline = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).Job;
            Assert.NotNull(baseline);
            Assert.Equal(JobDefOf.HaulToBuildingSite, baseline!.def);

            Find.God.Activate(TestEdict("TestMiningOnlyEdict", WorkTypeDefOf.Mining));

            Job? underEdict = ThinkTreeDefOf.Humanlike.thinkRoot.TryIssueJobPackage(pawn).Job;
            Assert.NotNull(underEdict);
            Assert.Equal(JobDefOf.Mine, underEdict!.def);
        }

        [Fact]
        public void Deactivating_an_edict_leaves_work_priorities_exactly_as_they_were()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SpawnRock(map, new IntVec3(2, 0, 0));

            var before = new Dictionary<WorkTypeDef, int>();
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                before[workType] = pawn.workSettings.GetPriority(workType);
            }

            EdictDef edict = TestEdict("TestPriorityEdict", WorkTypeDefOf.Mining);
            Find.God.Activate(edict);
            RunTicks(2000, pawn); // long enough that, if the edict touched priorities, it would have by now.
            Find.God.Deactivate(edict);

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                Assert.Equal(before[workType], pawn.workSettings.GetPriority(workType));
            }
        }

        [Fact]
        public void An_edicts_mood_thought_applies_while_active_and_disappears_on_deactivation()
        {
            Pawn pawn = NewHuman();
            EdictDef edict = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            global::SimWorld.Thoughts.ThoughtDef moodThought = edict.moodThought!;
            var moodThoughts = new List<global::SimWorld.Thoughts.Thought>();

            pawn.needs.mood!.thoughts.situational.Recalculate();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(moodThoughts);
            Assert.DoesNotContain(moodThoughts, t => t.def == moodThought);

            Find.God.Activate(edict);
            pawn.needs.mood.thoughts.situational.Recalculate();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(moodThoughts);
            Assert.Contains(moodThoughts, t => t.def == moodThought);

            Find.God.Deactivate(edict);
            pawn.needs.mood.thoughts.situational.Recalculate();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(moodThoughts);
            Assert.DoesNotContain(moodThoughts, t => t.def == moodThought);
        }

        // ---- rollup ----

        [Fact]
        public void Rollup_aggregates_a_mixed_tier_population_correctly()
        {
            Pawn full = NewHuman("Full");
            Pawn interval = NewHuman("Interval");
            interval.tier.Notify_AttentionChanged(false); // demotes Full -> Interval (not significant by default)

            Pawn statistical = NewHuman("Statistical");
            statistical.tier.Notify_AttentionChanged(false);
            statistical.tier.DemoteToStatistical();

            Assert.Equal(PawnTier.Full, full.tier.Tier);
            Assert.Equal(PawnTier.Interval, interval.tier.Tier);
            Assert.Equal(PawnTier.Statistical, statistical.tier.Tier);

            var population = new List<Pawn> { full, interval, statistical };
            var rollup = new GodRollup();
            rollup.Recompute(population);

            Assert.Equal(3, rollup.TotalPopulation);
            Assert.Equal(1, rollup.FullCount);
            Assert.Equal(1, rollup.IntervalCount);
            Assert.Equal(1, rollup.StatisticalCount);
            Assert.InRange(rollup.MeanMood, 0f, 1f);
            Assert.InRange(rollup.MeanHealth, 0f, 1f);
            Assert.InRange(rollup.MeanFoodNeed, 0f, 1f);
            Assert.Equal(Find.ResearchManager.CurrentEra, rollup.CurrentEra);
            Assert.Equal(rollup.CurrentEra!.Progress, rollup.EraProgress);
        }

        [Fact]
        public void A_dead_pawn_does_not_count_toward_the_rollup()
        {
            Pawn alive = NewHuman("Alive");
            Pawn dead = NewHuman("Dead");
            dead.health.Kill(null, null);
            Assert.True(dead.Dead);

            var rollup = new GodRollup();
            rollup.Recompute(new List<Pawn> { alive, dead });

            Assert.Equal(1, rollup.TotalPopulation);
        }

        [Fact]
        public void Statistical_citizen_contributes_health_without_its_real_hediff_set_ever_being_read()
        {
            Pawn pawn = NewHuman();
            InflictSevereInjuries(pawn);
            float realHealthIfComputed = pawn.health.summaryHealth.SummaryHealthPercent;
            Assert.True(realHealthIfComputed < TieringTuning.StatisticalHealthFractionMin,
                $"test setup must produce real health below the Statistical sample floor; got {realHealthIfComputed}");

            pawn.tier.Notify_AttentionChanged(false);
            pawn.tier.DemoteToStatistical();
            Assert.Equal(PawnTier.Statistical, pawn.tier.Tier);

            var rollup = new GodRollup();
            rollup.Recompute(new List<Pawn> { pawn });

            // The rollup's one number for this lone pawn must be the cohort-sampled readout, landing inside
            // the Statistical sample band and matching the tracker's own sampled value exactly — never the
            // real (and, by construction, far lower) hediff-driven summary health.
            Assert.Equal(pawn.tier.SampledHealthFraction, rollup.MeanHealth);
            Assert.InRange(rollup.MeanHealth, TieringTuning.StatisticalHealthFractionMin, TieringTuning.StatisticalHealthFractionMax);
            Assert.NotEqual(realHealthIfComputed, rollup.MeanHealth);
        }

        [Fact]
        public void RecomputeIfNeeded_caches_until_the_cadence_elapses_or_Notify_Dirty_forces_it()
        {
            Pawn pawn = NewHuman();
            var population = new List<Pawn> { pawn };
            var rollup = new GodRollup();

            rollup.Recompute(population);
            float initialMood = rollup.MeanMood;

            pawn.needs.mood!.CurLevelPercentage = 0.2f;

            // Cadence not yet elapsed: still the cached (stale) value.
            rollup.RecomputeIfNeeded(population, cadenceTicks: 10_000);
            Assert.Equal(initialMood, rollup.MeanMood);

            // Notify_Dirty forces a recompute regardless of cadence.
            rollup.Notify_Dirty();
            rollup.RecomputeIfNeeded(population, cadenceTicks: 10_000);
            Assert.Equal(0.2f, rollup.MeanMood, 3);

            // Advancing the clock past the cadence (with no Notify_Dirty at all) must also force a fresh
            // recompute on its own — the cadence half of the brief, independent of the dirty-flag half above.
            // Advances only the clock (the pawn is never registered on a tick list), so its Need_Mood cannot
            // drift on its own — any change below is purely the rollup catching up, not the need decaying.
            rollup.Recompute(population); // re-baseline: pins lastRecomputeTick to "now".
            pawn.needs.mood!.CurLevelPercentage = 0.9f;
            const int cadence = 5;
            for (int i = 0; i <= cadence; i++) Find.TickManager.DoSingleTick();
            rollup.RecomputeIfNeeded(population, cadenceTicks: cadence);
            Assert.Equal(0.9f, rollup.MeanMood, 3);
        }

        // ---- rollup fed by a real Settlement ----

        [Fact]
        public void A_settlement_with_no_statistical_population_rolls_up_identically_to_its_bare_citizen_list()
        {
            Pawn full = NewHuman("Full");
            Pawn interval = NewHuman("Interval");
            interval.tier.Notify_AttentionChanged(false); // demotes Full -> Interval.

            Settlement settlement = PlainSettlement(1);
            settlement.AddCitizen(full);
            settlement.AddCitizen(interval);

            var fromSettlement = new GodRollup();
            fromSettlement.Recompute(settlement);

            var fromList = new GodRollup();
            fromList.Recompute(new List<Pawn> { full, interval });

            Assert.Equal(fromList.TotalPopulation, fromSettlement.TotalPopulation);
            Assert.Equal(fromList.FullCount, fromSettlement.FullCount);
            Assert.Equal(fromList.IntervalCount, fromSettlement.IntervalCount);
            Assert.Equal(0, fromSettlement.StatisticalCount);
            Assert.Equal(fromList.MeanMood, fromSettlement.MeanMood, 5);
            Assert.Equal(fromList.MeanHealth, fromSettlement.MeanHealth, 5);
            Assert.Equal(fromList.MeanFoodNeed, fromSettlement.MeanFoodNeed, 5);
            Assert.Equal(fromList.MeanIndustrySkill, fromSettlement.MeanIndustrySkill, 5);
        }

        [Fact]
        public void A_settlements_statistical_count_is_included_even_though_it_has_no_Pawn_objects()
        {
            Settlement settlement = PlainSettlement(2);
            settlement.AddCitizen(NewHuman("Founder"));
            settlement.AddStatisticalPeople(40_000);

            var rollup = new GodRollup();
            rollup.Recompute(settlement);

            // A civilization of 40,001 must not report as 1 just because only 1 has a live Pawn object.
            Assert.Equal(40_001, rollup.TotalPopulation);
            Assert.Equal(1, rollup.FullCount);
            Assert.Equal(40_000, rollup.StatisticalCount);
        }

        [Fact]
        public void A_settlements_statistical_cohort_is_folded_into_the_means_by_a_weighted_sample()
        {
            // Purely-statistical settlement: every mean must land inside that cohort's own sample band, with
            // no real Pawn contributing anything at all.
            Settlement settlement = PlainSettlement(3);
            settlement.AddStatisticalPeople(1_000);

            var rollup = new GodRollup();
            rollup.Recompute(settlement);

            Assert.InRange(rollup.MeanMood, TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax);
            Assert.InRange(rollup.MeanFoodNeed, TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax);
            Assert.InRange(rollup.MeanHealth, TieringTuning.StatisticalHealthFractionMin, TieringTuning.StatisticalHealthFractionMax);
            Assert.InRange(rollup.MeanIndustrySkill, GodTuning.StatisticalIndustrySkillSampleMin, GodTuning.StatisticalIndustrySkillSampleMax);

            // Weighted, not merely folded in: one real citizen pinned at the extreme (mood 0, far outside the
            // cohort's own [0.4, 0.85] sample band) alongside a cohort outnumbering it a million to one must
            // land the mean almost exactly on the cohort's own sample, not partway between the two — which is
            // what an unweighted "citizen value averaged with one cohort sample" bug would produce instead
            // (roughly [0.2, 0.425], overlapping this band's own floor). This bound holds regardless of which
            // exact value the deterministic sample happens to draw.
            Pawn extreme = NewHuman("Extreme");
            extreme.needs.mood!.CurLevelPercentage = 0f;
            Settlement dominated = PlainSettlement(4);
            dominated.AddCitizen(extreme);
            dominated.AddStatisticalPeople(1_000_000);

            var weighted = new GodRollup();
            weighted.Recompute(dominated);

            Assert.InRange(weighted.MeanMood, TieringTuning.StatisticalNeedSampleMin - 0.001f, TieringTuning.StatisticalNeedSampleMax + 0.001f);
        }

        [Fact]
        public void A_civilization_rollup_sums_population_across_multiple_settlements()
        {
            Settlement a = PlainSettlement(5);
            a.AddCitizen(NewHuman("A-Founder"));
            a.AddStatisticalPeople(1_000);

            Settlement b = PlainSettlement(6);
            b.AddCitizen(NewHuman("B-Founder"));
            b.AddCitizen(NewHuman("B-Founder-2"));
            b.AddStatisticalPeople(2_000);

            var rollup = new GodRollup();
            rollup.Recompute(new List<Settlement> { a, b });

            Assert.Equal(3_003, rollup.TotalPopulation);
            Assert.Equal(3, rollup.FullCount);
            Assert.Equal(3_000, rollup.StatisticalCount);
        }

        [Fact]
        public void Recomputing_over_a_large_statistical_settlement_enumerates_no_statistical_citizen()
        {
            Settlement small = PlainSettlement(8);
            small.AddStatisticalPeople(10);
            Settlement huge = PlainSettlement(9);
            huge.AddStatisticalPeople(100_000_000); // far past any per-citizen loop budget.

            var rollup = new GodRollup();
            rollup.Recompute(small); // warms up JIT before timing the real case.

            var stopwatch = Stopwatch.StartNew();
            rollup.Recompute(huge);
            stopwatch.Stop();

            Assert.Equal(100_000_000, rollup.TotalPopulation);
            Assert.Equal(100_000_000, rollup.StatisticalCount);

            // A per-person loop over 100,000,000 citizens — even doing nothing but a hash and a comparison
            // each — would not finish in this window; O(1)-per-settlement work does, with room to spare.
            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                $"Recompute over a 100M-statistical settlement took {stopwatch.ElapsedMilliseconds}ms; " +
                "expected O(1) per settlement, not O(StatisticalPopulation).");
        }

        // ---- Scribe round trip ----

        [Fact]
        public void GodManager_active_edicts_round_trip_through_Scribe()
        {
            EdictDef hunters = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            Find.God.Activate(hunters);
            RunTicks(GodTuning.GodTickIntervalTicks + 1);

            string xml = Scribe.SaveToString(Find.God, "god");
            GodManager loaded = Scribe.Load<GodManager>(xml, "god", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.True(loaded.IsActive(hunters));
            Assert.Single(loaded.ActiveEdicts);
            Assert.Same(hunters, loaded.ActiveEdicts[0]);
        }

        // ---- era transitions ----

        [Fact]
        public void An_edict_past_its_obsoleteEra_refuses_activation()
        {
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            EdictDef edict = TestEdict("TestObsoleteActivateEdict", WorkTypeDefOf.Hunting, obsoleteEra: agrarian);

            Assert.Same(sticksAndStones, Find.ResearchManager.CurrentEra);
            Assert.True(Find.God.CanActivate(edict)); // not yet reached Agrarian: still issuable.

            FinishEra(sticksAndStones);
            FinishEra(agrarian);

            Assert.Same(agrarian, Find.ResearchManager.CurrentEra);
            Assert.False(Find.God.CanActivate(edict));
            Assert.False(Find.God.Activate(edict));
        }

        [Fact]
        public void An_active_edict_auto_deactivates_with_its_own_chronicle_line_on_reaching_its_obsoleteEra()
        {
            // HuntersMandate ships with obsoleteEra=Agrarian (content: a standing hunt-everyone mandate is a
            // thing a civilization outgrows the moment it learns to farm) — real content, not an ad-hoc def,
            // so this also proves the content itself round-trips through the mechanism correctly.
            EdictDef hunters = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            Assert.NotNull(hunters.obsoleteEra);
            Assert.True(Find.God.Activate(hunters));

            int chronicleCountBefore = Find.Storyteller.Chronicle.Count;
            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");

            FinishEra(sticksAndStones);
            FinishEra(agrarian); // crosses into Agrarian: HuntersMandate's obsoleteEra.

            Assert.False(Find.God.IsActive(hunters));
            Assert.True(Find.Storyteller.Chronicle.Count > chronicleCountBefore);
            ChronicleEntry outgrown = Find.Storyteller.Chronicle.Single(e => e.incidentDefName.StartsWith("Edict outgrown"));
            Assert.Contains(hunters.LabelCap, outgrown.incidentDefName);
            Assert.Contains(agrarian.LabelCap, outgrown.incidentDefName);
        }

        [Fact]
        public void Reaching_an_era_writes_one_chronicle_line_naming_every_edict_it_newly_unlocks()
        {
            // GodManager, like every other Find service, is lazily constructed and only subscribes to
            // EraReached once something has asked for it — real play always has by the time research can
            // possibly finish an era (JobGiver_Edicts and ThoughtWorker_UnderEdict both read Find.God on
            // ordinary ticks), but a test that never touches Find.God at all must do so explicitly first.
            _ = Find.God;

            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            EdictDef breakTheSod = DefDatabase<EdictDef>.GetNamed("BreakTheSod");

            FinishEra(sticksAndStones);
            FinishEra(agrarian); // crosses into Agrarian: BreakTheSod.requiredEra.

            List<ChronicleEntry> unlockLines =
                Find.Storyteller.Chronicle.Where(e => e.incidentDefName.StartsWith("New edicts available")).ToList();

            // One line for this whole transition, not one per edict, even though this line is the only
            // "unlock" the transition produced.
            ChronicleEntry unlock = Assert.Single(unlockLines);
            Assert.Contains(breakTheSod.LabelCap, unlock.incidentDefName);
        }

        [Fact]
        public void No_unlock_chronicle_line_is_written_when_a_transition_unlocks_no_edict()
        {
            _ = Find.God; // force the lazily-constructed subscription — see the sibling test's comment.

            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            EraDef bronze = DefDatabase<EraDef>.GetNamed("Bronze");
            EraDef classical = DefDatabase<EraDef>.GetNamed("Classical"); // no edict requires Classical exactly.

            FinishEra(sticksAndStones);
            FinishEra(agrarian);
            FinishEra(bronze);
            int chronicleCountBeforeClassical = Find.Storyteller.Chronicle.Count;

            FinishEra(classical);

            Assert.Same(classical, Find.ResearchManager.CurrentEra);
            IEnumerable<ChronicleEntry> writtenForThisTransition = Find.Storyteller.Chronicle.Skip(chronicleCountBeforeClassical);
            Assert.DoesNotContain(writtenForThisTransition, e => e.incidentDefName.StartsWith("New edicts available"));
        }

        [Fact]
        public void A_loaded_GodManager_keeps_reacting_to_era_transitions_without_double_firing()
        {
            // Deliberately never touches Find.God: an independent GodManager, saved and loaded, isolates
            // exactly what this test needs to prove from any other manager instance's own reaction.
            var god = new GodManager();
            EdictDef hunters = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            Assert.True(god.Activate(hunters));

            string xml = Scribe.SaveToString(god, "god");
            GodManager loaded = Scribe.Load<GodManager>(xml, "god", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.True(loaded.IsActive(hunters));

            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            FinishEra(sticksAndStones);
            FinishEra(agrarian);

            // The round trip survived: `loaded` still reacted to the transition it was not alive to see fire.
            Assert.False(loaded.IsActive(hunters));

            // Constructing `loaded` (Scribe.Load's Activator.CreateInstance call) and its own ExposeData's
            // PostLoadInit branch both call EnsureSubscribedToEraTransitions on this ONE instance (see that
            // method's own doc) — were that call additive instead of idempotent, `loaded` alone would produce
            // two "outgrown" lines for the one transition. Exactly two total is the un-bugged answer: one from
            // `god` (still live and subscribed itself, constructed once) and one from `loaded` — proving
            // `loaded`'s double subscription attempt collapsed to a single live one, not that nothing fired.
            int outgrownLinesForHunters = Find.Storyteller.Chronicle.Count(e =>
                e.incidentDefName.StartsWith("Edict outgrown") && e.incidentDefName.Contains(hunters.LabelCap));
            Assert.Equal(2, outgrownLinesForHunters);
        }

        [Fact]
        public void Find_Reset_drops_the_old_GodManagers_subscription_so_it_never_reacts_again()
        {
            EdictDef hunters = DefDatabase<EdictDef>.GetNamed("HuntersMandate");
            GodManager stale = Find.God;
            Assert.True(stale.Activate(hunters));
            Assert.True(stale.IsActive(hunters));

            Find.Reset(); // drops this thread's GodManager and ResearchManager alike.

            EraDef sticksAndStones = DefDatabase<EraDef>.GetNamed("SticksAndStones");
            EraDef agrarian = DefDatabase<EraDef>.GetNamed("Agrarian");
            FinishEra(sticksAndStones); // against the fresh, post-reset Find.ResearchManager.
            FinishEra(agrarian);

            // `stale` is still subscribed to the ResearchManager instance that existed before Reset(), which
            // no longer receives any FinishProject calls at all — so it can never react again, not even
            // wrongly: its edict list sits exactly where it was left.
            Assert.True(stale.IsActive(hunters));

            // The fresh Find.God is a distinct instance, correctly subscribed to the fresh ResearchManager,
            // with no memory of what the pre-reset instance had active.
            Assert.NotSame(stale, Find.God);
            Assert.False(Find.God.IsActive(hunters));
        }
    }
}
