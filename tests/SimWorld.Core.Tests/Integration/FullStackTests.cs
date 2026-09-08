using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// Every other test in this suite exercises one module. These walk the seams between them, because the
    /// seams are where this project has actually found its bugs: two rounds of ThingDefs that could not be
    /// spawned because nothing had ever tried, a stat defined twice in two files with load order silently
    /// picking a winner, an interface with no implementors that read as a working feature. A module can be
    /// perfectly tested and still not connect to the one beside it.
    ///
    /// The walk is the real pipeline, in order: generate a world, read its regions and deposits, score a site
    /// the way a founding would, generate that settlement's interior from its own world tile, put people on
    /// it, and let them live.
    /// </summary>
    public class FullStackTests : ContentTestBase
    {
        public FullStackTests(CoreContentFixture content) : base(content)
        {
        }

        private const string Seed = "integration-seed";
        private const int PotatoesPlaced = 6;
        private const float StartingHunger = 0.1f;

        /// <summary>Subdivision 3 (642 tiles) rather than a full planet: the seams are the same, the run is seconds.</summary>
        private static CoreWorld NewWorld(string seed = Seed) => WorldGenerator.GenerateWorld(
            seed, planetCoverage: 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
            OverallPopulation.Normal, name: "Integration", subdivisionOverride: 3);

        [Fact]
        public void A_generated_world_carries_regions_deposits_and_settlements_together()
        {
            CoreWorld world = NewWorld();

            Assert.NotEmpty(world.regions);
            Assert.NotEmpty(world.Settlements);

            // Every land tile belongs to exactly one region, and no water tile belongs to any. This is the
            // invariant the region partition promises; it is worth asserting from outside that module.
            var assigned = new HashSet<int>();
            foreach (WorldRegion region in world.regions)
            {
                foreach (int tileId in region.tiles)
                {
                    Assert.False(world.grid.Tiles[tileId].WaterCovered, "a water tile was put in a region");
                    Assert.True(assigned.Add(tileId), "tile " + tileId + " is in two regions");
                }
            }

            int landTiles = 0;
            for (int i = 0; i < world.grid.TilesCount; i++)
            {
                if (!world.grid.Tiles[i].WaterCovered) landTiles++;
            }
            Assert.Equal(landTiles, assigned.Count);

            // Deposits are on the tiles, not merely defined.
            bool anyDeposit = false;
            for (int i = 0; i < world.grid.TilesCount && !anyDeposit; i++)
            {
                anyDeposit = world.grid.Tiles[i].deposits is { Count: > 0 };
            }
            Assert.True(anyDeposit, "world generation produced no deposits at all");
        }

        [Fact]
        public void Site_scoring_reads_the_world_that_generation_actually_produced()
        {
            CoreWorld world = NewWorld();
            SiteWeightDef neolithic = DefDatabase<SiteWeightDef>.AllDefsListForReading
                .OrderBy(w => w.era?.order ?? int.MaxValue).First();

            var scored = new List<(int tile, float score)>();
            for (int i = 0; i < world.grid.TilesCount; i++)
            {
                if (world.grid.Tiles[i].WaterCovered) continue;
                scored.Add((i, SiteScorer.Score(world.grid, i, neolithic)));
            }

            Assert.NotEmpty(scored);

            // The scorer must discriminate. If every site on a whole generated world scores the same, the
            // necessities-times-advantages model is not reading the terrain and the founding choice is a coin
            // toss dressed up as a decision.
            Assert.True(scored.Select(s => s.score).Distinct().Count() > 1,
                "site scoring produced one value across an entire world");
            Assert.True(scored.Any(s => s.score > 0f), "no site on the world was habitable at all");
        }

        [Fact]
        public void A_settlements_interior_is_generated_from_its_own_world_tile()
        {
            CoreWorld world = NewWorld();
            WorldObject settlement = world.Settlements.First();

            CoreMap map = MapGenerator.GenerateMapFor(settlement, world, new IntVec2(64, 64));

            Assert.Equal(64, map.Size.x);
            Assert.Equal(64, map.Size.z);

            // Every cell got real terrain — the seam is only closed if the map is actually populated.
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                Assert.NotNull(map.terrainGrid.TerrainAt(map.cellIndices.IndexToCell(i)));
            }
        }

        [Fact]
        public void A_founding_band_lives_on_the_map_that_was_generated_for_it()
        {
            CoreWorld world = NewWorld();
            WorldObject settlement = world.Settlements.First();
            CoreMap map = MapGenerator.GenerateMapFor(settlement, world, new IntVec2(64, 64));

            // Put a small band on open ground, with food within reach.
            var band = new List<Pawn>();
            IntVec3 origin = FirstWalkable(map);
            for (int i = 0; i < 6; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist));
                GenSpawn.Spawn(pawn, OffsetWalkable(map, origin, i + 1), map);
                band.Add(pawn);
            }
            for (int i = 0; i < PotatoesPlaced; i++)
            {
                Thing food = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawPotatoes"));
                GenSpawn.Spawn(food, OffsetWalkable(map, origin, i + 20), map);
            }

            foreach (Pawn pawn in band)
            {
                Need_Food? hunger = pawn.needs.food;
                if (hunger != null) hunger.CurLevel = StartingHunger;
            }

            RunTicks(2000, band.ToArray());

            // Nobody died of being simulated, and the think tree gave everyone something to do. This is the
            // whole point of the walk: worldgen, mapgen, pawn generation, needs and the AI layer have to line
            // up for any of it to happen at all.
            Assert.All(band, p => Assert.False(p.Dead, "a pawn died during an ordinary 2,000-tick day"));
            Assert.Contains(band, p => p.jobs?.curJob != null);

            // The strong claim: someone actually got fed. A starving pawn on a generated map with food on it
            // has to notice, path to it and eat — worldgen, mapgen, needs, the think tree, reservations and
            // pathing all cooperating. Asserting only "has a job" would pass even if nobody ever arrived.
            // The strong claim: someone actually got fed. A starving pawn on a generated map with food on it
            // has to notice it, path to it and eat — worldgen, mapgen, needs, the think tree, reservations and
            // pathing all cooperating. Asserting only "has a job" would pass even if nobody ever arrived, and
            // when this test was first written that is exactly what it did.
            //
            // The claim is consumption rather than a full belly, deliberately. A raw potato is 0.05 nutrition
            // (Items_Food.xml), so six pawns eating one each move the needle from 0.10 to roughly 0.135; a
            // threshold set much above that fails for a reason that has nothing to do with the seam working.
            int potatoesLeft = map.listerThings.AllThings.Count(t => t.def.defName == "RawPotatoes");
            Assert.True(potatoesLeft < PotatoesPlaced,
                $"nobody ate: {potatoesLeft} of {PotatoesPlaced} potatoes are still lying on the map");
            Assert.Contains(band, p => (p.needs.food?.CurLevel ?? 0f) > StartingHunger);
        }

        [Fact]
        public void The_same_seed_produces_the_same_world_and_the_same_interior()
        {
            CoreWorld a = NewWorld();
            CoreWorld b = NewWorld();

            Assert.Equal(a.regions.Count, b.regions.Count);
            Assert.Equal(a.Settlements.Count(), b.Settlements.Count());
            for (int i = 0; i < a.grid.TilesCount; i++)
            {
                Assert.Equal(a.grid.Tiles[i].biome, b.grid.Tiles[i].biome);
                Assert.Equal(a.grid.Tiles[i].elevation, b.grid.Tiles[i].elevation, 4);
            }

            CoreMap mapA = MapGenerator.GenerateMapFor(a.Settlements.First(), a, new IntVec2(48, 48));
            CoreMap mapB = MapGenerator.GenerateMapFor(b.Settlements.First(), b, new IntVec2(48, 48));
            for (int i = 0; i < mapA.cellIndices.NumGridCells; i++)
            {
                IntVec3 cell = mapA.cellIndices.IndexToCell(i);
                Assert.Equal(mapA.terrainGrid.TerrainAt(cell), mapB.terrainGrid.TerrainAt(cell));
            }
        }

        [Fact]
        public void Generations_pass_and_the_chronicle_remembers_them()
        {
            // Demography and the chronicle are the civilization half of the game; this asserts they engage
            // with real generated pawns rather than only with hand-built test fixtures.
            var population = new List<Pawn>();
            for (int i = 0; i < 12; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist));
                pawn.ageTracker.DebugSetAge(22f + i % 5);
                population.Add(pawn);
            }

            int startingHouseholds = Find.FamilyManager.Families.Count;
            for (int year = 0; year < 40; year++)
            {
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerYear);
                Find.FamilyManager.DemographyTick(population);
            }

            Assert.True(Find.FamilyManager.Families.Count > startingHouseholds,
                "forty years passed and not one household was founded");
            Assert.NotEmpty(Find.Storyteller.Chronicle);
        }

        private static IntVec3 FirstWalkable(CoreMap map)
        {
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                if (map.pathGrid.Walkable(cell)) return cell;
            }
            return IntVec3.Zero;
        }

        private static IntVec3 OffsetWalkable(CoreMap map, IntVec3 origin, int step)
        {
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell((map.cellIndices.CellToIndex(origin) + step * 7 + i)
                    % map.cellIndices.NumGridCells);
                if (map.pathGrid.Walkable(cell)) return cell;
            }
            return origin;
        }
    }
}
