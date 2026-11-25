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
    /// <summary>
    /// The famine harvest: <see cref="WorkGiver_GrowerHarvest"/> takes an unripe food crop past its
    /// <see cref="PlantProperties.harvestMinGrowth"/> only while the settlement is short of food
    /// (<see cref="HuntingInitiative.ShortOfFood(Map.Map)"/>), as a RimWorld player designates an unripe crop
    /// for harvest when the larder runs out before it ripens. Measured on the 60-day bench, band 40 seed 201:
    /// the food ran out on day 40 beside a 1,301-cell rice field at 72% growth, and 8 people starved before
    /// it ripened around day 54.
    /// </summary>
    public class EarlyHarvestTests : ContentTestBase
    {
        public EarlyHarvestTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(12, 12, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static WorkGiver_GrowerHarvest Giver =>
            (WorkGiver_GrowerHarvest)DefDatabase<WorkGiverDef>.GetNamed("GrowerHarvest").Worker;

        /// <summary>A crop standing in a field: the grower's harvest is the field's (wild food is foraged).</summary>
        private static Plant SpawnPlant(CoreMap map, string defName, IntVec3 cell, float growth)
        {
            var plant = (Plant)ThingMaker.MakeThing(Def(defName));
            if (!(map.zoneManager.ZoneAt(cell) is Zone_Growing))
            {
                var field = new Zone_Growing { label = "field " + cell, plantDefToGrow = Def(defName) };
                map.zoneManager.RegisterZone(field);
                map.zoneManager.AddCell(field, cell);
            }
            GenSpawn.Spawn(plant, cell, map);
            plant.Growth = growth;
            return plant;
        }

        private static Pawn SpawnGrower(CoreMap map)
        {
            Pawn p = NewHuman("Grower");
            GenSpawn.Spawn(p, new IntVec3(1, 0, 1), map);
            return p;
        }

        /// <summary>Puts a whole larder (and more) on the map, so the settlement is not short.</summary>
        private static void Feed(CoreMap map)
        {
            float wanted = HuntingInitiative.NutritionWanted(null, map);
            Thing meals = ThingMaker.MakeThing(Def("MealSimple"));
            meals.stackCount = (int)(wanted / meals.def.ingestible!.nutrition) + 5;
            GenSpawn.Spawn(meals, new IntVec3(10, 0, 10), map);
        }

        [Fact]
        public void Food_plants_can_be_harvested_from_RimWorlds_sixty_five_percent()
        {
            foreach (string crop in new[] { "Plant_Rice", "Plant_Potato", "Plant_Berry" })
            {
                PlantProperties props = Def(crop).plant!;
                Assert.True(props.harvestMinGrowth < 1f, crop + " should be harvestable before it is ripe");
                Assert.InRange(props.harvestMinGrowth, 0.6f, 0.7f);
            }
        }

        [Theory]
        [InlineData("Plant_Rice")]
        [InlineData("Plant_Berry")]
        public void A_fed_settlement_waits_for_the_crop_to_ripen(string crop)
        {
            CoreMap map = NewMap();
            Pawn grower = SpawnGrower(map);
            Plant unripe = SpawnPlant(map, crop, new IntVec3(6, 0, 6), 0.8f);
            Plant ripe = SpawnPlant(map, crop, new IntVec3(7, 0, 7), 1f);
            Feed(map);

            Assert.False(HuntingInitiative.ShortOfFood(map));
            Assert.True(unripe.HarvestableNow, "harvestable in principle");
            Assert.False(Giver.HasJobOnThing(grower, unripe), "but a fed settlement leaves an unripe crop standing");
            Assert.True(Giver.HasJobOnThing(grower, ripe), "and harvests a ripe one as always");
        }

        [Theory]
        [InlineData("Plant_Rice")]
        [InlineData("Plant_Berry")]
        public void A_hungry_settlement_harvests_an_unripe_food_crop(string crop)
        {
            CoreMap map = NewMap();
            Pawn grower = SpawnGrower(map);
            Plant unripe = SpawnPlant(map, crop, new IntVec3(6, 0, 6), 0.8f);
            Plant green = SpawnPlant(map, crop, new IntVec3(7, 0, 7), 0.5f);

            Assert.True(HuntingInitiative.ShortOfFood(map));
            Assert.True(Giver.HasJobOnThing(grower, unripe), "past 65% and nothing to eat: harvest it now");
            Assert.False(Giver.HasJobOnThing(grower, green), "below 65% there is nothing to harvest yet");
        }

        [Fact]
        public void Hunger_never_brings_in_an_unripe_crop_that_is_not_food()
        {
            CoreMap map = NewMap();
            Pawn grower = SpawnGrower(map);
            Plant cotton = SpawnPlant(map, "Plant_Cotton", new IntVec3(6, 0, 6), 0.8f);

            Assert.True(HuntingInitiative.ShortOfFood(map));
            Assert.False(Giver.HasJobOnThing(grower, cotton));
        }

        [Fact]
        public void The_early_harvest_stops_once_the_larder_is_back()
        {
            CoreMap map = NewMap();
            Pawn grower = SpawnGrower(map);
            Plant unripe = SpawnPlant(map, "Plant_Rice", new IntVec3(6, 0, 6), 0.8f);

            Assert.True(Giver.HasJobOnThing(grower, unripe));

            Feed(map);
            // The shortage is read once per tick for the whole field; the next search reads it fresh.
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 1);
            Assert.False(Giver.HasJobOnThing(grower, unripe), "the rest of the field keeps growing");
        }

        [Fact]
        public void A_harvest_taken_early_yields_less_than_a_ripe_one()
        {
            CoreMap map = NewMap();
            Plant early = SpawnPlant(map, "Plant_Rice", new IntVec3(3, 0, 3), 0.7f);
            Plant ripe = SpawnPlant(map, "Plant_Rice", new IntVec3(8, 0, 8), 1f);

            early.Harvest(null);
            int earlyYield = map.listerThings.ThingsOfDef(Def("RawRice"))[0].stackCount;
            ripe.Harvest(null);
            int total = 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(Def("RawRice"))) total += t.stackCount;

            Assert.True(earlyYield > 0, "an early harvest still yields food");
            Assert.True(earlyYield < total - earlyYield, "but less than the ripe crop");
        }
    }
}
