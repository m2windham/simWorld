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
    /// Undergrowth growing back at the pace its biome sets (<see cref="BiomeDef.wildPlantRegrowDays"/>).
    /// Map generation scattered wild plants once and nothing ever replaced one: a cut, burnt or built-over
    /// map stayed bare forever, and the eleven land biomes' regrow times — 8 days in a rainforest, 80 in an
    /// extreme desert — were read by no line of the core.
    ///
    /// <para/>Every assertion here is a trend, a band or a round trip. The rate model (a map's worth of
    /// plants spread evenly over its biome's regrow window) is this port's own reading of the field, so what
    /// is pinned is that a rainforest beats a desert, that a cleared map really does come back within its own
    /// window, and that it stops at the density its tile supports — never a plant count.
    ///
    /// <para/>"Map" is qualified <c>global::</c> throughout: <c>SimWorld.Tests.Map</c> exists elsewhere in the
    /// suite and would otherwise shadow the type.
    /// </summary>
    public class WildPlantRegrowthTests : ContentTestBase
    {
        public WildPlantRegrowthTests(CoreContentFixture content) : base(content)
        {
        }

        private const int DefaultTileId = 5;

        /// <summary>A world with nothing on it but the grid, made current — the spawner resolves a map's tile
        /// through <see cref="Find.World"/> exactly as a running game does, so there has to be one.</summary>
        private static global::SimWorld.World.World NewWorld(string seed)
        {
            var world = new global::SimWorld.World.World(
                new WorldInfo { name = "Test", seedString = seed, seed = 11, subdivisionLevel = 2 },
                WorldGrid.Generate(2));
            Find.World = world;
            return world;
        }

        /// <summary>A generated map on <paramref name="world"/>'s tile <paramref name="tileId"/>, painted to
        /// the named biome. Two maps compared against each other must sit on two tiles of the <em>same</em>
        /// world: there is one current world, and both would otherwise resolve the same tile.</summary>
        private static (global::SimWorld.Map.Map map, Tile tile) MapOn(
            global::SimWorld.World.World world, string biomeName, string seed, int tileId = DefaultTileId, int size = 60, float timber = 0.4f)
        {
            Tile tile = world.grid.Tiles[tileId];
            tile.biome = DefDatabase<BiomeDef>.GetNamed(biomeName);
            tile.hilliness = Hilliness.Flat;
            tile.elevation = 100f;
            tile.rainfall = 1200f;
            tile.deposits.Add(new TileDeposit(DepositDefOf.Timber, timber));

            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(
                tile, tileId, seed, new global::SimWorld.Map.IntVec2(size, size));
            return (map, tile);
        }

        private static (global::SimWorld.Map.Map map, Tile tile) MapOn(string biomeName, string seed, int size = 60, float timber = 0.4f) =>
            MapOn(NewWorld(seed), biomeName, seed, DefaultTileId, size, timber);

        private static int PlantCount(global::SimWorld.Map.Map map) =>
            map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant).Count;

        private static void ClearPlants(global::SimWorld.Map.Map map)
        {
            foreach (Thing plant in map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant).ToList())
            {
                plant.Destroy();
            }
        }

        /// <summary>Advances the clock and runs only the spawner — the rest of <c>MapTick</c> (weather, power,
        /// rooms) is another module's business and would make a multi-day window unaffordable to simulate.</summary>
        private static void RunSpawnerTicks(int ticks, params global::SimWorld.Map.Map[] maps)
        {
            int start = Find.TickManager.TicksGame;
            for (int t = 1; t <= ticks; t++)
            {
                Find.TickManager.DebugSetTicksGame(start + t);
                foreach (global::SimWorld.Map.Map map in maps) WildPlantSpawner.WildPlantSpawnerTick(map);
            }
        }

        private static int RegrowWindowTicks(string biomeName) =>
            (int)(DefDatabase<BiomeDef>.GetNamed(biomeName).wildPlantRegrowDays * GenDate.TicksPerDay);

        // ----- the target density is generation's own -----

        [Fact]
        public void The_density_regrowth_aims_at_is_the_one_generation_placed()
        {
            (global::SimWorld.Map.Map map, Tile tile) = MapOn("TemperateForest", "plants-target");

            int desired = WildPlantSpawner.DesiredWildPlantCount(map, tile);
            int generated = PlantCount(map);

            Assert.True(desired > 0);
            Assert.InRange(generated, desired / 2, desired);
        }

        // ----- regrowth happens, at the biome's pace -----

        [Fact]
        public void A_cleared_map_grows_its_undergrowth_back()
        {
            (global::SimWorld.Map.Map map, Tile tile) = MapOn("TemperateForest", "plants-regrow");
            int desired = WildPlantSpawner.DesiredWildPlantCount(map, tile);

            ClearPlants(map);
            Assert.Equal(0, PlantCount(map));

            RunSpawnerTicks(RegrowWindowTicks("TemperateForest"), map);

            int regrown = PlantCount(map);
            Assert.True(regrown > 0, "A cleared temperate forest should have grown something back within its own regrow window.");
            Assert.True(regrown <= desired, $"Regrowth stopped nowhere: {regrown} plants against a supported density of {desired}.");
            Assert.True(regrown >= desired / 3, $"A whole regrow window brought back only {regrown} of {desired}.");
        }

        [Fact]
        public void A_rainforest_comes_back_faster_than_a_desert()
        {
            global::SimWorld.World.World world = NewWorld("plants-race");
            (global::SimWorld.Map.Map rainforest, Tile rainforestTile) = MapOn(world, "TropicalRainforest", "plants-rainforest", tileId: 5);
            (global::SimWorld.Map.Map desert, Tile desertTile) = MapOn(world, "Desert", "plants-desert", tileId: 40);

            Assert.True(
                DefDatabase<BiomeDef>.GetNamed("TropicalRainforest").wildPlantRegrowDays
                < DefDatabase<BiomeDef>.GetNamed("Desert").wildPlantRegrowDays,
                "Content is expected to give a rainforest the shorter regrow time.");
            Assert.True(
                WildPlantSpawner.SpawnChancePerTick(WildPlantSpawner.DesiredWildPlantCount(rainforest, rainforestTile), rainforestTile.biome!.wildPlantRegrowDays)
                > WildPlantSpawner.SpawnChancePerTick(WildPlantSpawner.DesiredWildPlantCount(desert, desertTile), desertTile.biome!.wildPlantRegrowDays));

            ClearPlants(rainforest);
            ClearPlants(desert);

            // Both maps tick on the same clock, so this is a like-for-like race rather than two runs.
            RunSpawnerTicks(GenDate.TicksPerDay * 2, rainforest, desert);

            int rainforestPlants = PlantCount(rainforest);
            int desertPlants = PlantCount(desert);
            Assert.True(rainforestPlants > 0, "Two days should be plenty for a rainforest to start coming back.");
            Assert.True(
                rainforestPlants > desertPlants,
                $"A rainforest ({rainforestPlants}) should outgrow a desert ({desertPlants}) over the same two days.");
        }

        [Fact]
        public void Regrowth_stops_at_the_density_the_tile_supports_and_never_passes_it()
        {
            (global::SimWorld.Map.Map map, Tile tile) = MapOn("TemperateForest", "plants-ceiling");
            int desired = WildPlantSpawner.DesiredWildPlantCount(map, tile);

            ClearPlants(map);
            RunSpawnerTicks(RegrowWindowTicks("TemperateForest") * 3, map);

            Assert.InRange(PlantCount(map), desired * 3 / 4, desired);
        }

        [Fact]
        public void A_regrown_plant_starts_as_a_seedling_on_open_fertile_ground()
        {
            (global::SimWorld.Map.Map map, _) = MapOn("TropicalRainforest", "plants-seedling");
            ClearPlants(map);

            RunSpawnerTicks(GenDate.TicksPerDay, map);

            IReadOnlyList<Thing> regrown = map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant);
            Assert.NotEmpty(regrown);
            foreach (Thing thing in regrown)
            {
                var plant = Assert.IsType<Plant>(thing);
                Assert.True(plant.Growth < 1f, "A plant that just seeded itself should not be fully grown.");
                Assert.False(map.roofGrid.Roofed(plant.Position));
                Assert.Null(map.edificeGrid[plant.Position]);
                Assert.True(map.terrainGrid.TerrainAt(plant.Position).fertility > 0f);
            }
        }

        /// <summary>
        /// The wiring itself, and the one test here that would still pass if the spawner were only ever
        /// called by its own tests: this one goes through <see cref="global::SimWorld.Map.Map.MapTick"/>, the
        /// call a running game actually makes (<c>Sim.Game.WireTickHooks</c> registers <c>TickMaps</c>).
        /// Without that line in <c>MapTick</c> a map in a live game regrows nothing, however green this file
        /// looks.
        /// </summary>
        [Fact]
        public void The_map_tick_a_running_game_makes_is_what_regrows_the_plants()
        {
            (global::SimWorld.Map.Map map, _) = MapOn("TropicalRainforest", "plants-maptick");
            ClearPlants(map);

            for (int i = 0; i < GenDate.TicksPerDay / 2; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }

            Assert.True(PlantCount(map) > 0, "Half a day of real map ticks in a rainforest should have grown something back.");
        }

        // ----- determinism -----

        /// <summary>
        /// Two runs of the same map and the same ticks regrow the same plants in the same cells, and neither
        /// touches the ambient <see cref="Rand.Current"/> stream — a per-tick draw taken from it would shift
        /// every other system's sequence for the rest of the game.
        /// </summary>
        [Fact]
        public void Regrowth_is_reproducible_and_draws_nothing_from_the_shared_random_stream()
        {
            (global::SimWorld.Map.Map map, _) = MapOn("TemperateForest", "plants-determinism");
            var runs = new List<List<global::SimWorld.Map.IntVec3>>();

            // The biome's whole regrow window, not one day of it. The window is by definition the span a
            // map's worth of plants comes back over, so it is the span that reliably grows something; one
            // day of a temperate forest expects ~1.4 plants, and a Poisson zero there is an ordinary
            // outcome rather than a defect. That mattered because the spawner seeds its rolls from
            // map.uniqueID, which is allocated from a process-global counter — so which rolls this map got
            // depended on how many Maps any earlier test in the run happened to have made, and the
            // NotEmpty guard below flipped red or green on nothing but test ordering. Widening the span
            // takes the guard out of that lottery without weakening what the test is actually for.
            int window = RegrowWindowTicks("TemperateForest");
            for (int run = 0; run < 2; run++)
            {
                ClearPlants(map);
                Find.TickManager.DebugSetTicksGame(0);

                Rand.Current = new RandomStream(99);
                uint before = Rand.Current.Iterations;
                RunSpawnerTicks(window, map);
                Assert.Equal(before, Rand.Current.Iterations);

                runs.Add(map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant)
                    .Select(t => t.Position)
                    .OrderBy(p => p.x).ThenBy(p => p.z)
                    .ToList());
            }

            Assert.NotEmpty(runs[0]);
            Assert.Equal(runs[0], runs[1]);
        }

        [Fact]
        public void A_map_with_no_world_tile_grows_nothing()
        {
            var map = new global::SimWorld.Map.Map(30, 30, global::SimWorld.Map.TerrainDefOf.Soil);
            Assert.Equal(-1, map.tile);

            RunSpawnerTicks(GenDate.TicksPerDay, map);

            Assert.Equal(0, PlantCount(map));
        }

        // ----- Scribe -----

        [Fact]
        public void Scribe_round_trip_keeps_the_plants_that_grew_back()
        {
            (global::SimWorld.Map.Map map, _) = MapOn("TemperateForest", "plants-scribe", size: 35);
            ClearPlants(map);
            RunSpawnerTicks(RegrowWindowTicks("TemperateForest"), map);
            Assert.NotEmpty(map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant));

            string xml = Scribe.SaveToString(map, "map");
            global::SimWorld.Map.Map loaded = Scribe.Load<global::SimWorld.Map.Map>(xml, "map", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            var before = map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant)
                .Select(t => (t.Position, ((Plant)t).Growth)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            var after = loaded.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant)
                .Select(t => (t.Position, ((Plant)t).Growth)).OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();
            Assert.Equal(before, after);
        }
    }
}
