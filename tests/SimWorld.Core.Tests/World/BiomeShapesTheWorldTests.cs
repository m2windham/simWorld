using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;
using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// The biome deciding what the world around it is allowed to contain: where rivers run
    /// (<see cref="BiomeDef.allowRivers"/>), where roads may be laid (<see cref="BiomeDef.allowRoads"/>) and
    /// which tiles are never chosen to settle (<see cref="BiomeDef.isExtremeBiome"/>). All three were content
    /// no line of the core read, so an ocean carried rivers, a road could be laid across sea ice and the game
    /// would happily pick an ice cap as the best site it could find.
    ///
    /// <para/>Each gate is proved twice: once as an invariant over a really generated world, and once by
    /// changing only the biome on tiles that already had the feature and re-running the same step — which is
    /// the half that cannot pass by luck of the seed.
    ///
    /// <para/>"World" is aliased to <c>global::SimWorld.World.World</c> throughout, as in
    /// <see cref="WorldGenTests"/>: this namespace's own last segment is also "World".
    /// </summary>
    public class BiomeShapesTheWorldTests : ContentTestBase
    {
        public BiomeShapesTheWorldTests(CoreContentFixture content) : base(content)
        {
        }

        private static global::SimWorld.World.World Generate(string seed, int subdivision = 4) =>
            WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision);

        private static BiomeDef Biome(string defName) => DefDatabase<BiomeDef>.GetNamed(defName);

        private static WorldGenStep Step(string defName) => DefDatabase<WorldGenStepDef>.GetNamed(defName).Worker;

        private static List<int> TilesWithRivers(WorldGrid grid) =>
            Enumerable.Range(0, grid.TilesCount).Where(i => grid.Tiles[i].Rivers.Count > 0).ToList();

        private static List<int> TilesWithRoads(WorldGrid grid) =>
            Enumerable.Range(0, grid.TilesCount).Where(i => grid.Tiles[i].Roads.Count > 0).ToList();

        // ----- allowRivers -----

        /// <summary>
        /// The invariant, over a world generated the ordinary way: the shipped content refuses rivers on the
        /// three water biomes, and now nothing writes one there. The river mouths themselves survive — the
        /// coastal land tile keeps its link out to sea, which is what the "every river reaches water" walk in
        /// <see cref="WorldGenTests"/> follows — so this is a gate on which tiles carry a river, not a rule
        /// that rivers may no longer reach the sea.
        /// </summary>
        [Fact]
        public void No_tile_whose_biome_refuses_rivers_carries_one_and_river_mouths_still_reach_the_sea()
        {
            global::SimWorld.World.World world = Generate("biome-river-invariant");
            WorldGrid grid = world.grid;

            List<int> riverTiles = TilesWithRivers(grid);
            Assert.NotEmpty(riverTiles);

            foreach (int i in riverTiles)
            {
                Tile tile = grid.Tiles[i];
                Assert.NotNull(tile.biome);
                Assert.True(tile.biome!.allowRivers, $"Tile {i} carries a river in {tile.biome.defName}, which refuses rivers.");
            }

            int mouths = riverTiles.Count(i => grid.Tiles[i].Rivers.Any(link => grid.Tiles[link.neighbor].WaterCovered));
            Assert.True(mouths > 0, "Every river still has to reach the sea; no coastal tile kept a link to open water.");
        }

        /// <summary>
        /// The half a seed cannot fake. Nothing about the terrain changes here — the same elevations, the same
        /// rainfall, therefore the same flow network — only the biome on the tiles that had rivers. Before the
        /// biome was consulted this made no difference whatsoever; now every one of those rivers is gone, and
        /// putting the original biomes back brings exactly the same rivers back.
        /// </summary>
        [Fact]
        public void Refusing_rivers_in_a_biome_removes_the_rivers_that_ran_through_it()
        {
            global::SimWorld.World.World world = Generate("biome-river-gate");
            WorldGrid grid = world.grid;
            WorldGenStep rivers = Step("Rivers");

            List<int> riverTiles = TilesWithRivers(grid);
            Assert.NotEmpty(riverTiles);
            var original = riverTiles.ToDictionary(i => i, i => grid.Tiles[i].biome!);
            int originalLinkCount = riverTiles.Sum(i => grid.Tiles[i].Rivers.Count);

            // Sea ice is the shipped biome that refuses rivers on a tile that is otherwise crossable.
            BiomeDef seaIce = Biome("SeaIce");
            Assert.False(seaIce.allowRivers);
            foreach (int i in riverTiles) grid.Tiles[i].biome = seaIce;

            ClearRivers(grid);
            rivers.GenerateFresh(world.info.seedString, world);
            Assert.Empty(TilesWithRivers(grid));

            foreach (KeyValuePair<int, BiomeDef> entry in original) grid.Tiles[entry.Key].biome = entry.Value;
            ClearRivers(grid);
            rivers.GenerateFresh(world.info.seedString, world);

            Assert.Equal(riverTiles, TilesWithRivers(grid));
            Assert.Equal(originalLinkCount, riverTiles.Sum(i => grid.Tiles[i].Rivers.Count));
        }

        // ----- allowRoads -----

        [Fact]
        public void No_tile_whose_biome_refuses_roads_carries_one()
        {
            global::SimWorld.World.World world = Generate("biome-road-invariant");
            WorldGrid grid = world.grid;

            List<int> roadTiles = TilesWithRoads(grid);
            Assert.NotEmpty(roadTiles);

            foreach (int i in roadTiles)
            {
                Tile tile = grid.Tiles[i];
                Assert.NotNull(tile.biome);
                Assert.True(tile.biome!.allowRoads, $"Tile {i} carries a road in {tile.biome.defName}, which refuses roads.");
            }
        }

        /// <summary>
        /// The same trick as the river case, and the same point: only the biome changes. A road that used to
        /// run straight across these tiles must now go round them or not exist — what it may not do is
        /// ignore the biome, which is exactly what it did before.
        /// </summary>
        [Fact]
        public void Refusing_roads_in_a_biome_keeps_every_road_off_those_tiles()
        {
            global::SimWorld.World.World world = Generate("biome-road-gate");
            WorldGrid grid = world.grid;
            WorldGenStep roads = Step("Roads");

            var settlementTiles = new HashSet<int>(world.Settlements.Select(s => s.tile));
            List<int> roadTiles = TilesWithRoads(grid).Where(i => !settlementTiles.Contains(i)).ToList();
            Assert.NotEmpty(roadTiles);

            BiomeDef seaIce = Biome("SeaIce");
            Assert.False(seaIce.allowRoads);
            foreach (int i in roadTiles) grid.Tiles[i].biome = seaIce;

            ClearRoads(grid);
            roads.GenerateFresh(world.info.seedString, world);

            foreach (int i in roadTiles)
            {
                Assert.Empty(grid.Tiles[i].Roads);
            }
        }

        // ----- isExtremeBiome -----

        /// <summary>
        /// An extreme biome is a hard "no" the way water and an unsurvivable temperature already were: no
        /// amount of fresh water, game or timber buys a site there. Asserted as the gate it is — score
        /// exactly zero, then positive again the moment the biome is an ordinary one on the same tile with
        /// the same deposits — never as a number.
        /// </summary>
        [Fact]
        public void An_extreme_biome_zeroes_a_site_score_however_good_the_tile_is()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            const int tileId = 5;
            Tile tile = grid.Tiles[tileId];
            tile.elevation = 50f;
            tile.temperature = 15f;
            tile.deposits.Add(new TileDeposit(DepositDefOf.FreshWater, 0.9f));
            tile.deposits.Add(new TileDeposit(DepositDefOf.Game, 0.9f));

            tile.biome = Biome("TemperateForest");
            float ordinary = SiteScorer.Score(grid, tileId, weights);
            Assert.True(ordinary > 0f);

            foreach (string extreme in new[] { "IceSheet", "ExtremeDesert" })
            {
                tile.biome = Biome(extreme);
                Assert.True(tile.biome!.isExtremeBiome, extreme + " is expected to be an extreme biome in content.");
                Assert.Equal(0f, SiteScorer.Score(grid, tileId, weights));
            }

            tile.biome = Biome("TemperateForest");
            Assert.Equal(ordinary, SiteScorer.Score(grid, tileId, weights));
        }

        /// <summary>
        /// And the same rule over a real world, where the extreme biomes are the polar caps: nothing that
        /// chooses a site by score — the player's opening settlement, an emerging civilization, a growing one
        /// spinning off a colony — can pick one, because every one of those tiles scores zero.
        /// </summary>
        [Fact]
        public void Every_extreme_tile_in_a_generated_world_scores_zero_as_a_site()
        {
            global::SimWorld.World.World world = Generate("biome-extreme-sites");
            WorldGrid grid = world.grid;
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");

            int extremeTiles = 0;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].biome?.isExtremeBiome != true) continue;
                extremeTiles++;
                Assert.Equal(0f, SiteScorer.Score(grid, i, weights));
            }

            Assert.True(extremeTiles > 0, "This seed was chosen for having ice caps; without any the assertion above proves nothing.");
        }

        // ----- Scribe -----

        /// <summary>
        /// Rivers and roads are not saved: <c>World.RegenerateGrid</c> re-runs their generation steps on load
        /// (only the Factions step is excluded). So the biome gates have to survive a round trip by being
        /// re-applied identically — if a loaded world re-ran the steps without them, a save/load would quietly
        /// put rivers back into the ocean.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_regenerates_the_same_biome_gated_rivers_and_roads()
        {
            global::SimWorld.World.World original = Generate("biome-gate-scribe", 3);
            string xml = Scribe.SaveToString(original, "world");

            var loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.Equal(TilesWithRivers(original.grid), TilesWithRivers(loaded.grid));
            Assert.Equal(TilesWithRoads(original.grid), TilesWithRoads(loaded.grid));

            for (int i = 0; i < loaded.grid.TilesCount; i++)
            {
                Tile tile = loaded.grid.Tiles[i];
                if (tile.biome == null) continue;
                if (!tile.biome.allowRivers) Assert.Empty(tile.Rivers);
                if (!tile.biome.allowRoads) Assert.Empty(tile.Roads);
            }
        }

        private static void ClearRivers(WorldGrid grid)
        {
            for (int i = 0; i < grid.TilesCount; i++) grid.Tiles[i].potentialRivers.Clear();
        }

        private static void ClearRoads(WorldGrid grid)
        {
            for (int i = 0; i < grid.TilesCount; i++) grid.Tiles[i].potentialRoads.Clear();
        }
    }
}
