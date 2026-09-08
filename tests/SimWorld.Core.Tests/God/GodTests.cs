using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
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
        private static EdictDef TestEdict(string defName, WorkTypeDef workType, EraDef? requiredEra = null) =>
            new EdictDef
            {
                defName = defName,
                requiredEra = requiredEra,
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
    }
}
