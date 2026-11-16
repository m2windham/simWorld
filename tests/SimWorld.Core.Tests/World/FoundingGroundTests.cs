using System.Globalization;
using SimWorld.Defs;
using SimWorld.MapGen;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World.Siting;
using Xunit;

using Tile = SimWorld.World.Tile;
using BiomeDef = SimWorld.World.BiomeDef;
using Settlement = SimWorld.World.Settlement;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// The founding band is set down on ground it can farm (<see cref="FoundingGround"/>). Found on a
    /// threat-free bench (<c>docs/perf/storyteller-pressure</c>, seed 2024): once terrain fertility matched
    /// RimWorld (sand 10%), the best-scored tile was a desert whose map is all sand, nothing could be sown,
    /// and the band starved by day 30.
    /// </summary>
    public class FoundingGroundTests : ContentTestBase
    {
        public FoundingGroundTests(CoreContentFixture content) : base(content)
        {
        }

        private static Tile MakeTile(string biome, float rainfall, float swampiness = 0f) => new Tile
        {
            biome = DefDatabase<BiomeDef>.GetNamed(biome),
            rainfall = rainfall,
            swampiness = swampiness,
        };

        [Fact]
        public void An_arid_desert_tile_paints_only_sand_and_cannot_be_farmed()
        {
            Tile desert = MakeTile("Desert", rainfall: 139f);
            Assert.All(GenStep_Terrain.BaseTerrainsFor(desert), t => Assert.Same(global::SimWorld.Map.TerrainDefOf.Sand, t));
            Assert.False(FoundingGround.CanFarm(desert));
        }

        [Theory]
        [InlineData("TemperateForest", 1200f, 0f)]
        [InlineData("Desert", 600f, 0f)]
        [InlineData("TemperateForest", 1200f, 0.5f)]
        public void Soil_or_marshy_ground_can_be_farmed(string biome, float rainfall, float swampiness)
        {
            Assert.True(FoundingGround.CanFarm(MakeTile(biome, rainfall, swampiness)));
        }

        [Fact]
        public void Crops_are_sown_in_ground_and_a_berry_bush_never_is()
        {
            Assert.True(DefDatabase<ThingDef>.GetNamed("Plant_Potato").plant!.SowableInGround);
            Assert.True(DefDatabase<ThingDef>.GetNamed("Plant_Healroot").plant!.SowableInGround);
            Assert.False(DefDatabase<ThingDef>.GetNamed("Plant_Berry").plant!.SowableInGround);

            var sand = new global::SimWorld.Map.Map(20, 20, global::SimWorld.Map.TerrainDefOf.Sand);
            Assert.Null(global::SimWorld.Building.FarmingInitiative.CropFor(sand));
            var soil = new global::SimWorld.Map.Map(20, 20, global::SimWorld.Map.TerrainDefOf.Soil);
            Assert.NotNull(global::SimWorld.Building.FarmingInitiative.CropFor(soil));
        }

        [Fact]
        public void The_founding_band_of_seed_2024_is_no_longer_set_down_on_bare_sand()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "2024", subdivisionOverride: 3, soloStart: false, bandSize: 20);
            Settlement home = game.CivilizationTarget.Seat!;
            Tile tile = Find.World!.grid.Tiles[home.tile];

            Assert.True(FoundingGround.CanFarm(tile),
                "seed 2024 founded on " + tile.biome?.defName + " at " + tile.rainfall.ToString("0", CultureInfo.InvariantCulture) + "mm");
        }
    }
}
