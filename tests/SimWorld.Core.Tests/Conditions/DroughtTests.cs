using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Conditions
{
    /// <summary>
    /// The DROUGHT defect (system: conditions): a civilization-scale <see cref="GameCondition_Drought"/> that
    /// reaches both a watched map's plant growth (<see cref="Plant.TickLong"/>) and an unwatched settlement's
    /// off-map production (<see cref="SettlementSubsistence"/>), through one aggregate,
    /// <see cref="GameConditionManager.AggregateGrowthFactor"/> — and a new instrument,
    /// <see cref="ResourceImpactLedger"/>, that says exactly how much nutrition it cost, from a single run,
    /// with no control arm.
    ///
    /// <para/><b>Why the unwatched half is the point.</b> A watched map already has a growth-rate seam a
    /// heat wave or cold snap can (and does) plug into. Nothing before this reached the other side of the
    /// book: a settlement nobody has opened produces through <see cref="SettlementSubsistence"/> alone, and no
    /// standing condition touched its land factor. <see cref="An_unwatched_settlements_production_is_reduced_by_a_drought"/>
    /// is the assertion that would have been vacuously true before this change (nothing there to reduce).
    ///
    /// <para/><b>Everything tuned is a band, never a literal</b> (CLAUDE.md): <see cref="IncidentWorker_Drought.SeverityRange"/>
    /// and the content's own <c>durationDays</c> are RimWorld-less numbers this port owns outright, so what is
    /// asserted is that a drought actually cuts production by a measurable amount, never these figures
    /// themselves.
    /// </summary>
    [Collection("GlobalDefs")]
    public class DroughtTests : ContentTestBase
    {
        public DroughtTests(CoreContentFixture content) : base(content)
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
        }

        // ---- fixtures ----

        private static IncidentDef DroughtIncident => DefDatabase<IncidentDef>.GetNamed("Drought");

        private static CoreMap NewMap(int size = 12) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        /// <summary>A civilization with no tile grid at all — plenty for anything that only cares about the
        /// world-scale <see cref="GameConditionManager"/>, exactly as <c>GameConditionTests.NewWorld</c> uses
        /// one.</summary>
        private static CoreWorld BareWorld()
        {
            var world = new CoreWorld();
            Find.World = world;
            return world;
        }

        /// <summary>A real, generated world — needed only by the settlement-production tests, which read a
        /// tile's biome through <see cref="SettlementSubsistenceTuning.LandYieldFactor"/>.</summary>
        private static CoreWorld GeneratedWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Drought", 2, soloStart: true);
            Find.World = world;
            return world;
        }

        private static BiomeDef Biome(string defName) => DefDatabase<BiomeDef>.GetNamed(defName);

        private static int LandTile(CoreWorld world) =>
            Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);

        private static Settlement SettlementOn(CoreWorld world, string biomeName, string name = "Droughthome")
        {
            int tile = LandTile(world);
            world.grid.Tiles[tile].biome = Biome(biomeName);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            world.worldObjects.Add(settlement);
            return settlement;
        }

        private static Pawn OffMapCitizen(Settlement settlement, string name)
        {
            Pawn pawn = NewHuman(name);
            settlement.AddCitizen(pawn);
            return pawn;
        }

        /// <summary>Drives <paramref name="passes"/> gated production passes by hand, each at its own tick
        /// position — the same helper <c>SettlementSubsistenceTests.RunPasses</c> uses.</summary>
        private static int RunPasses(Settlement settlement, int passes, int firstPass = 1)
        {
            int produced = 0;
            for (int i = 0; i < passes; i++)
            {
                Find.TickManager.DebugSetTicksGame((firstPass + i) * SettlementSubsistenceTuning.IntervalTicks);
                produced += SettlementSubsistence.Run(settlement);
            }
            return produced;
        }

        /// <summary>A permanent drought at world scope, severity set by hand rather than rolled — for tests
        /// that want a fixed, known factor instead of the incident's own random one.</summary>
        private static GameCondition_Drought RegisterDrought(CoreWorld world, float severity)
        {
            var condition = (GameCondition_Drought)GameConditionMaker.MakeCondition(
                DroughtGameConditionDefOf.Drought, GameCondition.PermanentDuration);
            condition.Severity = severity;
            world.gameConditionManager.RegisterCondition(condition);
            return condition;
        }

        private static Plant SpawnPlant(CoreMap map, IntVec3 cell, string defName = "Plant_Potato")
        {
            var plant = (Plant)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            plant.Growth = 0.1f;
            GenSpawn.Spawn(plant, cell, map);
            return plant;
        }

        // ---- content ----

        [Fact]
        public void Content_loads_and_drought_is_wired_up()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(DroughtGameConditionDefOf.Drought);
            Assert.Same(typeof(GameCondition_Drought), DroughtGameConditionDefOf.Drought.conditionClass);
            Assert.IsType<IncidentWorker_Drought>(DroughtIncident.Worker);
            Assert.True(DroughtGameConditionDefOf.Drought.durationDays.min > 0f);
        }

        // ---- the headline claim: the unwatched half ----

        /// <summary>
        /// The whole reason DROUGHT was chosen over an earthquake or a flood: an off-map settlement's
        /// production, which nothing before this touched at all, measurably drops under a drought.
        /// </summary>
        [Fact]
        public void An_unwatched_settlements_production_is_reduced_by_a_drought()
        {
            int ProducedOver20Passes(bool underDrought)
            {
                CoreWorld world = GeneratedWorld("drought-econ-" + underDrought);
                Settlement settlement = SettlementOn(world, "TemperateForest");
                for (int i = 0; i < 10; i++) OffMapCitizen(settlement, "Hand" + i);
                if (underDrought) RegisterDrought(world, 0.4f);
                return RunPasses(settlement, 20);
            }

            int normal = ProducedOver20Passes(underDrought: false);
            int drought = ProducedOver20Passes(underDrought: true);

            Assert.True(normal > 0, "the fixture itself must produce food with no drought in force");
            Assert.True(drought < normal, "a drought left an unwatched settlement's production untouched");
        }

        // ---- the watched half ----

        [Fact]
        public void A_watched_maps_plant_growth_is_slowed_by_a_drought()
        {
            float GrowthThisTick(bool underDrought)
            {
                CoreWorld world = BareWorld();
                CoreMap map = NewMap();
                map.outdoorTemperature = 21f;
                Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12); // noon: full light
                if (underDrought) RegisterDrought(world, 0.4f);

                Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
                float before = plant.Growth;
                plant.TickLong();
                return plant.Growth - before;
            }

            float normal = GrowthThisTick(underDrought: false);
            float drought = GrowthThisTick(underDrought: true);

            Assert.True(normal > 0f, "the fixture itself must grow with no drought in force");
            Assert.True(drought < normal, "a drought left a watched map's plant growth untouched");
        }

        [Fact]
        public void A_droughts_growth_factor_scales_a_watched_maps_growth_by_exactly_that_factor()
        {
            CoreWorld world = BareWorld();
            CoreMap map = NewMap();
            map.outdoorTemperature = 21f;
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12);

            Plant control = SpawnPlant(map, new IntVec3(2, 0, 2));
            float before = control.Growth;
            control.TickLong();
            float growthAtOne = control.Growth - before;

            RegisterDrought(world, 0.37f);
            Plant underDrought = SpawnPlant(map, new IntVec3(3, 0, 3));
            float before2 = underDrought.Growth;
            underDrought.TickLong();
            float growthAtSeverity = underDrought.Growth - before2;

            Assert.Equal(growthAtOne * 0.37f, growthAtSeverity, 4);
        }

        // ---- the aggregate ----

        [Fact]
        public void Aggregate_growth_factor_is_one_with_nothing_active_and_the_product_with_several()
        {
            CoreWorld world = BareWorld();
            Assert.Equal(1f, world.gameConditionManager.AggregateGrowthFactor());

            RegisterDrought(world, 0.5f);
            Assert.Equal(0.5f, world.gameConditionManager.AggregateGrowthFactor(), 5);

            // Content never stacks two droughts against itself (IncidentWorker_Drought.CanFireNowSub refuses
            // to), but the aggregate itself is a plain product over whatever is active, and this fixture pins
            // that rather than assuming it.
            RegisterDrought(world, 0.4f);
            Assert.Equal(0.5f * 0.4f, world.gameConditionManager.AggregateGrowthFactor(), 5);
        }

        [Fact]
        public void Growth_factor_aggregates_across_the_map_and_the_world()
        {
            CoreWorld world = BareWorld();
            CoreMap map = NewMap();

            RegisterDrought(world, 0.5f);
            var mapCondition = (GameCondition_Drought)GameConditionMaker.MakeCondition(
                DroughtGameConditionDefOf.Drought, GameCondition.PermanentDuration);
            mapCondition.Severity = 0.5f;
            map.gameConditionManager.RegisterCondition(mapCondition);

            Assert.Equal(0.25f, map.gameConditionManager.AggregateGrowthFactor(), 5);
            Assert.Equal(0.5f, world.gameConditionManager.AggregateGrowthFactor(), 5);
        }

        // ---- the instrument: nutrition denied ----

        /// <summary>
        /// The self-check the brief calls for: fired live, the ledger reads a real number; fired ablated, it
        /// reads exactly zero — not approximately, because an ablated firing registers no condition at all, so
        /// nothing downstream of <see cref="GameConditionManager.AggregateGrowthFactor"/> ever sees anything
        /// but 1 and <see cref="ResourceImpactLedger.RecordNutritionDenied"/> is never called.
        /// </summary>
        [Fact]
        public void The_nutrition_denied_counter_is_nonzero_live_and_exactly_zero_ablated()
        {
            float NutritionDeniedAfter(bool ablated)
            {
                Find.Storyteller = new global::SimWorld.Director.Storyteller();
                CoreWorld world = GeneratedWorld("drought-ledger-" + ablated);
                Settlement settlement = SettlementOn(world, "TemperateForest");
                for (int i = 0; i < 10; i++) OffMapCitizen(settlement, "Hand" + i);

                if (ablated) Ablation.Disable("Drought");
                try
                {
                    bool fired = DroughtIncident.Worker.TryExecute(
                        new IncidentParms { target = new CivilizationTarget(), forced = true });
                    Assert.True(fired, "the incident must still report success whether ablated or not");
                }
                finally
                {
                    Ablation.Clear();
                }

                RunPasses(settlement, 20);
                return Find.Storyteller.resourceImpact.NutritionDeniedBy("Drought");
            }

            float ablatedResult = NutritionDeniedAfter(ablated: true);
            float liveResult = NutritionDeniedAfter(ablated: false);

            Assert.Equal(0f, ablatedResult);
            Assert.True(liveResult > 0f, "a live drought must deny measurable nutrition somewhere");
        }

        /// <summary>The counter also catches a watched map's denied growth, keyed under the same source, so
        /// one ledger reads correctly whichever half of the book the player happens to be looking at.</summary>
        [Fact]
        public void A_watched_maps_denied_growth_is_credited_to_the_same_ledger_source()
        {
            CoreWorld world = BareWorld();
            CoreMap map = NewMap();
            map.outdoorTemperature = 21f;
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12);
            RegisterDrought(world, 0.4f);

            Assert.Equal(0f, Find.Storyteller.resourceImpact.NutritionDeniedBy("Drought"));

            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            plant.TickLong();

            Assert.True(Find.Storyteller.resourceImpact.NutritionDeniedBy("Drought") > 0f);
        }

        /// <summary>The counter survives the condition that caused it ending — it must not be stored only on
        /// the <see cref="GameCondition_Drought"/> instance, which a save taken after the drought has broken
        /// would no longer have.</summary>
        [Fact]
        public void The_counter_survives_the_drought_condition_ending()
        {
            CoreWorld world = GeneratedWorld("drought-survive");
            Settlement settlement = SettlementOn(world, "TemperateForest");
            for (int i = 0; i < 10; i++) OffMapCitizen(settlement, "Hand" + i);

            GameCondition_Drought condition = RegisterDrought(world, 0.3f);
            RunPasses(settlement, 5);
            float deniedWhileActive = Find.Storyteller.resourceImpact.NutritionDeniedBy("Drought");
            Assert.True(deniedWhileActive > 0f);

            // End the condition by hand rather than waiting out a real duration.
            condition.duration = 1;
            Find.TickManager.DebugSetTicksGame(condition.startTick + 2);
            world.gameConditionManager.GameConditionManagerTick();
            Assert.Empty(world.gameConditionManager.ActiveConditions);
            Assert.Equal(1f, world.gameConditionManager.AggregateGrowthFactor());

            Assert.Equal(deniedWhileActive, Find.Storyteller.resourceImpact.NutritionDeniedBy("Drought"));
        }

        // ---- scribe ----

        [Fact]
        public void Scribe_round_trip_of_the_resource_impact_ledger()
        {
            var ledger = new ResourceImpactLedger();
            ledger.RecordNutritionDenied("Drought", 12.5f);
            ledger.RecordNutritionDenied("Drought", 7.5f);
            ledger.RecordNutritionDenied("SomethingElse", 3f);

            string xml = Scribe.SaveToString(ledger, "resourceImpact");
            ResourceImpactLedger loaded = Scribe.Load<ResourceImpactLedger>(xml, "resourceImpact", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(20f, loaded.NutritionDeniedBy("Drought"), 3);
            Assert.Equal(3f, loaded.NutritionDeniedBy("SomethingElse"), 3);
            Assert.Equal(0f, loaded.NutritionDeniedBy("Nonexistent"));
            Assert.Equal(23f, loaded.TotalNutritionDenied, 3);
        }

        [Fact]
        public void The_ledger_survives_a_save_of_the_whole_storyteller()
        {
            Find.Storyteller.def = DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic");
            Find.Storyteller.difficulty = DefDatabase<DifficultyDef>.GetNamed("Medium");
            Find.Storyteller.resourceImpact.RecordNutritionDenied("Drought", 42f);

            string xml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            var loaded = Scribe.Load<global::SimWorld.Director.Storyteller>(
                xml, "storyteller", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(42f, loaded.resourceImpact.NutritionDeniedBy("Drought"), 3);
        }

        /// <summary>Registered at map scope, exactly as <c>GameConditionTests.A_condition_round_trips_through_scribe_and_carries_on</c>
        /// round-trips <c>Flashstorm</c>/<c>HeatWave</c> — a <see cref="CoreMap"/> is a well-trodden Scribe
        /// path in this suite; a bare <see cref="CoreWorld"/> with no generated grid is not (its own
        /// <c>ExposeData</c> regenerates the grid from <c>info</c> on load, which a <see cref="BareWorld"/>
        /// never set).</summary>
        [Fact]
        public void A_drought_condition_round_trips_through_scribe_with_its_severity()
        {
            CoreMap map = NewMap();
            var condition = (GameCondition_Drought)GameConditionMaker.MakeCondition(
                DroughtGameConditionDefOf.Drought, GameCondition.PermanentDuration);
            condition.Severity = 0.42f;
            map.gameConditionManager.RegisterCondition(condition);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            var loadedCondition = Assert.IsType<GameCondition_Drought>(loaded.gameConditionManager.ActiveConditions[0]);
            Assert.Equal(0.42f, loadedCondition.Severity, 4);
            Assert.Equal(0.42f, loaded.gameConditionManager.AggregateGrowthFactor(), 4);
        }

        // ---- the incident: NamedRand + Ablation discipline ----

        [Fact]
        public void Firing_the_incident_does_not_disturb_the_ambient_random_stream()
        {
            BareWorld();
            uint before = Rand.Current.Iterations;

            DroughtIncident.Worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), forced = true });

            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void Ablated_it_still_fires_and_registers_no_condition()
        {
            BareWorld();
            Ablation.Disable("Drought");
            try
            {
                bool fired = DroughtIncident.Worker.TryExecute(
                    new IncidentParms { target = new CivilizationTarget(), forced = true });

                Assert.True(fired, "an ablated incident must still report success, or selection itself changes");
                Assert.Empty(Find.World!.gameConditionManager.ActiveConditions);
                Assert.Equal(1f, Find.World!.gameConditionManager.AggregateGrowthFactor());
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Ablated_it_leaves_the_ambient_stream_exactly_where_the_live_one_does()
        {
            BareWorld();
            Ablation.Disable("Drought");
            try
            {
                uint before = Rand.Current.Iterations;
                DroughtIncident.Worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), forced = true });
                Assert.Equal(before, Rand.Current.Iterations);
            }
            finally
            {
                Ablation.Clear();
            }
        }

        [Fact]
        public void Switching_it_back_on_restores_the_drought()
        {
            BareWorld();
            Ablation.Disable("Drought");
            Ablation.Clear();

            DroughtIncident.Worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), forced = true });

            // The harness leaving an ablation set would quietly report a disabled world as the baseline, which
            // is the one failure that makes every number downstream wrong and none of them look it.
            Assert.Single(Find.World!.gameConditionManager.ActiveConditions);
        }

        [Fact]
        public void Severity_and_duration_come_from_their_own_ranges_through_the_seeded_stream()
        {
            var severities = new HashSet<float>();
            for (int i = 0; i < 25; i++)
            {
                BareWorld();
                Find.TickManager.DebugSetTicksGame(i * 777);
                Assert.True(DroughtIncident.Worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), forced = true }));

                var condition = (GameCondition_Drought)Find.World!.gameConditionManager.ActiveConditions[0];
                severities.Add(condition.Severity);

                Assert.InRange(condition.Severity, IncidentWorker_Drought.SeverityRange.min, IncidentWorker_Drought.SeverityRange.max);
                Assert.InRange(
                    condition.duration / (float)GenDate.TicksPerDay,
                    DroughtGameConditionDefOf.Drought.durationDays.min,
                    DroughtGameConditionDefOf.Drought.durationDays.max);
            }

            Assert.True(severities.Count > 1, "every drought rolled exactly the same severity");
        }

        [Fact]
        public void A_condition_does_not_stack_with_itself()
        {
            BareWorld();
            var target = new CivilizationTarget();

            Assert.True(DroughtIncident.Worker.TryExecute(new IncidentParms { target = target, forced = true }));
            Assert.False(DroughtIncident.Worker.CanFireNow(new IncidentParms { target = target, forced = true }));
            Assert.False(DroughtIncident.Worker.TryExecute(new IncidentParms { target = target, forced = true }));

            Assert.Single(Find.World!.gameConditionManager.ActiveConditions);
        }

        [Fact]
        public void An_incident_cannot_fire_before_there_is_a_world()
        {
            Find.World = null;
            var target = new CivilizationTarget();

            Assert.False(DroughtIncident.Worker.CanFireNow(new IncidentParms { target = target, forced = true }));
            Assert.False(DroughtIncident.Worker.TryExecute(new IncidentParms { target = target, forced = true }));
        }

        [Fact]
        public void Firing_the_incident_sends_a_letter()
        {
            BareWorld();
            int before = Find.LetterStack.LettersListForReading.Count;

            Assert.True(DroughtIncident.Worker.TryExecute(new IncidentParms { target = new CivilizationTarget(), forced = true }));

            Assert.True(Find.LetterStack.LettersListForReading.Count > before);
            Assert.Contains(
                Find.LetterStack.LettersListForReading,
                l => l.label.Contains("Drought", System.StringComparison.Ordinal));
        }
    }
}
