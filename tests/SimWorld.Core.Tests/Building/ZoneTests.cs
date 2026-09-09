using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Zones, the home area, and the sow/harvest work they drive (system 16: Building — zones and
    /// plant growth).</summary>
    public class ZoneTests : ContentTestBase
    {
        public ZoneTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name = "Farmer")
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // ---- content ----

        [Fact]
        public void Zone_content_loads_with_no_errors_and_JobDefs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(BuildingJobDefOf.Sow);
            Assert.NotNull(BuildingJobDefOf.Harvest);
            Assert.IsType<WorkGiver_GrowerSow>(DefDatabase<WorkGiverDef>.GetNamed("GrowerSow").Worker);
            Assert.IsType<WorkGiver_GrowerHarvest>(DefDatabase<WorkGiverDef>.GetNamed("GrowerHarvest").Worker);
        }

        // ---- ZoneManager ----

        [Fact]
        public void ZoneManager_enforces_one_zone_per_cell()
        {
            CoreMap map = NewMap(5, 5);
            var a = new Zone_Stockpile();
            var b = new Zone_Stockpile();
            map.zoneManager.RegisterZone(a);
            map.zoneManager.RegisterZone(b);

            var cell = new IntVec3(2, 0, 2);
            Assert.True(map.zoneManager.AddCell(a, cell));
            Assert.Same(a, map.zoneManager.ZoneAt(cell));

            // b cannot also claim a's cell.
            Assert.False(map.zoneManager.AddCell(b, cell));
            Assert.Same(a, map.zoneManager.ZoneAt(cell));

            // Freeing it from a lets b claim it.
            map.zoneManager.RemoveCell(a, cell);
            Assert.Null(map.zoneManager.ZoneAt(cell));
            Assert.True(map.zoneManager.AddCell(b, cell));
            Assert.Same(b, map.zoneManager.ZoneAt(cell));
        }

        [Fact]
        public void DeregisterZone_frees_every_cell_it_held()
        {
            CoreMap map = NewMap(5, 5);
            var zone = new Zone_Growing();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(1, 0, 1));
            map.zoneManager.AddCell(zone, new IntVec3(1, 0, 2));

            map.zoneManager.DeregisterZone(zone);

            Assert.Null(map.zoneManager.ZoneAt(new IntVec3(1, 0, 1)));
            Assert.Null(map.zoneManager.ZoneAt(new IntVec3(1, 0, 2)));
            Assert.DoesNotContain(zone, map.zoneManager.AllZones);
        }

        // ---- Scribe ----

        [Fact]
        public void Zone_growing_round_trips_through_scribe_with_its_cells_and_plant_def()
        {
            CoreMap map = NewMap(6, 6);
            var zone = new Zone_Growing { label = "North field", plantDefToGrow = Def("Plant_Potato"), allowSow = true };
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(1, 0, 1));
            map.zoneManager.AddCell(zone, new IntVec3(2, 0, 1));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Zone_Growing? loadedZone = loaded.zoneManager.AllZones.OfType<Zone_Growing>().FirstOrDefault();
            Assert.NotNull(loadedZone);
            Assert.Equal("North field", loadedZone!.label);
            Assert.Equal("Plant_Potato", loadedZone.plantDefToGrow?.defName);
            Assert.True(loadedZone.allowSow);
            Assert.Equal(2, loadedZone.CellCount);
            Assert.Same(loadedZone, loaded.zoneManager.ZoneAt(new IntVec3(1, 0, 1)));
            Assert.Same(loadedZone, loaded.zoneManager.ZoneAt(new IntVec3(2, 0, 1)));
        }

        [Fact]
        public void Zone_stockpile_filter_round_trips_through_scribe()
        {
            CoreMap map = NewMap(6, 6);
            var zone = new Zone_Stockpile();
            zone.filter.SetAllow(Def("WoodLog"), true);
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(3, 0, 3));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Zone_Stockpile? loadedZone = loaded.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault();
            Assert.NotNull(loadedZone);
            Assert.True(loadedZone!.filter.Allows(Def("WoodLog")));
            Assert.False(loadedZone.filter.Allows(Def("Steel")));
        }

        [Fact]
        public void Home_area_round_trips_through_scribe()
        {
            CoreMap map = NewMap(6, 6);
            map.areaManager.Home[new IntVec3(2, 0, 2)] = true;
            map.areaManager.Home[new IntVec3(3, 0, 3)] = true;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(2, loaded.areaManager.Home.TrueCount);
            Assert.True(loaded.areaManager.Home[new IntVec3(2, 0, 2)]);
            Assert.True(loaded.areaManager.Home[new IntVec3(3, 0, 3)]);
            Assert.False(loaded.areaManager.Home[new IntVec3(0, 0, 0)]);
        }

        // ---- Wiring: growing zone -> real AI work ----

        [Fact]
        public void Growing_zone_with_no_plants_produces_sowing_work()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            var zone = new Zone_Growing { plantDefToGrow = Def("Plant_Potato") };
            map.zoneManager.RegisterZone(zone);
            var cell = new IntVec3(4, 0, 4);
            map.zoneManager.AddCell(zone, cell);

            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Plant));

            RunTicks(3000, pawn);

            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            Assert.Single(plants);
            Assert.IsType<Plant>(plants[0]);
            Assert.Equal("Plant_Potato", plants[0].def.defName);
            Assert.Equal(cell, plants[0].Position);
            Assert.True(((Plant)plants[0]).Growth < 1f, "A freshly sown plant should not start fully grown.");
        }

        [Fact]
        public void A_fully_grown_plant_produces_harvest_work_that_yields_its_crop()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            var plant = (Plant)ThingMaker.MakeThing(Def("Plant_Potato"));
            plant.Growth = 1f;
            var cell = new IntVec3(4, 0, 4);
            GenSpawn.Spawn(plant, cell, map);

            RunTicks(3000, pawn);

            Assert.True(plant.Destroyed, "The mature plant should have been harvested (destroyed) by now.");
            IReadOnlyList<Thing> potatoes = map.listerThings.ThingsOfDef(Def("RawPotatoes"));
            Assert.Single(potatoes);
            Assert.True(potatoes[0].stackCount > 0);
        }

        [Fact]
        public void Growing_zone_with_sowing_disallowed_produces_no_sowing_work()
        {
            CoreMap map = NewMap(8, 8);
            Pawn pawn = SpawnHuman(map, new IntVec3(0, 0, 0));

            var zone = new Zone_Growing { plantDefToGrow = Def("Plant_Potato"), allowSow = false };
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, new IntVec3(4, 0, 4));

            RunTicks(1000, pawn);

            Assert.Empty(map.listerThings.ThingsInGroup(ThingRequestGroup.Plant));
        }
    }
}
