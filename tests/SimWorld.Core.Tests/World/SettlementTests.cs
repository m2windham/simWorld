using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using SimWorld.World.Gen;
using SimWorld.World.Siting;
using Xunit;

namespace SimWorld.Tests.World
{
    /// <summary>
    /// The settlement entity and the founding flow (spec §5b.3, §5b.5, §11.3). "World" is aliased to
    /// <c>global::SimWorld.World.World</c> throughout, exactly as <c>SettlementFoundingTests</c> already does —
    /// this test namespace's own last segment is also "World", which would otherwise shadow it.
    /// </summary>
    public class SettlementTests : ContentTestBase
    {
        public SettlementTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static global::SimWorld.World.World GenerateSoloWorld(string seed, int subdivision = 4) =>
            WorldGenerator.GenerateWorld(seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, "Test", subdivision, soloStart: true);

        private static int BestScoredTile(WorldGrid grid, out float bestScore)
        {
            SiteWeightDef weights = DefDatabase<SiteWeightDef>.GetNamed("SiteWeights_SticksAndStones");
            int best = -1;
            bestScore = -1f;
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

        /// <summary>Advances every living founder's age by one demography interval, the same
        /// AgeTickMothballed shortcut <c>DemographyTests.AdvanceYear</c> uses — literally single-stepping a
        /// multi-decade run would make this far too slow, and the module's own <c>Pawn_TierTracker</c> already
        /// relies on this exact path to keep an off-tick-list pawn's age exact.</summary>
        private static void AdvanceYear(Settlement settlement, int ticks)
        {
            foreach (Pawn p in settlement.Citizens.ToList())
            {
                if (!p.Dead) p.ageTracker.AgeTickMothballed(ticks);
            }
        }

        // ----- The test that matters -----

        [Fact]
        public void Founding_on_a_scored_site_then_letting_years_pass_grows_population_multiplies_households_and_chronicles_the_founding()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-founding-matters");
            WorldGrid grid = world.grid;
            Faction faction = world.factions.First();

            int tile = BestScoredTile(grid, out float bestScore);
            Assert.True(bestScore > 0f, "Expected at least one scoreable site for this seed.");

            int householdsBefore = Find.FamilyManager.Families.Count;

            Settlement settlement = SettlementFounder.Found(world, tile, faction, 24, new RandomStream(4242), "Testhome");

            Assert.Equal("Testhome", settlement.name);
            Assert.Equal(tile, settlement.tile);
            Assert.Same(faction, settlement.faction);
            Assert.Equal(24, settlement.Citizens.Count);
            Assert.All(settlement.Citizens, p => Assert.Equal(PawnTier.Full, p.tier.Tier));
            Assert.Contains<global::SimWorld.World.WorldObject>(settlement, world.worldObjects);

            int householdsAfterFounding = Find.FamilyManager.Families.Count;
            Assert.True(householdsAfterFounding - householdsBefore >= 10, "Expected several households from a 24-person band.");

            int populationBefore = settlement.TotalPopulation;

            int tick = 0;
            for (int year = 0; year < 80; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                AdvanceYear(settlement, DemographyTuning.DemographyIntervalTicks);
                Find.TickManager.DebugSetTicksGame(tick);
                settlement.GrowthTick();
            }

            Assert.True(settlement.TotalPopulation > populationBefore,
                $"Expected population growth through demography over 80 years ({populationBefore} -> {settlement.TotalPopulation}).");
            Assert.True(Find.FamilyManager.Families.Count > householdsAfterFounding,
                "Expected households to multiply through marriages over 80 years.");

            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.Contains("Founding") && e.incidentDefName.Contains("Testhome"));
        }

        // ----- Founding validation -----

