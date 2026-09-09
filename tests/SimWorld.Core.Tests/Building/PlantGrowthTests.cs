using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>Plant growth from fertility × light × temperature (system 16: Building — plant growth).</summary>
    public class PlantGrowthTests : ContentTestBase
    {
        public PlantGrowthTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Plant SpawnPlant(CoreMap map, IntVec3 cell, string defName = "Plant_Potato")
        {
            var plant = (Plant)ThingMaker.MakeThing(Def(defName));
            plant.Growth = 0.1f;
            GenSpawn.Spawn(plant, cell, map);
            return plant;
        }

        // ---- PlantUtility factors, in isolation ----

        [Fact]
        public void Light_factor_is_zero_at_midnight_and_full_at_noon()
        {
            Assert.Equal(0f, PlantUtility.GrowthRateFactor_Light(0));
            Assert.Equal(1f, PlantUtility.GrowthRateFactor_Light(GenDate.TicksPerHour * 12));
        }

        [Fact]
        public void Light_factor_ramps_monotonically_through_dawn()
        {
            float night = PlantUtility.GrowthRateFactor_Light(GenDate.TicksPerHour * 5);
            float dawn = PlantUtility.GrowthRateFactor_Light(GenDate.TicksPerHour * 7);
            float day = PlantUtility.GrowthRateFactor_Light(GenDate.TicksPerHour * 10);
            Assert.True(night < dawn, "Growth light should rise from night into dawn.");
            Assert.True(dawn < day, "Growth light should keep rising from dawn into full day.");
        }

        [Fact]
        public void Temperature_factor_is_zero_outside_the_grow_range_and_full_in_the_optimal_band()
        {
            Assert.Equal(0f, PlantUtility.GrowthRateFactor_Temperature(-10f));
            Assert.Equal(0f, PlantUtility.GrowthRateFactor_Temperature(70f));
            Assert.Equal(1f, PlantUtility.GrowthRateFactor_Temperature(20f));
        }

        [Fact]
        public void Temperature_factor_ramps_monotonically_either_side_of_the_optimal_band()
        {
            float cold = PlantUtility.GrowthRateFactor_Temperature(2f);
            float cool = PlantUtility.GrowthRateFactor_Temperature(8f);
            float optimal = PlantUtility.GrowthRateFactor_Temperature(20f);
            float hot = PlantUtility.GrowthRateFactor_Temperature(50f);
            float scorching = PlantUtility.GrowthRateFactor_Temperature(56f);
            Assert.True(cold < cool);
            Assert.True(cool < optimal);
            Assert.True(optimal > hot);
            Assert.True(hot > scorching);
        }

        // ---- Plant.TickLong: the three-factor product ----

        [Fact]
        public void A_plant_does_not_grow_in_freezing_temperature_even_at_noon_on_rich_soil()
        {
            CoreMap map = NewMap(5, 5);
            map.outdoorTemperature = -20f;
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12); // noon: full light
            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            float before = plant.Growth;

            plant.TickLong();

            Assert.Equal(before, plant.Growth);
        }

        [Fact]
        public void A_plant_grows_faster_on_more_fertile_terrain_all_else_equal()
        {
            float GrowthAfterOneTick(TerrainDef terrain)
            {
                CoreMap map = NewMap(5, 5);
                map.outdoorTemperature = 21f;
                map.terrainGrid.SetTerrain(new IntVec3(2, 0, 2), terrain);
                Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12);
                Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
                float before = plant.Growth;
                plant.TickLong();
                return plant.Growth - before;
            }

            float onGravel = GrowthAfterOneTick(DefDatabase<TerrainDef>.GetNamed("Gravel")); // fertility 0
            float onSoil = GrowthAfterOneTick(SimWorld.Map.TerrainDefOf.Soil);
            float onRichSoil = GrowthAfterOneTick(DefDatabase<TerrainDef>.GetNamed("SoilRich"));

            Assert.Equal(0f, onGravel);
            Assert.True(onSoil > onGravel);
            Assert.True(onRichSoil > onSoil, "Richer soil (higher TerrainDef.fertility) should grow a plant faster.");
        }

        [Fact]
        public void A_plant_does_not_grow_at_night_even_on_fertile_soil_at_a_good_temperature()
        {
            CoreMap map = NewMap(5, 5);
            map.outdoorTemperature = 21f;
            Find.TickManager.DebugSetTicksGame(0); // midnight
            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            float before = plant.Growth;

            plant.TickLong();

            Assert.Equal(before, plant.Growth);
        }

        [Fact]
        public void Growth_never_exceeds_full_and_a_fully_grown_plant_stops_advancing()
        {
            CoreMap map = NewMap(5, 5);
            map.outdoorTemperature = 21f;
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerHour * 12);
            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            plant.Growth = 0.999f;

            for (int i = 0; i < 5; i++) plant.TickLong();

            Assert.Equal(1f, plant.Growth);
            Assert.True(plant.FullyGrown);
            Assert.True(plant.HarvestableNow);
        }

        // ---- Harvest ----

        [Fact]
        public void Harvesting_a_fully_grown_plant_destroys_it_and_spawns_its_full_yield()
        {
            CoreMap map = NewMap(5, 5);
            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            plant.Growth = 1f;

            plant.Harvest(null);

            Assert.True(plant.Destroyed);
            Thing yield = map.listerThings.ThingsOfDef(Def("RawPotatoes"))[0];
            Assert.Equal(Def("Plant_Potato").plant!.harvestYield, yield.stackCount);
        }

        [Fact]
        public void Harvesting_a_partially_grown_plant_yields_proportionally_less()
        {
            CoreMap map = NewMap(5, 5);
            Plant fullyGrown = SpawnPlant(map, new IntVec3(1, 0, 1));
            fullyGrown.Growth = 1f;
            Plant halfGrown = SpawnPlant(map, new IntVec3(3, 0, 3));
            halfGrown.Growth = 0.5f;

            fullyGrown.Harvest(null);
            halfGrown.Harvest(null);

            int fullYield = map.listerThings.ThingsOfDef(Def("RawPotatoes"))[0].stackCount;
            int halfYield = map.listerThings.ThingsOfDef(Def("RawPotatoes"))[1].stackCount;
            Assert.True(halfYield < fullYield, "A plant harvested at half growth should yield less than one harvested fully grown.");
        }

        [Fact]
        public void Wild_plant_is_not_harvestable_for_yield()
        {
            CoreMap map = NewMap(5, 5);
            var plant = (Plant)ThingMaker.MakeThing(Def("WildPlant"));
            GenSpawn.Spawn(plant, new IntVec3(2, 0, 2), map);

            Assert.True(plant.FullyGrown); // scattered as pre-established scrub
            Assert.False(plant.HarvestableNow);
        }

        // ---- Scribe ----

        [Fact]
        public void Plant_growth_round_trips_through_scribe()
        {
            CoreMap map = NewMap(5, 5);
            Plant plant = SpawnPlant(map, new IntVec3(2, 0, 2));
            plant.Growth = 0.42f;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out System.Collections.Generic.IReadOnlyList<string> errors);
            Assert.Empty(errors);

            var loadedPlant = (Plant)loaded.thingGrid.ThingsListAt(new IntVec3(2, 0, 2))[0];
            Assert.Equal(0.42f, loadedPlant.Growth);
        }
    }
}
