using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// Wildlife on a map: the population a tile's <see cref="BiomeDef.animalDensity"/> supports, placed at
    /// generation by <see cref="GenStep_Animals"/> and kept there by <see cref="WildAnimalSpawner"/>.
    ///
    /// <para/><b>What was broken.</b> Nothing anywhere in this codebase had ever spawned a wild animal on an
    /// interior map, so the whole animals module above it — hunting, taming, training, husbandry, all built
    /// and green — could never start on a generated map: <see cref="WorkGiver_Hunt"/> and
    /// <see cref="WorkGiver_TameAnimals"/> both scan the map's pawns for an unowned animal and both found
    /// none, for ever. <see cref="BiomeDef.animalDensity"/> was authored for all fourteen shipped biomes and
    /// read by exactly one line of the core, which uses it to decide where to put deposits.
    ///
    /// <para/><b>Every assertion here is a trend, a band or a round trip.</b> The density model (a weight
    /// budget per map area, scaled by the biome's own figure, refilled over a fixed window) is this port's
    /// own reading of a RimWorld field whose exact constants are not sourceable here, so what is pinned is
    /// that a rainforest carries more game than a desert, that a cleared map comes back within its own
    /// window and then stops, and that generating twice gives the same animals — never an animal count.
    ///
    /// <para/>"Map" is qualified <c>global::</c> throughout: <c>SimWorld.Tests.Map</c> exists elsewhere in
    /// the suite and would otherwise shadow the type.
    /// </summary>
    public class WildAnimalSpawnerTests : ContentTestBase
    {
        public WildAnimalSpawnerTests(CoreContentFixture content) : base(content)
        {
        }

        private const int DefaultTileId = 5;
        private const int DefaultSize = 200;

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

        private static (global::SimWorld.Map.Map map, Tile tile) MapOn(
            global::SimWorld.World.World world, string biomeName, string seed, int tileId = DefaultTileId, int size = DefaultSize)
        {
            Tile tile = world.grid.Tiles[tileId];
            tile.biome = DefDatabase<BiomeDef>.GetNamed(biomeName);
            tile.hilliness = Hilliness.Flat;
            tile.elevation = 100f;
            tile.rainfall = 1200f;

            global::SimWorld.Map.Map map = MapGenerator.GenerateMap(
                tile, tileId, seed, new global::SimWorld.Map.IntVec2(size, size));
            return (map, tile);
        }

        private static (global::SimWorld.Map.Map map, Tile tile) MapOn(string biomeName, string seed, int size = DefaultSize) =>
            MapOn(NewWorld(seed), biomeName, seed, DefaultTileId, size);

        private static List<Pawn> WildAnimalsOn(global::SimWorld.Map.Map map) =>
            map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal && p.faction == null && !p.Dead).ToList();

        private static void ClearAnimals(global::SimWorld.Map.Map map)
        {
            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal).ToList())
            {
                animal.DeSpawn();
            }
        }

        /// <summary>Advances the clock and runs only the spawner — the rest of <c>MapTick</c> is other
        /// modules' business and would make a multi-day window unaffordable to simulate.</summary>
        private static void RunSpawnerTicks(int ticks, global::SimWorld.Map.Map map)
        {
            int start = Find.TickManager.TicksGame;
            for (int t = 1; t <= ticks; t++)
            {
                Find.TickManager.DebugSetTicksGame(start + t);
                WildAnimalSpawner.WildAnimalSpawnerTick(map);
            }
        }

        private static int RepopulateWindowTicks() => (int)(WildAnimalTuning.RepopulateDays * GenDate.TicksPerDay);

        // ---- generation ----

        [Fact]
        public void A_generated_map_carries_the_wildlife_its_biome_promises()
        {
            var (map, tile) = MapOn("TemperateForest", "gen-stock");

            List<Pawn> animals = WildAnimalsOn(map);
            float desired = WildAnimalSpawner.DesiredAnimalWeight(map, tile);

            Assert.True(desired > 0f, "a temperate forest is authored with animalDensity above zero");
            Assert.NotEmpty(animals);

            // A band, not a count: stocking fills a weight budget with whatever kinds it draws, so the last
            // animal placed can overshoot by its own weight, and a map with little open ground can come up
            // short of the budget when its cell tries miss.
            float standing = WildAnimalSpawner.CurrentAnimalWeight(map);
            Assert.InRange(standing, desired * 0.5f, desired + HeaviestKindWeight());
        }

        [Fact]
        public void A_richer_biome_carries_more_game_than_a_poorer_one()
        {
            global::SimWorld.World.World world = NewWorld("gen-trend");
            var (rainforest, _) = MapOn(world, "TropicalRainforest", "gen-trend-rf", tileId: 5);
            var (desert, _) = MapOn(world, "Desert", "gen-trend-d", tileId: 6);

            Assert.True(WildAnimalSpawner.CurrentAnimalWeight(rainforest) > WildAnimalSpawner.CurrentAnimalWeight(desert),
                "a tropical rainforest (animalDensity 1.6) should carry more game than a desert (0.3)");
        }

        [Fact]
        public void Land_that_holds_no_animals_is_stocked_with_none()
        {
            // The ocean is authored at animalDensity 0. Run against a real, walkable map so the answer is
            // the biome's and not "there was nowhere to stand".
            var (map, _) = MapOn("TemperateForest", "gen-empty");
            ClearAnimals(map);

            Tile ocean = Find.World!.grid.Tiles[7];
            ocean.biome = DefDatabase<BiomeDef>.GetNamed("Ocean");

            Assert.Equal(0f, WildAnimalTuning.DesiredAnimalWeight(map.cellIndices.NumGridCells, ocean));
            Assert.Equal(0, WildAnimalSpawner.StockMap(map, ocean, seed: 7));
            Assert.Empty(WildAnimalsOn(map));
        }

        [Fact]
        public void Everything_generation_places_is_a_wild_animal_and_lawful_prey()
        {
            var (map, _) = MapOn("BorealForest", "gen-prey");

            List<Pawn> animals = WildAnimalsOn(map);
            Assert.NotEmpty(animals);
            foreach (Pawn animal in animals)
            {
                Assert.Null(animal.faction);
                Assert.True(animal.Spawned);
                Assert.True(HuntUtility.IsHuntableAnimal(animal),
                    animal.kindDef!.defName + " was placed by map generation but is not lawful hunting work");
            }
        }

        [Fact]
        public void Two_generations_of_the_same_tile_are_born_with_the_same_animals()
        {
            var (first, _) = MapOn("TemperateForest", "determinism", size: 120);
            List<(string kind, global::SimWorld.Map.IntVec3 cell)> a = WildAnimalsOn(first)
                .Select(p => (p.kindDef!.defName, p.Position)).OrderBy(t => t.Item2.x).ThenBy(t => t.Item2.z).ToList();

            var (second, _) = MapOn("TemperateForest", "determinism", size: 120);
            List<(string kind, global::SimWorld.Map.IntVec3 cell)> b = WildAnimalsOn(second)
                .Select(p => (p.kindDef!.defName, p.Position)).OrderBy(t => t.Item2.x).ThenBy(t => t.Item2.z).ToList();

            Assert.NotEmpty(a);
            Assert.Equal(a, b);
        }

        // ---- restocking ----

        [Fact]
        public void A_cleared_map_restocks_within_its_own_window()
        {
            var (map, tile) = MapOn("TemperateForest", "restock");
            ClearAnimals(map);
            Assert.Empty(WildAnimalsOn(map));

            RunSpawnerTicks(RepopulateWindowTicks(), map);

            // The land came back. Deliberately not "back to full": the rate is linear and the window is the
            // mean, so a single run lands either side of it — what is pinned is that a hunted-out map is not
            // bare for the rest of the game, which is what it was before this existed.
            Assert.NotEmpty(WildAnimalsOn(map));
            Assert.True(WildAnimalSpawner.CurrentAnimalWeight(map) > 0f);
        }

        [Fact]
        public void A_richer_biome_restocks_toward_more_than_a_poorer_one()
        {
            global::SimWorld.World.World world = NewWorld("restock-trend");
            var (rainforest, _) = MapOn(world, "TropicalRainforest", "restock-rf", tileId: 5);
            var (desert, _) = MapOn(world, "Desert", "restock-d", tileId: 6);
            ClearAnimals(rainforest);
            ClearAnimals(desert);

            int window = RepopulateWindowTicks() * 3;
            int start = Find.TickManager.TicksGame;
            for (int t = 1; t <= window; t++)
            {
                Find.TickManager.DebugSetTicksGame(start + t);
                WildAnimalSpawner.WildAnimalSpawnerTick(rainforest);
                WildAnimalSpawner.WildAnimalSpawnerTick(desert);
            }

            Assert.True(WildAnimalSpawner.CurrentAnimalWeight(rainforest) > WildAnimalSpawner.CurrentAnimalWeight(desert),
                "given the same time, a rainforest should restock toward more game than a desert");
        }

        [Fact]
        public void Restocking_stops_at_what_the_land_supports()
        {
            var (map, tile) = MapOn("TropicalRainforest", "restock-cap");
            ClearAnimals(map);

            // Long enough to place many times the budget if nothing stopped it.
            RunSpawnerTicks(RepopulateWindowTicks() * 5, map);

            float desired = WildAnimalSpawner.DesiredAnimalWeight(map, tile);
            Assert.InRange(WildAnimalSpawner.CurrentAnimalWeight(map), 0f, desired + HeaviestKindWeight());
        }

        [Fact]
        public void A_map_with_no_world_tile_gets_no_wildlife()
        {
            // Most of this suite's maps never sat on a planet; nothing should walk onto one.
            Find.World = null;
            var map = new global::SimWorld.Map.Map(40, 40, TerrainDefOf.Soil);

            RunSpawnerTicks(RepopulateWindowTicks(), map);

            Assert.Empty(WildAnimalsOn(map));
        }

        [Fact]
        public void The_spawner_draws_nothing_from_the_ambient_random_stream()
        {
            // This runs inside the tick loop, where one stray draw would shift every subsequent roll in the
            // game. Pawn generation itself reads the ambient stream, so the spawner pushes and pops a seeded
            // state around it; that is what this asserts — the position is where it started, after a window
            // long enough to have generated animals.
            var (map, _) = MapOn("TropicalRainforest", "determinism-rand");
            ClearAnimals(map);

            Rand.Current = new RandomStream(99);
            uint before = Rand.Current.Iterations;
            RunSpawnerTicks(RepopulateWindowTicks() * 2, map);

            Assert.NotEmpty(WildAnimalsOn(map));
            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void Two_runs_of_the_same_map_restock_identically()
        {
            var (map, _) = MapOn("TemperateForest", "restock-determinism");
            var runs = new List<List<(string kind, global::SimWorld.Map.IntVec3 cell)>>();

            for (int run = 0; run < 2; run++)
            {
                ClearAnimals(map);
                Find.TickManager.DebugSetTicksGame(0);
                Rand.Current = new RandomStream(7);

                RunSpawnerTicks(RepopulateWindowTicks() * 2, map);
                runs.Add(WildAnimalsOn(map).Select(p => (p.kindDef!.defName, p.Position))
                    .OrderBy(t => t.Item2.x).ThenBy(t => t.Item2.z).ToList());
            }

            Assert.NotEmpty(runs[0]);
            Assert.Equal(runs[0], runs[1]);
        }

        // ---- ledger ----

        [Fact]
        public void Somebody_elses_livestock_is_not_the_wilderness()
        {
            // A tamed animal belongs to a faction. It must neither count toward what the land holds nor stop
            // the land restocking — otherwise a settlement that tamed its map's wildlife would have told the
            // wilderness it was full.
            var (map, tile) = MapOn("TemperateForest", "livestock");
            List<Pawn> animals = WildAnimalsOn(map);
            Assert.NotEmpty(animals);

            float wildWeight = WildAnimalSpawner.CurrentAnimalWeight(map);
            animals[0].faction = new Faction(
                DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Herders", "F_Herders");

            Assert.True(WildAnimalSpawner.CurrentAnimalWeight(map) < wildWeight,
                "taming an animal should take it out of the wild population the spawner is measuring");
        }

        // ---- Scribe ----

        [Fact]
        public void A_maps_wildlife_round_trips_through_Scribe_and_is_still_prey()
        {
            var (map, _) = MapOn("TemperateForest", "scribe", size: 80);
            List<(string kind, global::SimWorld.Map.IntVec3 cell)> before = WildAnimalsOn(map)
                .Select(p => (p.kindDef!.defName, p.Position)).OrderBy(t => t.Item2.x).ThenBy(t => t.Item2.z).ToList();
            Assert.NotEmpty(before);

            string xml = Scribe.SaveToString(map, "map");
            var loaded = Scribe.Load<global::SimWorld.Map.Map>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            List<(string kind, global::SimWorld.Map.IntVec3 cell)> after = WildAnimalsOn(loaded)
                .Select(p => (p.kindDef!.defName, p.Position)).OrderBy(t => t.Item2.x).ThenBy(t => t.Item2.z).ToList();
            Assert.Equal(before, after);

            foreach (Pawn animal in WildAnimalsOn(loaded))
            {
                Assert.True(HuntUtility.IsHuntableAnimal(animal),
                    "an animal that was lawful prey before the save is not after the load");
            }
        }

        private static float HeaviestKindWeight() =>
            WildAnimalTuning.WildAnimalKinds().Max(WildAnimalTuning.AnimalWeightOf);
    }
}