        [Theory]
        [InlineData(19)]
        [InlineData(41)]
        public void Founding_rejects_a_band_outside_the_20_to_40_range(int bandSize)
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-band-bounds");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SettlementFounder.Found(world, tile, faction, bandSize, new RandomStream(1)));
        }

        [Fact]
        public void Founding_gives_settlements_unique_names_when_none_is_supplied()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-naming");
            Faction faction = world.factions.First();
            WorldGrid grid = world.grid;
            List<int> landTiles = Enumerable.Range(0, grid.TilesCount).Where(i => !grid.Tiles[i].WaterCovered).Take(3).ToList();

            var rand = new RandomStream(77);
            var names = new List<string>();
            foreach (int tile in landTiles)
            {
                Settlement s = SettlementFounder.Found(world, tile, faction, 20, rand);
                names.Add(s.name);
            }

            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        }

        // ----- Population shape -----

        [Fact]
        public void Population_by_tier_reports_statistical_as_a_bare_count_and_full_interval_from_real_pawns()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-population-shape");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(5));

            Assert.Equal(20, settlement.PopulationOf(PawnTier.Full));
            Assert.Equal(0, settlement.PopulationOf(PawnTier.Interval));
            Assert.Equal(0, settlement.PopulationOf(PawnTier.Statistical));
            Assert.Equal(20, settlement.TotalPopulation);

            // Demote one citizen to Interval the same way a real attention change would (Notify_AttentionChanged
            // true-then-false: significant, then not), proving PopulationOf actually reads live tier state
            // rather than assuming everyone founded is forever Full.
            Pawn demoted = settlement.Citizens[0];
            demoted.tier.Notify_AttentionChanged(true);
            demoted.tier.Notify_AttentionChanged(false);
            Assert.Equal(PawnTier.Interval, demoted.tier.Tier);

            Assert.Equal(19, settlement.PopulationOf(PawnTier.Full));
            Assert.Equal(1, settlement.PopulationOf(PawnTier.Interval));
            Assert.Equal(20, settlement.TotalPopulation);
        }

        [Fact]
        public void A_large_statistical_population_answers_queries_without_instantiating_anyone()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-statistical-scale");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(9));

            settlement.AddStatisticalPeople(40000);

            // The design constraint itself: answering "how many people live here" cost one int add, not
            // 40,000 live Pawn objects — Citizens still holds only the 20 real founders.
            Assert.Equal(20, settlement.Citizens.Count);
            Assert.Equal(40000, settlement.StatisticalPopulation);
            Assert.Equal(40020, settlement.TotalPopulation);
            Assert.Equal(40000, settlement.PopulationOf(PawnTier.Statistical));
        }

        [Fact]
        public void Statistical_population_grows_through_the_closed_form_rate_when_years_pass()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-statistical-growth");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(13));
            settlement.AddStatisticalPeople(10000);

            int tick = 0;
            for (int year = 0; year < 10; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                AdvanceYear(settlement, DemographyTuning.DemographyIntervalTicks);
                Find.TickManager.DebugSetTicksGame(tick);
                settlement.GrowthTick();
            }

            // A band around compound 4.5%/year over 10 years (~55% cumulative), not a magic exact figure.
            double expected = 10000.0 * Math.Pow(1.045, 10);
            Assert.True(Math.Abs(settlement.StatisticalPopulation - expected) < expected * 0.02,
                $"Expected ~{expected:F0}, got {settlement.StatisticalPopulation}.");
            Assert.True(settlement.StatisticalPopulation > 10000);
        }

        // ----- Stores -----

        [Fact]
        public void Stores_are_a_simple_def_to_count_ledger()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-stores");
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(3));

            Assert.Equal(0, settlement.StoreCountOf(EconomyThingDefOf.Coal));

            settlement.AddStore(EconomyThingDefOf.Coal, 50);
            Assert.Equal(50, settlement.StoreCountOf(EconomyThingDefOf.Coal));

            settlement.AddStore(EconomyThingDefOf.Coal, -20);
            Assert.Equal(30, settlement.StoreCountOf(EconomyThingDefOf.Coal));

            settlement.SetStoreCount(EconomyThingDefOf.Coal, 0);
            Assert.Equal(0, settlement.StoreCountOf(EconomyThingDefOf.Coal));
            Assert.False(settlement.Stores.ContainsKey(EconomyThingDefOf.Coal), "A store reduced to zero should not linger in the ledger.");
        }

        // ----- CoalSupply reading real stores (settlement-entity module's own brief) -----

        private static WorldGrid PathableGrid(int subdivisionLevel = 2)
        {
            WorldGrid grid = WorldGrid.Generate(subdivisionLevel);
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile tile = grid.Tiles[i];
                tile.elevation = 50f;
                tile.biome = BiomeDefOf.TemperateForest;
            }
            return grid;
        }

        private static Settlement PlainSettlement(WorldGrid grid, int tile) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "S" + tile, 0);

        [Fact]
        public void CoalSupply_reads_real_stock_even_with_no_local_deposit()
        {
            WorldGrid grid = PathableGrid();
            Settlement settlement = PlainSettlement(grid, 3);
            // No Coal deposit anywhere near this tile, so the structural signal alone would say "no access".
            settlement.AddStore(EconomyThingDefOf.Coal, 5);

            CoalAccess access = CoalSupply.Evaluate(grid, settlement, new List<Settlement>());

            Assert.True(access.HasAccess);
            Assert.True(access.IsLocal);
            Assert.Equal(TradeUtility.BaseMarketValue(EconomyThingDefOf.Coal), access.UnitCost, 3);
        }

        [Fact]
        public void CoalSupply_still_grants_local_access_from_a_deposit_alone_with_empty_stores()
        {
            WorldGrid grid = WorldGrid.Generate(2);
            const int tileId = 5;
            grid.Tiles[tileId].deposits.Add(new TileDeposit(DepositDefOf.Coal, 0.5f));
            Settlement settlement = PlainSettlement(grid, tileId);

            CoalAccess access = CoalSupply.Evaluate(grid, settlement, new List<Settlement>());

            Assert.True(access.HasAccess);
            Assert.True(access.IsLocal);
        }

        [Fact]
        public void CoalSupply_has_no_access_when_neither_stores_nor_deposits_nor_candidates_have_coal()
        {
            WorldGrid grid = PathableGrid();
            Settlement home = PlainSettlement(grid, 3);
            Settlement coalless = PlainSettlement(grid, 10);

            CoalAccess access = CoalSupply.Evaluate(grid, home, new List<Settlement> { coalless });

            Assert.False(access.HasAccess);
        }

        [Fact]
        public void CoalSupply_trades_from_a_settlement_whose_stock_is_real_even_without_a_local_deposit()
        {
            WorldGrid grid = PathableGrid();
            Settlement home = PlainSettlement(grid, 0);
            Settlement supplier = PlainSettlement(grid, TileAtLeastHopsAway(grid, 0, 2));
            supplier.AddStore(EconomyThingDefOf.Coal, 10);

            CoalAccess access = CoalSupply.Evaluate(grid, home, new List<Settlement> { supplier });

            Assert.True(access.HasAccess);
            Assert.False(access.IsLocal);
            Assert.Equal(supplier.tile, access.SourceTile);
        }

        private static int TileAtLeastHopsAway(WorldGrid grid, int start, int minHops)
        {
            var distance = new Dictionary<int, int> { [start] = 0 };
            var frontier = new Queue<int>();
            frontier.Enqueue(start);
            int farthest = start;
            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                foreach (int n in grid.NeighborsOf(current))
                {
                    if (distance.ContainsKey(n) || grid.Tiles[n].WaterCovered) continue;
                    distance[n] = distance[current] + 1;
                    frontier.Enqueue(n);
                    if (distance[n] >= minHops) return n;
                    farthest = n;
                }
            }
            return farthest;
        }

        // ----- Scribe round trip -----

        [Fact]
        public void Scribe_round_trip_preserves_the_settlement_its_population_and_its_stores()
        {
            global::SimWorld.World.World world = GenerateSoloWorld("settlement-scribe", subdivision: 2);
            Faction faction = world.factions.First();
            int tile = BestScoredTile(world.grid, out _);

            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(21), "Scribehome");
            settlement.AddStatisticalPeople(123);
            settlement.AddStore(EconomyThingDefOf.Coal, 7);

            string xml = Scribe.SaveToString(world, "world");
            global::SimWorld.World.World loaded = Scribe.Load<global::SimWorld.World.World>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement? loadedSettlement = loaded.worldObjects.OfType<Settlement>().FirstOrDefault();
            Assert.NotNull(loadedSettlement);
            Assert.Equal("Scribehome", loadedSettlement!.name);
            Assert.Equal(settlement.foundingTick, loadedSettlement.foundingTick);
            Assert.Equal(settlement.tile, loadedSettlement.tile);
            Assert.Equal(20, loadedSettlement.Citizens.Count);
            Assert.Equal(123, loadedSettlement.StatisticalPopulation);
            Assert.Equal(7, loadedSettlement.StoreCountOf(EconomyThingDefOf.Coal));
        }
    }
}
