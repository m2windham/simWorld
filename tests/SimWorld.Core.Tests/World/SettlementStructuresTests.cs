using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// <see cref="Settlement.Structures"/> and <see cref="Settlement.StructureCount"/> (tracker item
    /// <c>settlements.structures</c>): a keyed def→count ledger for completed buildings, mirroring
    /// <see cref="Settlement.Stores"/>'s own API and Scribe shape exactly, and the single accessor that reads
    /// either a live <see cref="Settlement.InteriorMap"/> or the ledger — never both — so the two can never be
    /// compared against each other and therefore never disagree.
    /// </summary>
    public class SettlementStructuresTests : ContentTestBase
    {
        public SettlementStructuresTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Wall => DefDatabase<ThingDef>.GetNamed("Wall");
        private static ThingDef Bed => DefDatabase<ThingDef>.GetNamed("Bed");

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "StructTest" + tile, 0);

        private static CoreWorld GenerateSoloWorld(string seed, int subdivision = 2) =>
            WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision, soloStart: true);

        /// <summary>The same site-scoring helper <c>SettlementTests</c> uses, so founding lands somewhere a
        /// settlement could plausibly stand rather than on whatever tile a loop happens to hit first.</summary>
        private static int BestScoredTile(WorldGrid grid)
        {
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                float score = SiteScorer.Score(grid, i, weights);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Spawns <paramref name="count"/> completed <paramref name="def"/> things onto the first
        /// empty, standable cells the map offers — never onto a citizen, an existing building or unwalkable
        /// terrain, so a freshly-entered settlement's own founders never get built over.</summary>
        private static void SpawnThings(CoreMap map, ThingDef def, int count)
        {
            int placed = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (placed >= count) break;
                if (!GenGrid.Standable(c, map) || GenGrid.GetThingList(c, map).Count != 0) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
                placed++;
            }
            Assert.Equal(count, placed); // fixture sanity — the test map must have room for this many
        }

        // -------------------------------------------------------------------------------------------
        // The ledger, on its own — mirrors Stores' own API shape.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void With_no_map_StructureCount_reads_the_ledger_and_mirrors_the_stores_api_shape()
        {
            Settlement settlement = PlainSettlement();
            Assert.Equal(0, settlement.StructureCount(Wall));
            Assert.Empty(settlement.Structures);

            settlement.AddStructure(Wall, 4);
            Assert.Equal(4, settlement.StructureCount(Wall));
            Assert.Equal(4, settlement.Structures[Wall]);

            settlement.AddStructure(Wall, -4);
            Assert.Equal(0, settlement.StructureCount(Wall));
            // Clamped to a removed key, exactly as SetStoreCount does — never a stale zero entry left behind.
            Assert.DoesNotContain(Wall, settlement.Structures.Keys);

            settlement.SetStructureCount(Bed, 3);
            Assert.Equal(3, settlement.StructureCount(Bed));
            settlement.SetStructureCount(Bed, 0);
            Assert.False(settlement.Structures.ContainsKey(Bed));
        }

        // -------------------------------------------------------------------------------------------
        // The single source of truth: the load-bearing invariant.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void With_a_map_StructureCount_tracks_the_map_not_a_stale_ledger_value()
        {
            CoreWorld world = GenerateSoloWorld("struct-truth");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(11), "TruthTown");

            // A ledger value set before there is a map — and left there — so it would disagree with the map
            // the moment one exists. Huge and specific enough that nothing a freshly generated map could
            // already hold (map generation scatters a handful of ruined walls of its own — GenStep_Ruins —
            // so "the map has zero to start with" is not an assumption this test can make) would ever match it
            // by accident. This is the disagreement StructureCount must never surface.
            settlement.SetStructureCount(Wall, 12_345);
            Assert.Equal(12_345, settlement.StructureCount(Wall)); // still the ledger: no map yet

            CoreMap map = settlement.EnterMap(world);
            int fromMapGeneration = settlement.StructureCount(Wall); // whatever ruins the generator itself scattered
            Assert.NotEqual(12_345, fromMapGeneration);

            SpawnThings(map, Wall, 3);

            Assert.Equal(fromMapGeneration + 3, settlement.StructureCount(Wall));
            Assert.NotEqual(12_345, settlement.StructureCount(Wall));

            // The stale entry is still sitting in the raw ledger, unread — proof this is "the map is asked
            // instead", not "the ledger happened to already agree".
            Assert.Equal(12_345, settlement.Structures[Wall]);
        }

        [Fact]
        public void With_a_map_a_def_with_nothing_built_reads_zero_even_if_the_ledger_never_learned_that()
        {
            CoreWorld world = GenerateSoloWorld("struct-truth-zero");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(14), "ZeroTown");
            settlement.EnterMap(world);

            Assert.Equal(0, settlement.StructureCount(Bed));
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Scribe_round_trip_preserves_the_structures_ledger()
        {
            CoreWorld world = GenerateSoloWorld("struct-scribe");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(12), "Scribestruct");
            settlement.AddStructure(Wall, 6);
            settlement.AddStructure(Bed, 2);

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement? reloaded = loaded.worldObjects.OfType<Settlement>().FirstOrDefault(s => s.name == "Scribestruct");
            Assert.NotNull(reloaded);
            Assert.Equal(6, reloaded!.StructureCount(Wall));
            Assert.Equal(2, reloaded.StructureCount(Bed));
        }

        [Fact]
        public void Scribe_round_trip_of_a_settlement_with_a_live_map_preserves_what_StructureCount_answers()
        {
            CoreWorld world = GenerateSoloWorld("struct-scribe-map");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(15), "Scribemap");
            CoreMap map = settlement.EnterMap(world);
            int fromMapGeneration = settlement.StructureCount(Wall);
            SpawnThings(map, Wall, 4);

            int before = settlement.StructureCount(Wall);
            Assert.Equal(fromMapGeneration + 4, before);

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Scribemap");
            Assert.NotNull(reloaded.InteriorMap);
            Assert.Equal(before, reloaded.StructureCount(Wall));
        }

        // -------------------------------------------------------------------------------------------
        // The ledger stays honest against a live map, on the settlement's own gated sync.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void The_ledger_is_kept_current_from_the_map_on_the_settlements_own_gated_sync()
        {
            CoreWorld world = GenerateSoloWorld("struct-sync");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(13), "Syncstruct");

            CoreMap map = settlement.EnterMap(world);
            int fromMapGeneration = settlement.StructureCount(Wall);
            SpawnThings(map, Wall, 5);
            int total = fromMapGeneration + 5;

            // Nothing has run the gated sync yet — the raw ledger has not seen the map's walls, even though
            // StructureCount already reports them correctly by reading the map directly.
            Assert.Equal(total, settlement.StructureCount(Wall));
            Assert.False(settlement.Structures.ContainsKey(Wall), "the ledger saw the map's walls before its own gated sync ever ran");

            Find.TickManager.DebugSetTicksGame(SettlementTuning.CitizenMapSyncIntervalTicks);
            settlement.Tick(world);

            Assert.Equal(total, settlement.Structures[Wall]);
        }
    }
}
