using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Citizen-initiated construction under edicts (<c>docs/status.json</c>'s <c>building.initiative</c>):
    /// a settlement deriving what it lacks, placing blueprints for it through the existing
    /// <see cref="GenConstruct"/> pipeline, the edict bias seam, self-gating, and the honest "no interior map,
    /// no-op" boundary.
    /// </summary>
    public class ConstructionInitiativeTests : ContentTestBase
    {
        public ConstructionInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        /// <summary>A bare <see cref="Settlement"/> with no world behind it — the same minimal-construction
        /// shape <c>GodTests.PlainSettlement</c>/<c>SettlementTests.PlainSettlement</c> use for tests that only
        /// need the entity itself, not a generated world to place it on.</summary>
        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "TestSettlement" + tile, 0);

        private static void AddCitizens(Settlement settlement, int count)
        {
            for (int i = 0; i < count; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
        }

        private static Thing SpawnBuilding(CoreMap map, IntVec3 cell, string defName)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static Thing SpawnBlueprint(CoreMap map, IntVec3 cell, string defName)
        {
            Thing thing = ThingMaker.MakeThing(Def("Blueprint_" + defName));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        private static int BlueprintCount(CoreMap map, ThingDef entityDef) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<global::SimWorld.Building.Blueprint>()
                .Count(bp => bp.EntityToBuild == entityDef);

        private static EdictDef TestEdict(string defName, params ThingDef[] prioritizedConstruction) =>
            new EdictDef
            {
                defName = defName,
                prioritizedConstruction = prioritizedConstruction.ToList(),
            };

        // ---- content ----

        [Fact]
        public void Content_loads_with_no_errors_and_the_new_DefOfs_bind()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(ConstructionThingDefOf.Bed);
            Assert.NotNull(ConstructionThingDefOf.Wall);
            Assert.NotNull(ConstructionThingDefOf.StorageHut);
            Assert.NotNull(GenConstruct.BlueprintDefFor(ConstructionThingDefOf.Bed));
            Assert.NotNull(GenConstruct.FrameDefFor(ConstructionThingDefOf.Bed));
            Assert.NotNull(GenConstruct.BlueprintDefFor(ConstructionThingDefOf.StorageHut));
            Assert.NotNull(GenConstruct.FrameDefFor(ConstructionThingDefOf.StorageHut));

            EdictDef greatWorks = DefDatabase<EdictDef>.GetNamed("GreatWorksMandate");
            Assert.NotEmpty(greatWorks.prioritizedConstruction);
        }

        // ---- the honest boundary: no interior map, nowhere to place anything ----

        [Fact]
        public void A_settlement_never_entered_is_a_no_op_and_never_triggers_generation()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 5);

            SettlementConstructionInitiative.TickSettlement(settlement);

            Assert.Null(settlement.InteriorMap);
        }

        // ---- deriving needs ----

        [Fact]
        public void A_settlement_with_citizens_and_no_beds_queues_beds_up_to_the_per_tick_cap()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 5); // shortfall (5) exceeds the per-tick cap
            CoreMap map = NewMap(20, 20);

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            Assert.Equal(ConstructionInitiativeTuning.MaxBlueprintsPerTick, BlueprintCount(map, ConstructionThingDefOf.Bed));
            // Bed alone exhausts this tick's whole budget ahead of Wall/Storage — default priority order.
            Assert.Equal(0, BlueprintCount(map, ConstructionThingDefOf.Wall));
        }

        [Fact]
        public void No_citizens_and_no_stores_needs_nothing()
        {
            Settlement settlement = PlainSettlement();
            CoreMap map = NewMap(20, 20);

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
        }

        [Fact]
        public void Already_built_or_already_planned_counts_toward_the_target()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 3); // bed target 3
            CoreMap map = NewMap(20, 20);
            SpawnBuilding(map, new IntVec3(1, 0, 1), "Bed"); // 1 already built
            SpawnBlueprint(map, new IntVec3(2, 0, 2), "Bed"); // 1 already planned

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            // Shortfall was 3 - (1 built + 1 planned) = 1: exactly one new Blueprint_Bed, the pre-existing one
            // left untouched, never a duplicate for a need already on its way to being met.
            Assert.Equal(2, BlueprintCount(map, ConstructionThingDefOf.Bed));

            // A second gated tick with the shortfall already at zero adds nothing further.
            Find.TickManager.DebugSetTicksGame(ConstructionInitiativeTuning.IntervalTicks);
            SettlementConstructionInitiative.TickSettlement(settlement, map);
            Assert.Equal(2, BlueprintCount(map, ConstructionThingDefOf.Bed));
        }

        [Fact]
        public void Storage_target_grows_with_how_much_the_settlement_actually_stores()
        {
            Settlement small = PlainSettlement(1);
            small.AddStore(Def("WoodLog"), 10); // below one hut's worth
            CoreMap smallMap = NewMap(20, 20);

            Settlement large = PlainSettlement(2);
            large.AddStore(Def("WoodLog"), 200); // several huts' worth
            CoreMap largeMap = NewMap(20, 20);

            SettlementConstructionInitiative.TickSettlement(small, smallMap);
            SettlementConstructionInitiative.TickSettlement(large, largeMap);

            int smallHuts = BlueprintCount(smallMap, ConstructionThingDefOf.StorageHut);
            int largeHuts = BlueprintCount(largeMap, ConstructionThingDefOf.StorageHut);
            Assert.True(smallHuts >= 1, "Any stored goods at all should want at least one hut.");
            Assert.True(largeHuts > smallHuts, "More stored goods should want more storage, not the same amount.");
        }

        [Fact]
        public void Placement_never_overlaps_or_blocks_what_is_already_there()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 1); // bed target 1
            CoreMap map = NewMap(3, 3);

            // Fill every cell but one with an edifice, so GenConstruct.CanPlaceBlueprintAt refuses everywhere
            // except the single free cell.
            var free = new IntVec3(1, 0, 1);
            foreach (IntVec3 c in map.AllCells)
            {
                if (c == free) continue;
                SpawnBuilding(map, c, "Wall");
            }

            SettlementConstructionInitiative.TickSettlement(settlement, map);

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            Assert.Single(blueprints);
            Assert.Equal(free, blueprints[0].Position);
        }

        // ---- determinism ----

        [Fact]
        public void Same_seed_gives_the_same_placement()
        {
            List<IntVec3> Run(int seed)
            {
                Rand.Current = new RandomStream(seed);
                Settlement settlement = PlainSettlement();
                AddCitizens(settlement, 2);
                CoreMap map = NewMap(30, 30);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
                return map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                    .Select(t => t.Position)
                    .OrderBy(p => p.x).ThenBy(p => p.z)
                    .ToList();
            }

            List<IntVec3> first = Run(4242);
            Pawn.ResetThingIdCounter();
            List<IntVec3> second = Run(4242);

            Assert.Equal(first, second);
            Assert.NotEmpty(first);
        }

        // ---- self-gating ----

        [Fact]
        public void Self_gates_on_the_tuned_interval()
        {
            Settlement settlement = PlainSettlement();
            AddCitizens(settlement, 1);
            CoreMap map = NewMap(20, 20);

            Find.TickManager.DebugSetTicksGame(1); // not a multiple of the interval
            SettlementConstructionInitiative.TickSettlement(settlement, map);
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));

            Find.TickManager.DebugSetTicksGame(ConstructionInitiativeTuning.IntervalTicks); // gated tick
            SettlementConstructionInitiative.TickSettlement(settlement, map);
            Assert.NotEmpty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
        }

        [Fact]
        public void Tick_is_a_silent_no_op_without_a_running_world()
        {
            Find.TickManager.DebugSetTicksGame(0);
            // No exception even though Find.World is null (no game running) — the same "nothing to do
            // without a live world" boundary Sim.Game.TickMaps already draws for itself.
            SettlementConstructionInitiative.Tick();
        }

        // ---- the edict seam ----

        [Fact]
        public void An_active_edict_reorders_which_need_is_filled_first_and_leaves_no_trace_once_deactivated()
        {
            Settlement Scenario(int tile)
            {
                var s = PlainSettlement(tile);
                AddCitizens(s, 1); // bed target 1, wall target clamps to the 4-minimum
                s.AddStore(Def("WoodLog"), ConstructionInitiativeTuning.GoodsPerStorageHut); // storage target 1
                return s;
            }

            // Control: no edict active. Bed(1) then Wall(4) already exhaust the per-tick cap (3) before
            // Storage is ever reached, so no StorageHut blueprint appears this tick.
            CoreMap controlMap = NewMap(30, 30);
            SettlementConstructionInitiative.TickSettlement(Scenario(1), controlMap);
            Assert.Equal(0, BlueprintCount(controlMap, ConstructionThingDefOf.StorageHut));

            // Treatment: an edict biasing StorageHut moves it to the front of this settlement's own priority
            // order — read live off Find.God, no activation-time edit to anything this class owns.
            EdictDef edict = TestEdict("TestStorageBias", ConstructionThingDefOf.StorageHut);
            Assert.True(Find.God.Activate(edict));

            CoreMap treatmentMap = NewMap(30, 30);
            SettlementConstructionInitiative.TickSettlement(Scenario(2), treatmentMap);
            Assert.Equal(1, BlueprintCount(treatmentMap, ConstructionThingDefOf.StorageHut));

            // Deactivating leaves no trace: the very next settlement reverts to the unbiased order.
            Assert.True(Find.God.Deactivate(edict));
            CoreMap afterMap = NewMap(30, 30);
            SettlementConstructionInitiative.TickSettlement(Scenario(3), afterMap);
            Assert.Equal(0, BlueprintCount(afterMap, ConstructionThingDefOf.StorageHut));
        }

        // ---- Scribe: the new content round-trips through the existing generic Blueprint/Frame machinery ----

        [Fact]
        public void Bed_and_storage_hut_blueprints_and_frames_round_trip_through_scribe()
        {
            CoreMap map = NewMap(6, 6);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Bed")), new IntVec3(1, 0, 1), map);

            var frame = (global::SimWorld.Building.Frame)ThingMaker.MakeThing(Def("Frame_StorageHut"));
            GenSpawn.Spawn(frame, new IntVec3(2, 0, 2), map);
            frame.AddMaterial(Def("WoodLog"), 6);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing? loadedBlueprint = loaded.thingGrid.ThingsListAt(new IntVec3(1, 0, 1))
                .FirstOrDefault(t => t is global::SimWorld.Building.Blueprint);
            Assert.NotNull(loadedBlueprint);
            Assert.Equal("Bed", ((global::SimWorld.Building.Blueprint)loadedBlueprint!).EntityToBuild.defName);

            Thing? loadedFrameThing = loaded.edificeGrid[new IntVec3(2, 0, 2)];
            Assert.NotNull(loadedFrameThing);
            var loadedFrame = (global::SimWorld.Building.Frame)loadedFrameThing!;
            Assert.Equal("StorageHut", loadedFrame.EntityToBuild.defName);
            Assert.Equal(6, loadedFrame.MaterialDelivered(Def("WoodLog")));
        }

        // ---- the whole loop: needs a bed -> gets a blueprint -> a citizen builds it ----

        [Fact]
        public void A_settlement_that_needs_a_bed_ends_up_with_one_built_by_a_citizen()
        {
            CoreMap map = NewMap(10, 10);
            Settlement settlement = PlainSettlement();
            Pawn citizen = NewHuman("Builder");
            settlement.AddCitizen(citizen);
            citizen.skills!.GetSkill(SkillDefOf.Construction)!.Level = 20; // deterministic success, matches BuildingTests' own idiom
            GenSpawn.Spawn(citizen, new IntVec3(0, 0, 0), map);

            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 40;
            GenSpawn.Spawn(wood, new IntVec3(1, 0, 1), map);

            // Places this tick's blueprints (bed target 1, wall target clamps to 4, cap 3): a Blueprint_Bed
            // and up to two Blueprint_Wall — nothing decided this before SettlementConstructionInitiative.
            SettlementConstructionInitiative.TickSettlement(settlement, map);
            Assert.Equal(1, BlueprintCount(map, ConstructionThingDefOf.Bed));

            RunTicks(4000, citizen);

            Assert.True(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= 1,
                "The citizen should have hauled materials and finished the bed through the existing construction work givers.");
        }

        // ---- the whole loop through the production entry point (Find.World-driven Tick()) ----

        private static SimWorld.World.World GenerateSoloWorld(string seed, int subdivision = 4) =>
            WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision, soloStart: true);

        [Fact]
        public void Tick_drives_every_settlement_in_the_running_world_and_still_self_gates()
        {
            SimWorld.World.World world = GenerateSoloWorld("construction-initiative-tick");
            Faction faction = world.factions.First();
            int tile = System.Linq.Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(99));
            settlement.EnterMap(world);
            Find.World = world;

            Find.TickManager.DebugSetTicksGame(0); // 0 is itself a multiple of the interval: gate passes
            SettlementConstructionInitiative.Tick();
            int afterFirst = BlueprintCount(settlement.InteriorMap!, ConstructionThingDefOf.Bed);
            Assert.Equal(ConstructionInitiativeTuning.MaxBlueprintsPerTick, afterFirst);

            Find.TickManager.DebugSetTicksGame(1); // not a multiple: gated out
            SettlementConstructionInitiative.Tick();
            Assert.Equal(afterFirst, BlueprintCount(settlement.InteriorMap!, ConstructionThingDefOf.Bed));

            Find.TickManager.DebugSetTicksGame(ConstructionInitiativeTuning.IntervalTicks); // next gated tick
            SettlementConstructionInitiative.Tick();
            Assert.True(BlueprintCount(settlement.InteriorMap!, ConstructionThingDefOf.Bed) > afterFirst);
        }

        // ---- the whole loop end to end: nobody spawned by hand (World.Settlement's own seam) ----

        private static IntVec3 FirstStandableCell(CoreMap map)
        {
            foreach (IntVec3 c in map.AllCells)
            {
                if (GenGrid.Standable(c, map)) return c;
            }
            return IntVec3.Zero;
        }

        [Fact]
        public void A_founded_settlements_own_citizens_build_a_queued_bed_with_nobody_spawned_by_hand()
        {
            SimWorld.World.World world = GenerateSoloWorld("construction-initiative-citizens-build");
            Faction faction = world.factions.First();
            int tile = System.Linq.Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(4343));
            Find.World = world;

            // Entering is the whole of it: the settlement's own founders are already standing on this map —
            // nothing here calls GenSpawn.Spawn on a pawn.
            CoreMap map = settlement.EnterMap(world);
            // Counted over the people: a generated interior also carries the wildlife its biome supports
            // (SimWorld.MapGen.GenStep_Animals), and the claim here is that no *pawn of ours* was placed by
            // hand — the founders are simply already standing on their own map.
            Assert.Equal(settlement.Citizens.Count, map.mapPawns.AllPawns.Count(p => p.RaceProps.Humanlike));

            // Deterministic construction success, matching this file's own idiom above
            // (A_settlement_that_needs_a_bed_ends_up_with_one_built_by_a_citizen) — which citizen actually
            // picks up the job is the initiative's/work system's call, not this test's, so every founder gets
            // the skill rather than picking one out.
            foreach (Pawn citizen in settlement.Citizens)
            {
                SkillRecord? construction = citizen.skills?.GetSkill(SkillDefOf.Construction);
                if (construction != null) construction.Level = 20;
            }

            // Materials are the one thing this test still places by hand: a settlement's starting resources
            // are credited to Settlement.Stores as a def-count ledger, never spawned as a real Thing
            // (Sim.Game.NewGame's own documented limitation — no map exists yet when a scenario's starting
            // items are granted), so nothing in production puts real wood on this ground either.
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 400;
            GenSpawn.Spawn(wood, FirstStandableCell(map), map);

            for (int i = 0; i < 6000; i++)
            {
                SettlementConstructionInitiative.Tick();
                Find.TickManager.DoSingleTick();
            }

            Assert.True(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= 1,
                "The settlement's own citizens should have queued and finished a bed with nobody spawned by hand.");
        }
    }
}
