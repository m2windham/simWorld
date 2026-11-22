using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// RimWorld's passive cooler (Buildings_Temperature.xml) and the two comps it carries: the heat pusher
    /// (Verse.CompHeatPusher, -11 a second down to 17 C) and the refuelable (RimWorld.CompRefuelable, 50 wood
    /// burned at 10 a day), with the refuel work that keeps it going.
    /// </summary>
    public class PassiveCoolerTests : ContentTestBase
    {
        public PassiveCoolerTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Cooler => CoolingThingDefOf.PassiveCooler;

        private static CoreMap NewMap(float outdoor, int size = 20) =>
            new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil) { outdoorTemperature = outdoor };

        private static Thing Spawn(CoreMap map, IntVec3 cell, string defName, Faction? faction = null)
        {
            Thing t = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
            if (faction != null) t.SetFaction(faction);
            return GenSpawn.Spawn(t, cell, map);
        }

        /// <summary>A walled, roofed room of <paramref name="w"/> by <paramref name="h"/> inside, a door in its
        /// east wall, its air set to <paramref name="inside"/>.</summary>
        private static Room BuildRoom(CoreMap map, int w, int h, float inside, Faction? owner = null)
        {
            var shell = new CellRect(1, 1, w + 2, h + 2);
            var door = new IntVec3(shell.maxX, 0, shell.minZ + shell.Height / 2);
            foreach (IntVec3 c in shell.EdgeCells) Spawn(map, c, c == door ? "Door" : "Wall", owner);
            foreach (IntVec3 c in shell.Cells) map.roofGrid.SetRoof(c, SimWorld.Map.RoofDefOf.RoofConstructed);
            map.MapTick();
            Room room = map.roomTracker.RoomAt(new IntVec3(2, 0, 2))!;
            Assert.False(room.Group.UsesOutdoorTemperature);
            room.Group.temperature = inside;
            return room;
        }

        private static void Run(CoreMap map, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }
        }

        /// <summary>The air of the room at (2, 2), looked up afresh: anything spawned re-floods the rooms, so a
        /// Room held from before is stale.</summary>
        private static float Air(CoreMap map) => map.roomTracker.RoomAt(new IntVec3(2, 0, 2))!.Temperature;

        private static CompRefuelable Fuel(Thing t) => ((ThingWithComps)t).GetComp<CompRefuelable>()!;

        [Fact]
        public void The_def_is_rimworlds()
        {
            Assert.Equal(Traversability.PassThroughOnly, Cooler.passability);
            Assert.Equal(0.40f, Cooler.fillPercent, 3);
            var push = (CompProperties_HeatPusher)Cooler.comps!.Find(c => c is CompProperties_HeatPusher)!;
            Assert.Equal(typeof(CompHeatPusherGated), push.compClass);
            Assert.Equal(-11f, push.heatPerSecond);
            Assert.Equal(17f, push.heatPushMinTemperature);
            var fuel = (CompProperties_Refuelable)Cooler.comps.Find(c => c is CompProperties_Refuelable)!;
            Assert.Equal(10f, fuel.fuelConsumptionRate);
            Assert.Equal(50f, fuel.fuelCapacity);
            Assert.Equal(1f, fuel.initialFuelPercent);
            Assert.True(fuel.fuelFilter.Allows(DefDatabase<ThingDef>.GetNamed("WoodLog")));
            Assert.Contains(DefDatabase<SimWorld.Research.ResearchProjectDef>.GetNamed("PassiveCooler"), Cooler.researchPrerequisites!);
        }

        [Fact]
        public void One_push_moves_a_room_by_eleven_over_its_cell_count()
        {
            CoreMap map = NewMap(30f);
            Room room = BuildRoom(map, 5, 5, 30f);
            Assert.True(CompHeatPusher.PushHeat(new IntVec3(2, 0, 2), map, -11f));
            Assert.Equal(30f - 11f / 25f, room.Temperature, 4);
        }

        [Fact]
        public void Heat_pushed_into_a_wall_is_shared_between_the_rooms_beside_it()
        {
            CoreMap map = NewMap(30f);
            Room room = BuildRoom(map, 5, 5, 30f);
            // (0..6) is the shell; its west wall at x=1 touches the room on one side and outdoors on the other.
            Assert.True(CompHeatPusher.PushHeat(new IntVec3(1, 0, 3), map, -11f));
            Assert.True(room.Temperature < 30f);
        }

        [Fact]
        public void It_cools_a_hot_room_and_burns_fuel_as_it_does()
        {
            float After(bool withCooler)
            {
                CoreMap map = NewMap(45f);
                BuildRoom(map, 5, 5, 30f);
                if (withCooler) Spawn(map, new IntVec3(3, 0, 3), "PassiveCooler");
                Run(map, 2500);
                return Air(map);
            }

            float without = After(false), with = After(true);
            Assert.True(with < without - 3f, $"with {with}, without {without}");
        }

        [Fact]
        public void It_stops_at_seventeen_degrees()
        {
            CoreMap map = NewMap(10f);
            BuildRoom(map, 2, 2, 17f);
            Spawn(map, new IntVec3(2, 0, 2), "PassiveCooler");
            map.MapTick();
            Room room = map.roomTracker.RoomAt(new IntVec3(2, 0, 2))!;
            for (int i = 0; i < 600; i++)
            {
                room.Group.temperature = 17f; // hold the air at the cutoff against the cold outside
                Find.TickManager.DoSingleTick();
            }
            Assert.Equal(17f, room.Temperature);
        }

        [Fact]
        public void Out_of_fuel_it_cools_nothing()
        {
            CoreMap map = NewMap(30f);
            BuildRoom(map, 5, 5, 30f);
            Thing cooler = Spawn(map, new IntVec3(3, 0, 3), "PassiveCooler");
            map.MapTick();
            Fuel(cooler).ConsumeFuel(50f);
            Assert.False(Fuel(cooler).HasFuel);
            for (int i = 0; i < 600; i++) Find.TickManager.DoSingleTick();
            Assert.Equal(30f, Air(map), 3);
        }

        [Fact]
        public void A_full_tank_burns_ten_a_day_and_lasts_five_days()
        {
            CoreMap map = NewMap(30f);
            Thing cooler = Spawn(map, new IntVec3(10, 0, 10), "PassiveCooler");
            Assert.Equal(50f, Fuel(cooler).Fuel);
            for (int i = 0; i < 6000; i++) Find.TickManager.DoSingleTick();
            Assert.InRange(Fuel(cooler).Fuel, 48.95f, 49.05f); // a float summed 6000 times
            Assert.False(Fuel(cooler).ShouldAutoRefuelNow); // 98% of the tank: RimWorld waits for 30%
            Fuel(cooler).ConsumeFuel(34f);
            Assert.True(Fuel(cooler).ShouldAutoRefuelNow);
            Assert.Equal((int)System.Math.Ceiling(50f - Fuel(cooler).Fuel), Fuel(cooler).GetFuelCountToFullyRefuel());
        }

        [Fact]
        public void A_hauler_tops_up_a_low_cooler_with_wood()
        {
            CoreMap map = NewMap(20f);
            Faction settlers = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            Thing cooler = Spawn(map, new IntVec3(10, 0, 10), "PassiveCooler", settlers);
            Fuel(cooler).ConsumeFuel(40f);
            Thing wood = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog"));
            wood.stackCount = 60;
            GenSpawn.Spawn(wood, new IntVec3(4, 0, 4), map);
            Pawn hauler = NewHuman("Hauler");
            hauler.faction = settlers;
            GenSpawn.Spawn(hauler, new IntVec3(2, 0, 2), map);

            var giver = new WorkGiver_Refuel();
            Assert.Contains(cooler, giver.PotentialWorkThingsGlobal(hauler));
            Assert.True(giver.HasJobOnThing(hauler, cooler));
            Job job = giver.JobOnThing(hauler, cooler)!;
            Assert.Same(RefuelJobDefOf.Refuel, job.def);
            hauler.jobs.StartJob(job);
            for (int i = 0; i < 2000 && !Fuel(cooler).IsFull; i++) RunTicks(1, hauler);

            Assert.True(Fuel(cooler).IsFull, $"fuel {Fuel(cooler).Fuel}");
            Assert.True(wood.stackCount <= 20 || wood.Destroyed);
            Assert.False(giver.HasJobOnThing(hauler, cooler));
        }

        [Fact]
        public void Nobody_refuels_a_cooler_of_another_faction()
        {
            CoreMap map = NewMap(20f);
            Thing cooler = Spawn(map, new IntVec3(10, 0, 10), "PassiveCooler");
            Fuel(cooler).ConsumeFuel(40f);
            Thing wood = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog"));
            wood.stackCount = 60;
            GenSpawn.Spawn(wood, new IntVec3(4, 0, 4), map);
            Pawn hauler = NewHuman("Hauler");
            hauler.faction = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            GenSpawn.Spawn(hauler, new IntVec3(2, 0, 2), map);
            Assert.False(new WorkGiver_Refuel().HasJobOnThing(hauler, cooler));
        }

        [Fact]
        public void The_fuel_level_survives_a_save_round_trip()
        {
            CoreMap map = NewMap(20f);
            Thing cooler = Spawn(map, new IntVec3(10, 0, 10), "PassiveCooler");
            Fuel(cooler).ConsumeFuel(12.5f);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Thing again = loaded.listerThings.ThingsOfDef(Cooler)[0];
            Assert.Equal(37.5f, Fuel(again).Fuel, 3);
        }

        /// <summary>
        /// The largest dormitory the compound builds (10 by 8 inside), a 45 C heat wave outside. One cooler keeps
        /// it out of heatstroke's reach but not comfortable; two make it comfortable, and then a citizen with
        /// serious heatstroke outside is sent into it, where every one of its 80 cells is somewhere to stand.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Coolers_in_a_heat_waved_dormitory_and_where_heatstroke_sends_a_citizen(int coolers)
        {
            CoreMap map = NewMap(45f, 24);
            BuildRoom(map, 10, 8, 45f);
            for (int i = 0; i < coolers; i++) Spawn(map, new IntVec3(2, 0, 5 + i), "PassiveCooler");
            Run(map, 20000);

            Pawn citizen = NewHuman("Citizen");
            GenSpawn.Spawn(citizen, new IntVec3(18, 0, 18), map);
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(citizen);
            float air = Air(map);
            Room room = map.roomTracker.RoomAt(new IntVec3(2, 0, 2))!;
            Assert.Equal(80, room.Cells.Count);
            int standable = 0;
            foreach (IntVec3 c in room.Cells) standable += map.pathGrid.Walkable(c) ? 1 : 0;
            Assert.Equal(80, standable);
            HealthUtility.AdjustSeverity(citizen, TemperatureDefOf.Heatstroke, 0.4f);
            ThinkResult result = new JobGiver_SeekSafeTemperature().TryIssueJobPackage(citizen);

            if (coolers == 1)
            {
                Assert.True(air > comfy.max && air < comfy.max + 10f, $"one cooler: {air}");
                Assert.False(result.IsValid);
            }
            else
            {
                Assert.True(comfy.Includes(air), $"two coolers: {air}");
                Assert.True(result.IsValid);
                Assert.Same(SafeTemperatureJobDefOf.GotoSafeTemperature, result.Job!.def);
                Assert.Same(room, map.roomTracker.RoomAt(result.Job.GetTarget(TargetIndex.A).Cell));
            }
        }
    }
}
