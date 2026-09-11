using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.MapGen;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Food growing wild on a map, at the density its biome's <see cref="BiomeDef.forageability"/> promises.
    ///
    /// <para/><b>What these exist to keep closed.</b> A generated interior used to carry no nutrition at all:
    /// the only plant map generation scattered was <c>WildPlant</c>, which has no
    /// <see cref="PlantProperties.harvestedThingDef"/> and no ingestible block, so <c>Plant.HarvestableNow</c>
    /// was false for every plant on every map outside a growing zone — and nothing in <c>src/</c> created one
    /// of those either. <c>forageability</c>, authored for all eleven land biomes, was read by one line of the
    /// core and that line uses it to decide where to paint sand. A settlement founded the ordinary way had
    /// nothing whatsoever to eat.
    ///
    /// <para/>Assertions are trends and bands, never counts: the density constant is this port's own stand-in
    /// for RimWorld's per-species <c>BiomeDef.wildPlants</c> table, which this port's BiomeDef does not carry.
    /// What is pinned is that a forest bears more food than a desert, that an ice sheet bears none, that what
    /// is there is genuinely harvestable into genuinely edible items, and that it comes back.
    ///
    /// <para/>"Map" is qualified <c>global::</c> throughout: <c>SimWorld.Tests.Map</c> exists elsewhere in the
    /// suite and would otherwise shadow the type.
    /// </summary>
    public class WildFoodTests : ContentTestBase
    {
        public WildFoodTests(CoreContentFixture content) : base(content)
        {
        }

        private const int DefaultTileId = 5;

        private static global::SimWorld.World.World NewWorld(string seed)
        {
            var world = new global::SimWorld.World.World(
                new WorldInfo { name = "Test", seedString = seed, seed = 11, subdivisionLevel = 2 },
                WorldGrid.Generate(2));
            Find.World = world;
            return world;
        }

        private static (global::SimWorld.Map.Map map, Tile tile) MapOn(
            global::SimWorld.World.World world, string biomeName, string seed, int tileId = DefaultTileId, int size = 80)
        {
            Tile tile = world.grid.Tiles[tileId];
            tile.biome = DefDatabase<BiomeDef>.GetNamed(biomeName);
            tile.hilliness = Hilliness.Flat;
            tile.elevation = 100f;
            tile.rainfall = 1200f;
            tile.deposits.Add(new TileDeposit(DepositDefOf.Timber, 0.4f));

            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(
                tile, tileId, seed, new global::SimWorld.Map.IntVec2(size, size));
            return (map, tile);
        }

        private static int FoodPlantCount(global::SimWorld.Map.Map map) =>
            map.listerThings.ThingsOfDef(WildFoodDefOf.Plant_Berry).Count;

        [Fact]
        public void A_generated_map_carries_food_a_settlement_could_actually_eat()
        {
            // The headline: before this, nutrition on a freshly generated interior was exactly zero.
            global::SimWorld.World.World world = NewWorld("wildfood-exists");
            (global::SimWorld.Map.Map map, _) = MapOn(world, "TemperateForest", "wildfood-exists");

            Assert.True(FoodPlantCount(map) > 0, "a temperate forest generated with no food growing on it");

            float nutritionStanding = map.listerThings.ThingsOfDef(WildFoodDefOf.Plant_Berry)
                .OfType<Plant>()
                .Where(p => p.HarvestableNow)
                .Sum(p => Yield(p));
            Assert.True(nutritionStanding > 0f, "the food plants on the map yield no nutrition when harvested");
        }

        private static float Yield(Plant plant)
        {
            PlantProperties props = plant.def.plant!;
            return props.harvestYield * props.harvestedThingDef!.ingestible!.nutrition;
        }

        [Fact]
        public void The_plant_a_map_bears_is_harvestable_into_something_edible()
        {
            // Two separate claims that both have to hold, and each of which has been false in this codebase
            // at some point: the plant yields an item, and the item feeds a person.
            ThingDef bush = WildFoodDefOf.Plant_Berry;
            Assert.NotNull(bush.plant);
            ThingDef? harvested = bush.plant!.harvestedThingDef;
            Assert.NotNull(harvested);
            Assert.True(harvested!.IsNutritionGivingIngestible, harvested.defName + " is not nutrition-giving");
            Assert.True(harvested.EverHaulable, harvested.defName + " cannot be hauled into a granary");
        }

        [Fact]
        public void A_richer_land_bears_more_food_than_a_poorer_one()
        {
            // The trend the forageability field exists to express, asserted across two real biomes rather
            // than against a constant.
            global::SimWorld.World.World world = NewWorld("wildfood-trend");
            (global::SimWorld.Map.Map forest, _) = MapOn(world, "TemperateForest", "wildfood-trend", tileId: 5);
            (global::SimWorld.Map.Map desert, _) = MapOn(world, "Desert", "wildfood-trend", tileId: 9);

            Assert.True(FoodPlantCount(forest) > FoodPlantCount(desert),
                "a temperate forest bore no more wild food than a desert");
        }

        [Fact]
        public void A_land_where_nothing_edible_grows_bears_none()
        {
            global::SimWorld.World.World world = NewWorld("wildfood-ice");
            (global::SimWorld.Map.Map ice, _) = MapOn(world, "IceSheet", "wildfood-ice");

            Assert.Equal(0, FoodPlantCount(ice));
        }

        [Fact]
        public void Foraged_ground_grows_back()
        {
            // A settlement that strips its own wilderness bare must not have destroyed it: the same regrow
            // model the undergrowth already uses, applied to the half of it that feeds people.
            global::SimWorld.World.World world = NewWorld("wildfood-regrow");
            (global::SimWorld.Map.Map map, Tile tile) = MapOn(world, "TemperateForest", "wildfood-regrow");

            foreach (Thing plant in map.listerThings.ThingsOfDef(WildFoodDefOf.Plant_Berry).ToList()) plant.Destroy();
            Assert.Equal(0, FoodPlantCount(map));

            int desired = WildPlantSpawner.DesiredWildFoodPlantCount(map, tile);
            Assert.True(desired > 0);

            // A quarter of the biome's own regrow window is enough to see it moving, without paying for the
            // whole window in a test.
            int ticks = (int)(tile.biome!.wildPlantRegrowDays * GenDate.TicksPerDay / 4);
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                WildPlantSpawner.WildPlantSpawnerTick(map);
            }

            Assert.True(FoodPlantCount(map) > 0, "a stripped map grew no food back inside a quarter of its own regrow window");
            Assert.True(FoodPlantCount(map) <= desired, "regrowth ran past the density the tile supports");
        }

        [Fact]
        public void The_same_seed_bears_food_in_the_same_cells()
        {
            // Determinism is a feature, and adding a second scatter to a generation step that was already
            // drawing from a seeded stream is exactly where it gets lost quietly.
            global::SimWorld.World.World world = NewWorld("wildfood-determinism");
            (global::SimWorld.Map.Map first, _) = MapOn(world, "TemperateForest", "wildfood-determinism");
            var cellsFirst = new HashSet<global::SimWorld.Map.IntVec3>(
                map0Positions(first));

            (global::SimWorld.Map.Map second, _) = MapOn(world, "TemperateForest", "wildfood-determinism");
            var cellsSecond = new HashSet<global::SimWorld.Map.IntVec3>(map0Positions(second));

            Assert.NotEmpty(cellsFirst);
            Assert.Equal(cellsFirst.Count, cellsSecond.Count);
            Assert.True(cellsFirst.SetEquals(cellsSecond), "two generations of the same seed bore food in different cells");

            static IEnumerable<global::SimWorld.Map.IntVec3> map0Positions(global::SimWorld.Map.Map m) =>
                m.listerThings.ThingsOfDef(WildFoodDefOf.Plant_Berry).Select(t => t.Position);
        }
    }
}
