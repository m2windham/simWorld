using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Blueprint/frame/building construction, the power net graph, and room/temperature (system 16).</summary>
    public class BuildingTests : ContentTestBase
    {
        public BuildingTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Builder")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static void SetConstructionSkill(Pawn pawn, int level) =>
            pawn.skills!.GetSkill(SkillDefOf.Construction)!.Level = level;

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static global::SimWorld.Building.Building SpawnBuilding(CoreMap map, IntVec3 cell, string defName)
        {
            var thing = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        // ---- content ----

        [Fact]
        public void Building_content_loads_with_no_errors_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(StatDefOf.WorkToBuild);
            Assert.NotNull(StatDefOf.Insulation);
            Assert.NotNull(JobDefOf.HaulToBuildingSite);
            Assert.NotNull(JobDefOf.ConstructFinishFrame);
            Assert.IsType<WorkGiver_ConstructFinishFrame>(DefDatabase<WorkGiverDef>.GetNamed("ConstructFinishFrames").Worker);
            Assert.IsType<WorkGiver_ConstructDeliverResourcesToFrames>(DefDatabase<WorkGiverDef>.GetNamed("ConstructDeliverResourcesToFrames").Worker);
            Assert.IsType<WorkGiver_ConstructDeliverResourcesToBlueprints>(DefDatabase<WorkGiverDef>.GetNamed("ConstructDeliverResourcesToBlueprints").Worker);
        }

        // ---- GenConstruct placement validity ----

        [Fact]
        public void GenConstruct_rejects_terrain_lacking_the_needed_affordance()
        {
            CoreMap map = NewMap(5, 5);
            var needsHeavy = new ThingDef { defName = "TestNeedsHeavy", terrainAffordanceNeeded = SimWorld.Map.TerrainAffordanceDefOf.Heavy };

            TerrainDef marsh = DefDatabase<TerrainDef>.GetNamed("MarshyTerrain");
            map.terrainGrid.SetTerrain(new IntVec3(1, 0, 1), marsh);
            Assert.False(GenConstruct.CanPlaceBlueprintAt(needsHeavy, new IntVec3(1, 0, 1), map, out string? reason));
            Assert.NotNull(reason);

            Assert.True(GenConstruct.CanPlaceBlueprintAt(needsHeavy, new IntVec3(2, 0, 2), map, out _));
        }

        [Fact]
        public void GenConstruct_rejects_a_cell_already_holding_a_blueprint_frame_or_edifice()
        {
            CoreMap map = NewMap(5, 5);
            ThingDef wall = Def("Wall");
            var cell = new IntVec3(2, 0, 2);

            Assert.True(GenConstruct.CanPlaceBlueprintAt(wall, cell, map, out _));

            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, cell, map);
            Assert.False(GenConstruct.CanPlaceBlueprintAt(wall, cell, map, out string? reason));
            Assert.NotNull(reason);

            blueprint.Destroy();
            SpawnBuilding(map, cell, "Wall");
            Assert.False(GenConstruct.CanPlaceBlueprintAt(wall, cell, map, out _));
        }

        // ---- end-to-end construction ----

        [Fact]
        public void Pawn_hauls_materials_and_builds_a_wall_end_to_end()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
            SetConstructionSkill(pawn, 20); // deterministic success (see JobDriver_ConstructFinishFrame's curve)

            var site = new IntVec3(4, 0, 4);
            Thing blueprint = ThingMaker.MakeThing(Def("Blueprint_Wall"));
            GenSpawn.Spawn(blueprint, site, map);
            SpawnStack(map, new IntVec3(1, 0, 1), "WoodLog", 5);

            Assert.Null(map.edificeGrid[site]);
            Assert.True(map.pathGrid.Walkable(site));

            RunTicks(600, pawn);

            Thing? built = map.edificeGrid[site];
            Assert.NotNull(built);
            Assert.IsType<global::SimWorld.Building.Building>(built);
            Assert.Equal("Wall", built!.def.defName);
            Assert.False(map.pathGrid.Walkable(site));
            Assert.Contains(built, map.listerThings.ThingsInGroup(ThingRequestGroup.Building));
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
        }

        [Fact]
        public void Same_seed_gives_the_same_construction_outcome()
        {
            bool Run(int seed)
            {
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap(8, 8);
                Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
                SetConstructionSkill(pawn, 0);
                var site = new IntVec3(4, 0, 4);
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), site, map);
                SpawnStack(map, new IntVec3(1, 0, 1), "WoodLog", 5);
                RunTicks(600, pawn);
                return map.edificeGrid[site] != null;
            }

            bool first = Run(777);
            Pawn.ResetThingIdCounter();
            bool second = Run(777);
            Assert.Equal(first, second);
        }

        [Fact]
        public void Construction_speed_and_success_chance_both_increase_with_skill()
        {
            SimpleCurve speed = JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel;
            SimpleCurve chance = JobDriver_ConstructFinishFrame.SuccessChanceFromConstructionLevel;
            Assert.True(speed.Evaluate(20) > speed.Evaluate(0));
            Assert.True(chance.Evaluate(20) > chance.Evaluate(0));
        }

        [Fact]
        public void Failed_construction_consumes_some_materials_and_respawns_a_blueprint()
        {
            int failures = 0;
            for (int seed = 0; seed < 24; seed++)
            {
                Rand.Current = new RandomStream(seed);
                CoreMap map = NewMap(8, 8);
                Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));
                SetConstructionSkill(pawn, 0); // 50% success chance at level 0

                var site = new IntVec3(4, 0, 4);
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Wall")), site, map);
                SpawnStack(map, new IntVec3(1, 0, 1), "WoodLog", 5);
                RunTicks(600, pawn);

                // A Frame occupies the edifice grid too (see Frame.SpawnSetup) — check for the finished
                // Building specifically, not just "something is here".
                bool succeeded = map.edificeGrid[site] is global::SimWorld.Building.Building;
                if (succeeded) continue;

                failures++;

                // Every one of the 5 original WoodLog units is now either lying loose on the ground or
                // (if a hauler already re-delivered the refund into a fresh Frame) tallied in that Frame's
                // resourceContainer; either way the total left in the world must be less than 5 — some of
                // it was genuinely destroyed by the failure, not just relocated.
                int woodOnGround = map.listerThings.ThingsOfDef(Def("WoodLog")).Sum(t => t.stackCount);
                int woodInFrame = map.thingGrid.ThingsListAt(site).OfType<Frame>()
                    .Sum(f => f.MaterialDelivered(Def("WoodLog")));
                Assert.True(woodOnGround + woodInFrame < 5,
                    "A failed construction must not refund every delivered unit.");

                Pawn.ResetThingIdCounter();
            }

            Assert.InRange(failures, 4, 20); // a 50% chance over 24 tries lands well inside this band
        }

        // ---- Scribe ----

        [Fact]
        public void Blueprint_and_frame_round_trip_through_scribe()
        {
            CoreMap map = NewMap(6, 6);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("Blueprint_Door")), new IntVec3(1, 0, 1), map);

            var frame = (Frame)ThingMaker.MakeThing(Def("Frame_Wall"));
            GenSpawn.Spawn(frame, new IntVec3(2, 0, 2), map);
            frame.AddMaterial(Def("WoodLog"), 3);
            frame.workDone = 12.5f;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing? loadedBlueprint = loaded.thingGrid.ThingsListAt(new IntVec3(1, 0, 1))
                .FirstOrDefault(t => t is global::SimWorld.Building.Blueprint);
            Assert.NotNull(loadedBlueprint);
            Assert.Equal("Door", ((global::SimWorld.Building.Blueprint)loadedBlueprint!).EntityToBuild.defName);

            Thing? loadedFrameThing = loaded.edificeGrid[new IntVec3(2, 0, 2)];
            Assert.NotNull(loadedFrameThing);
            var loadedFrame = (Frame)loadedFrameThing!;
            Assert.Equal("Wall", loadedFrame.EntityToBuild.defName);
            Assert.Equal(3, loadedFrame.MaterialDelivered(Def("WoodLog")));
            Assert.Equal(12.5f, loadedFrame.workDone);
        }

        // ---- power ----

        private static CompPower PowerCompOf(Thing t) => ((ThingWithComps)t).GetComp<CompPower>()!;

        [Fact]
        public void Adjacent_power_comps_join_one_net_and_a_gap_keeps_them_apart()
        {
            CoreMap map = NewMap(10, 10);
            var conduits = new List<Thing>();
            for (int x = 0; x < 4; x++)
            {
                conduits.Add(SpawnBuilding(map, new IntVec3(x, 0, 0), "PowerConduit"));
            }

            PowerNet net = PowerCompOf(conduits[0]).powerNet!;
            Assert.Equal(4, net.Connectors.Count);
            foreach (Thing c in conduits) Assert.Same(net, PowerCompOf(c).powerNet);

            // Removing the third conduit (index 2) splits the line into two nets: {0,1} and {3}.
            conduits[2].Destroy();
            Assert.Equal(2, PowerCompOf(conduits[0]).powerNet!.Connectors.Count);
            Assert.Single(PowerCompOf(conduits[3]).powerNet!.Connectors);
            Assert.NotSame(PowerCompOf(conduits[0]).powerNet, PowerCompOf(conduits[3]).powerNet);
        }

        [Fact]
        public void Generator_charges_a_battery_and_powers_a_heater_then_browns_out_when_cut()
        {
            CoreMap map = NewMap(10, 4);

            // A straight run: generator - conduit - battery - conduit - heater.
            Thing generator = SpawnBuilding(map, new IntVec3(0, 0, 0), "WoodFiredGenerator");
            SpawnBuilding(map, new IntVec3(1, 0, 0), "PowerConduit");
            Thing battery = SpawnBuilding(map, new IntVec3(2, 0, 0), "Battery");
            SpawnBuilding(map, new IntVec3(3, 0, 0), "PowerConduit");
            Thing heater = SpawnBuilding(map, new IntVec3(4, 0, 0), "Heater");

            PowerNet net = PowerCompOf(generator).powerNet!;
            Assert.Same(net, PowerCompOf(battery).powerNet);
            Assert.Same(net, PowerCompOf(heater).powerNet);

            var batteryComp = (CompPowerBattery)PowerCompOf(battery);
            var heaterPower = (CompPowerTrader)PowerCompOf(heater);

            for (int i = 0; i < 50; i++) map.MapTick();
            Assert.True(batteryComp.storedEnergy > 0f, "Surplus generation should have charged the battery.");
            Assert.True(heaterPower.powerOn);

            generator.Destroy();
            for (int i = 0; i < 400; i++) map.MapTick();

            Assert.Equal(0f, batteryComp.storedEnergy);
            Assert.False(heaterPower.powerOn, "Once the battery is drained the net must brown out.");
        }

        // ---- rooms & temperature ----

        [Fact]
        public void Four_walls_and_a_roof_form_an_enclosed_room_that_equalises_toward_outdoor_temperature()
        {
            CoreMap map = NewMap(10, 10);
            map.outdoorTemperature = 0f;
            var rect = new CellRect(2, 2, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, "Wall");
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);

            map.MapTick();
            Room? interior = map.roomTracker.RoomAt(new IntVec3(3, 0, 3));
            Assert.NotNull(interior);
            Assert.False(interior!.TouchesOutside);

            interior.Group.temperature = 30f;
            for (int i = 0; i < 200; i++) map.MapTick();
            Assert.True(interior.Temperature < 30f);
            Assert.True(interior.Temperature > 0f);
        }

        [Fact]
        public void An_unroofed_enclosure_tracks_outdoor_temperature_directly()
        {
            CoreMap map = NewMap(10, 10);
            map.outdoorTemperature = 5f;
            var rect = new CellRect(2, 2, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, "Wall");
            // No roof: an open-air courtyard.

            map.MapTick();
            Room? interior = map.roomTracker.RoomAt(new IntVec3(3, 0, 3));
            Assert.NotNull(interior);
            Assert.True(interior!.TouchesOutside);
            Assert.Equal(5f, interior.Temperature);

            map.outdoorTemperature = 40f;
            map.MapTick();
            Assert.Equal(40f, interior.Temperature);
        }

        [Fact]
        public void Two_rooms_joined_only_by_a_door_share_one_room_group()
        {
            CoreMap map = NewMap(12, 8);
            // A single outer shell (x:1..9, z:1..5) split by one dividing wall column at x=5, with one cell
            // of that column (5,3) a Door instead of a Wall — the only cell either sub-room's flood fill
            // reaches on that side, so both rooms record the very same Door instance as a boundary edifice.
            var outer = new CellRect(1, 1, 9, 5);
            foreach (IntVec3 c in outer.EdgeCells) SpawnBuilding(map, c, "Wall");
            foreach (int z in new[] { 1, 2, 4, 5 }) SpawnBuilding(map, new IntVec3(5, 0, z), "Wall");
            SpawnBuilding(map, new IntVec3(5, 0, 3), "Door");
            foreach (IntVec3 c in outer.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);

            map.MapTick();
            Room? left = map.roomTracker.RoomAt(new IntVec3(3, 0, 3));
            Room? right = map.roomTracker.RoomAt(new IntVec3(7, 0, 3));
            Assert.NotNull(left);
            Assert.NotNull(right);
            Assert.NotSame(left, right); // still two distinct Rooms
            Assert.Same(left!.Group, right!.Group); // but one RoomGroup

            left.Group.temperature = 17f;
            Assert.Equal(17f, right.Temperature);
        }

        [Fact]
        public void A_bigger_room_equalises_slower_than_a_smaller_one()
        {
            float FinalTempAfter(int width)
            {
                CoreMap map = NewMap(16, 16);
                map.outdoorTemperature = 0f;
                var rect = new CellRect(1, 1, width, width);
                foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, "Wall");
                foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
                map.MapTick();
                var center = new IntVec3(rect.minX + width / 2, 0, rect.minZ + width / 2);
                Room room = map.roomTracker.RoomAt(center)!;
                room.Group.temperature = 30f;
                for (int i = 0; i < 30; i++) map.MapTick();
                return room.Temperature;
            }

            float small = FinalTempAfter(4);
            float big = FinalTempAfter(10);
            Assert.True(big > small, "A larger enclosed volume should have cooled less in the same time.");
        }

        [Fact]
        public void Higher_insulation_equalises_slower()
        {
            float FinalTempWithBoundary(string boundaryDefName)
            {
                CoreMap map = NewMap(10, 10);
                map.outdoorTemperature = 0f;
                var rect = new CellRect(2, 2, 4, 4);
                foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, boundaryDefName);
                foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
                map.MapTick();
                Room room = map.roomTracker.RoomAt(new IntVec3(3, 0, 3))!;
                room.Group.temperature = 30f;
                for (int i = 0; i < 30; i++) map.MapTick();
                return room.Temperature;
            }

            // Walls (Insulation 100) vs. an all-door boundary (Insulation 1): the door-bounded room should
            // equalise toward the outdoors noticeably faster.
            float wallBounded = FinalTempWithBoundary("Wall");
            float doorBounded = FinalTempWithBoundary("Door");
            Assert.True(doorBounded < wallBounded);
        }

        [Fact]
        public void Powered_heater_warms_its_room_toward_its_target_temperature()
        {
            CoreMap map = NewMap(10, 10);
            map.outdoorTemperature = -10f;
            var rect = new CellRect(2, 2, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, "Wall");
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);

            SpawnBuilding(map, new IntVec3(3, 0, 3), "Heater");
            SpawnBuilding(map, new IntVec3(4, 0, 3), "WoodFiredGenerator"); // cardinally adjacent: powers it directly

            for (int i = 0; i < 60; i++) map.MapTick();

            Room room = map.roomTracker.RoomAt(new IntVec3(4, 0, 4))!;
            Assert.True(room.Temperature > map.outdoorTemperature);
        }

        // ---- Scribe: power net + room temperature via the whole map ----

        [Fact]
        public void Map_round_trips_battery_charge_and_room_temperature()
        {
            CoreMap map = NewMap(10, 10);
            map.outdoorTemperature = 0f;
            var rect = new CellRect(2, 2, 4, 4);
            foreach (IntVec3 c in rect.EdgeCells) SpawnBuilding(map, c, "Wall");
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            SpawnBuilding(map, new IntVec3(0, 0, 0), "WoodFiredGenerator");
            Thing battery = SpawnBuilding(map, new IntVec3(1, 0, 0), "Battery");

            for (int i = 0; i < 20; i++) map.MapTick();
            Room before = map.roomTracker.RoomAt(new IntVec3(3, 0, 3))!;
            before.Group.temperature = 12f;
            float storedBefore = ((CompPowerBattery)PowerCompOf(battery)).storedEnergy;
            Assert.True(storedBefore > 0f);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            loaded.MapTick(); // rebuild rooms/power nets lazily, exactly like a freshly loaded map would
            Thing loadedBattery = loaded.listerThings.ThingsOfDef(Def("Battery")).Single();
            Assert.Equal(storedBefore, ((CompPowerBattery)PowerCompOf(loadedBattery)).storedEnergy);

            // The one MapTick above both reattaches the saved temperature and immediately applies its own
            // tick of equalisation toward outdoor (0), so this lands close to 12 rather than exactly on it.
            Room after = loaded.roomTracker.RoomAt(new IntVec3(3, 0, 3))!;
            Assert.InRange(after.Temperature, 11f, 12f);
        }
    }
}
