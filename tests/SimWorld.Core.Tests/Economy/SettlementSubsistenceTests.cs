using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using SettlementLarder = global::SimWorld.Economy.SettlementLarder;
using SettlementSubsistence = global::SimWorld.Economy.SettlementSubsistence;
using SettlementSubsistenceTuning = global::SimWorld.Economy.SettlementSubsistenceTuning;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>The production half of the civilization-scale ledger</b> (spec §11.2) — a settlement with no map
    /// grows food into <c>Settlement.Stores</c>, which is the only larder its citizens can reach.
    ///
    /// <para/><b>The defect these pin.</b> <c>Economy.SettlementLarder</c> closed consumption and named what
    /// it left open in as many words: nothing at civilization scale produced into any ledger. The only writer
    /// that ever added food was the founding band's ration crate, and <c>Economy.SettlementStockInitiative</c>
    /// banks from a live map, so it only ever fires for the one settlement somebody has opened. Measured over
    /// twenty in-game days on a settlement nobody opened — <c>Game.NewGame</c>, tribal start, twenty-five
    /// founders, nothing called by hand:
    ///
    /// <para/><code>
    /// day | alive | mean food | ledger nutrition | worst Malnutrition
    ///   0 |    25 |      0.80 |            440.0 | 0.00
    ///   5 |    24 |      0.31 |            242.4 | 0.00
    ///  11 |    22 |      0.33 |              0.0 | 0.00
    ///  15 |    22 |      0.00 |              0.0 | 0.43
    ///  20 |    18 |      0.00 |              0.0 | 1.00   (lethal at 1.00)
    /// </code>
    ///
    /// <para/>A crate draining at exactly the rate twenty-five people eat, and then a town starving to death.
    ///
    /// <para/><b>What these assert.</b> That a settlement's ledger fills from its own hands and its own land,
    /// that it stops at the larder the food economy already asks for rather than growing without bound, that
    /// the citizens who are paid for here are exactly the citizens the map path cannot pay for, that the
    /// Statistical cohort is on neither side of the book, and that not one of it costs a random draw.
    /// Everything tuned is pinned as an ordering, a band or a trend — never as a literal (CLAUDE.md).
    /// </summary>
    public class SettlementSubsistenceTests : ContentTestBase
    {
        public SettlementSubsistenceTests(CoreContentFixture content) : base(content)
        {
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static BiomeDef Biome(string defName) => DefDatabase<BiomeDef>.GetNamed(defName);

        /// <summary>A world this thread is running, small enough to generate inside a test. The tile a
        /// settlement sits on is what carries its biome, so a settlement with no world under it has no land
        /// and — correctly — grows nothing; every test here needs a real one.</summary>
        private static CoreWorld NewWorld(string seed)
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                seed, 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Subsistence", 2, soloStart: true);
            Find.World = world;
            return world;
        }

        private static int LandTile(CoreWorld world) =>
            Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);

        /// <summary>A settlement on a tile whose biome this test states outright, so the land under it is a
        /// fixture rather than whatever world generation happened to paint there.</summary>
        private static Settlement SettlementOn(CoreWorld world, string biomeName, string name = "Subsisthome", int? tileOverride = null)
        {
            int tile = tileOverride ?? LandTile(world);
            world.grid.Tiles[tile].biome = Biome(biomeName);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            world.worldObjects.Add(settlement);
            return settlement;
        }

        /// <summary>A citizen of <paramref name="settlement"/> standing on no map — the state this whole class
        /// is about, on both sides of the book: no map to grow food on, no map to find it on.</summary>
        private static Pawn OffMapCitizen(Settlement settlement, string name = "Hand")
        {
            Pawn pawn = NewHuman(name);
            settlement.AddCitizen(pawn);
            return pawn;
        }

        private static float Stored(Settlement settlement) => SettlementLarder.StoredNutrition(settlement);

        /// <summary>Drives <paramref name="passes"/> gated passes by hand, each at its own tick position, so
        /// the pass-index arithmetic in <c>Run</c> sees what the tick loop would give it.</summary>
        private static int RunPasses(Settlement settlement, int passes, int firstPass = 1)
        {
            int produced = 0;
            for (int i = 0; i < passes; i++)
            {
                Find.TickManager.DebugSetTicksGame((firstPass + i) * SettlementSubsistenceTuning.IntervalTicks);
                produced += SettlementSubsistence.Run(settlement);
            }
            return produced;
        }

        // -------------------------------------------------------------------------------------------
        // It produces at all — the column that used to read 0.0 for ever.
        // -------------------------------------------------------------------------------------------

        /// <summary>The whole point, in one assertion: a settlement whose citizens have no map grows food into
        /// the only larder those citizens can reach.</summary>
        [Fact]
        public void A_settlement_with_no_map_grows_food_into_its_own_ledger()
        {
            CoreWorld world = NewWorld("subsist-basic");
            Settlement settlement = SettlementOn(world, "TemperateForest");
            for (int i = 0; i < 10; i++) OffMapCitizen(settlement, "Hand" + i);

            Assert.Empty(settlement.Stores);
            Assert.True(RunPasses(settlement, 1) > 0, "a settlement of ten off-map citizens grew nothing at all");
            Assert.True(Stored(settlement) > 0f);

            // And what it grew is food the ledger's own consumer will actually serve — the same predicate
            // SettlementLarder.BestStoredFood eats by, so a def is food here exactly when it is food there.
            Assert.NotNull(SettlementLarder.BestStoredFood(settlement));
        }

        /// <summary>Read off content by rule rather than named: the harvested product of the best crop the
        /// content ships, by the same nutrition-per-cell-per-day measure <c>Building.FarmingInitiative</c>
        /// ranks a map's crops with. A new crop ships into this with no code change.</summary>
        [Fact]
        public void What_it_grows_is_read_off_the_content_that_already_says_so()
        {
            ThingDef? grown = SettlementSubsistence.SubsistenceFoodDef();
            Assert.NotNull(grown);
            Assert.True(grown!.IsNutritionGivingIngestible);

            ThingDef best = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Plant && d.plant?.harvestedThingDef != null)
                .OrderByDescending(global::SimWorld.Building.FarmingInitiative.NutritionPerCellPerDay)
                .ThenBy(d => d.plant!.harvestedThingDef!.defName, System.StringComparer.Ordinal)
                .First();
            Assert.Equal(best.plant!.harvestedThingDef!.defName, grown.defName);
        }

        // -------------------------------------------------------------------------------------------
        // The ceiling — this is a larder, not a fountain.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The load-bearing half of the model. A settlement stops producing at the larder the food economy
        /// already asks for (<c>AI.HuntingTuning.DaysOfFoodWanted</c>, the same figure
        /// <c>HuntingInitiative.WantsMeat</c> closes the hunting gate at), so a civilization's ledger is a
        /// working larder rather than an ever-growing pile and a bad year still empties it.
        /// </summary>
        [Fact]
        public void A_settlement_stops_growing_once_its_larder_is_the_size_the_food_economy_asks_for()
        {
            CoreWorld world = NewWorld("subsist-ceiling");
            Settlement settlement = SettlementOn(world, "TropicalRainforest");
            for (int i = 0; i < 8; i++) OffMapCitizen(settlement, "Hand" + i);

            float wanted = SettlementSubsistence.NutritionWantedInStores(settlement);
            Assert.True(wanted > 0f);

            // Run long enough that an uncapped settlement would be drowning in food: the rate is a multiple
            // of what these eight burn, and this is eighty passes of it.
            RunPasses(settlement, 80);

            Assert.True(Stored(settlement) > 0f, "it produced nothing");
            Assert.True(Stored(settlement) <= wanted + 1f,
                "the ledger reached " + Stored(settlement).ToString("F1") + " against a target of "
                + wanted.ToString("F1") + " — abstract production is a fountain, not a larder");

            // And at the ceiling it is genuinely idle: another pass adds nothing.
            Assert.Equal(0, RunPasses(settlement, 1, firstPass: 200));
        }

        /// <summary>The ceiling is a target, not a floor: a settlement eaten down below it starts producing
        /// again, which is what makes the ledger recover instead of settling at zero.</summary>
        [Fact]
        public void A_settlement_eaten_back_down_starts_growing_again()
        {
            CoreWorld world = NewWorld("subsist-refill");
            Settlement settlement = SettlementOn(world, "TemperateForest");
            for (int i = 0; i < 8; i++) OffMapCitizen(settlement, "Hand" + i);

            RunPasses(settlement, 80);
            float full = Stored(settlement);
            Assert.Equal(0, RunPasses(settlement, 1, firstPass: 200));

            // Spend most of it the way the larder would, and the fields answer.
            ThingDef grown = SettlementSubsistence.SubsistenceFoodDef()!;
            settlement.SetStoreCount(grown, settlement.StoreCountOf(grown) / 5);
            Assert.True(RunPasses(settlement, 1, firstPass: 201) > 0, "an emptied ledger was not refilled");
            Assert.True(Stored(settlement) < full + 1f);
        }

        // -------------------------------------------------------------------------------------------
        // The predicate: whose labour this is, and the invariant it protects.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The guard that keeps this from minting food on top of a working map economy. A citizen standing on
        /// a live map already forages, farms and cooks through the ported map path, and what rests in the
        /// granary is carried across by <c>Economy.SettlementStockInitiative</c>; counting them here as well
        /// would pay one person's labour twice and break that class's invariant — <i>a unit of goods is either
        /// a Thing on a map or a count in Stores, never both</i> — from the side that creates units.
        /// </summary>
        [Fact]
        public void A_settlement_whose_citizens_are_all_on_a_map_grows_nothing_abstractly()
        {
            CoreWorld world = NewWorld("subsist-watched");
            Settlement settlement = SettlementOn(world, "TropicalRainforest");
            var map = new CoreMap(12, 12, TerrainDefOf.Soil);

            for (int i = 0; i < 6; i++)
            {
                Pawn citizen = OffMapCitizen(settlement, "Worker" + i);
                GenSpawn.Spawn(citizen, new IntVec3(2 + i, 0, 2), map);
                Assert.True(citizen.Spawned);
                Assert.False(SettlementSubsistence.WouldWorkForStores(citizen));
            }

            Assert.Equal(0f, SettlementSubsistence.BurnPerDay(settlement));
            Assert.Equal(0, RunPasses(settlement, 10));
            Assert.Empty(settlement.Stores);
        }

        /// <summary>
        /// The mixed case, which is the normal one once <c>God.AttentionBudget</c> has demoted everybody
        /// beyond the Full-tier budget: each citizen is paid through exactly one path, so a half-watched
        /// settlement produces exactly its half abstractly and no more.
        /// </summary>
        [Fact]
        public void A_half_watched_settlement_is_paid_for_exactly_once_per_citizen()
        {
            CoreWorld world = NewWorld("subsist-mixed");
            var map = new CoreMap(12, 12, TerrainDefOf.Soil);

            Settlement mixed = SettlementOn(world, "TemperateForest", "Mixed");
            for (int i = 0; i < 6; i++) OffMapCitizen(mixed, "Mixed" + i);
            for (int i = 0; i < 3; i++) GenSpawn.Spawn(mixed.Citizens[i], new IntVec3(2 + i, 0, 2), map);

            Settlement offMapOnly = SettlementOn(world, "TemperateForest", "OffMap", tileOverride: OtherLandTile(world, mixed.tile));
            for (int i = 0; i < 3; i++) OffMapCitizen(offMapOnly, "Off" + i);

            // Three pairs of hands either way, so the two settlements burn — and therefore grow — the same.
            Assert.Equal(SettlementSubsistence.BurnPerDay(offMapOnly), SettlementSubsistence.BurnPerDay(mixed), 3);
            Assert.Equal(RunPasses(offMapOnly, 3), RunPasses(mixed, 3));
        }

        /// <summary>
        /// The two sides of one book agree about who is in it: the set this class pays for is exactly the set
        /// <c>SettlementLarder</c> feeds. Stated as the predicate identity rather than as two behaviours, so a
        /// later change to either side that broke the pairing would fail here rather than silently starve or
        /// silently flood a settlement.
        /// </summary>
        [Fact]
        public void The_hands_and_the_mouths_are_the_same_people()
        {
            CoreWorld world = NewWorld("subsist-pairing");
            Settlement settlement = SettlementOn(world, "TemperateForest");
            var map = new CoreMap(12, 12, TerrainDefOf.Soil);

            Pawn offMap = OffMapCitizen(settlement, "OffMap");
            Pawn onMap = OffMapCitizen(settlement, "OnMap");
            Pawn dead = OffMapCitizen(settlement, "Dead");
            Pawn suspended = OffMapCitizen(settlement, "Suspended");

            GenSpawn.Spawn(onMap, new IntVec3(3, 0, 3), map);
            dead.health.Kill(null, null);
            suspended.Suspended = true;

            foreach (Pawn citizen in new[] { offMap, onMap, dead, suspended })
            {
                // Hungry enough that the larder's own hunger test cannot be what separates them.
                if (citizen.needs?.food != null) citizen.needs.food.CurLevelPercentage = 0.05f;
                Assert.Equal(
                    SettlementLarder.WouldEatFromStores(citizen),
                    SettlementSubsistence.WouldWorkForStores(citizen));
            }
        }

        // -------------------------------------------------------------------------------------------
        // Scale: the cohort is on neither side of the book.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The defined, cheap answer for a settlement of thousands (spec §11.3), and the asymmetry
        /// <c>SettlementLarder</c> named: <i>the cohort does not eat</i>. If production scaled with total
        /// population while consumption scales with the live roster, a settlement of ten thousand abstract
        /// people would flood its ledger feeding one. So the cohort is on neither side — never iterated, never
        /// fed, never counted as hands — which is the only pairing that balances.
        /// </summary>
        [Fact]
        public void A_statistical_cohort_of_thousands_grows_exactly_as_much_as_nobody()
        {
            CoreWorld world = NewWorld("subsist-cohort");

            Settlement crowded = SettlementOn(world, "TemperateForest", "Crowded");
            OffMapCitizen(crowded, "Hand");
            crowded.AddStatisticalPeople(10_000);

            Settlement empty = SettlementOn(world, "TemperateForest", "Sparse", tileOverride: OtherLandTile(world, crowded.tile));
            OffMapCitizen(empty, "Hand");

            Assert.Equal(10_001, crowded.TotalPopulation);
            Assert.Equal(SettlementSubsistence.BurnPerDay(empty), SettlementSubsistence.BurnPerDay(crowded), 4);
            Assert.Equal(SettlementSubsistence.NutritionWantedInStores(empty), SettlementSubsistence.NutritionWantedInStores(crowded), 4);
            Assert.Equal(RunPasses(empty, 20), RunPasses(crowded, 20));
        }

        // -------------------------------------------------------------------------------------------
        // The land.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The biome decides how much of its intent a settlement's ground returns, read off
        /// <c>BiomeDef.forageability</c> — "how much food a forager can find here", that field's own words —
        /// the way <c>Building.WildFoodTuning</c> already reads it. Pinned as the ordering the content states,
        /// never as a rate.
        /// </summary>
        [Fact]
        public void Better_land_grows_more_and_dead_land_grows_nothing()
        {
            CoreWorld world = NewWorld("subsist-land");
            var tiles = Enumerable.Range(0, world.grid.TilesCount).Where(i => !world.grid.Tiles[i].WaterCovered).Take(4).ToList();

            float Yield(string biome, int tile)
            {
                Settlement settlement = SettlementOn(world, biome, biome + "home", tileOverride: tile);
                for (int i = 0; i < 5; i++) OffMapCitizen(settlement, biome + i);
                return SettlementSubsistence.NutritionPerDay(settlement);
            }

            float rainforest = Yield("TropicalRainforest", tiles[0]);
            float forest = Yield("TemperateForest", tiles[1]);
            float desert = Yield("Desert", tiles[2]);
            float ice = Yield("IceSheet", tiles[3]);

            Assert.True(rainforest > forest, "a rainforest must out-grow a temperate forest");
            Assert.True(forest > desert, "a temperate forest must out-grow a desert");
            Assert.Equal(0f, ice); // forageability 0: nothing edible grows here, so nothing does
        }

        /// <summary>
        /// The best land content ships realises the settlement's whole intent — the multiple of its own burn
        /// that <c>Building.FarmingInitiative</c> already sizes a field at — so the model is anchored on a
        /// figure this codebase committed to rather than on a fresh constant. Everything thinner is a fraction
        /// of it, and thin enough land produces less than its own mouths burn, which is the model saying
        /// something true about that ground.
        /// </summary>
        [Fact]
        public void The_best_land_in_content_returns_the_settlements_whole_intent()
        {
            CoreWorld world = NewWorld("subsist-anchor");
            BiomeDef best = DefDatabase<BiomeDef>.AllDefsListForReading.OrderByDescending(b => b.forageability).First();
            Settlement settlement = SettlementOn(world, best.defName);
            for (int i = 0; i < 5; i++) OffMapCitizen(settlement, "Hand" + i);

            float burn = SettlementSubsistence.BurnPerDay(settlement);
            Assert.Equal(burn * SettlementSubsistenceTuning.YieldRatio, SettlementSubsistence.NutritionPerDay(settlement), 3);
            Assert.True(SettlementSubsistence.NutritionPerDay(settlement) > burn,
                "the best land in the world cannot even feed the people standing on it");

            Settlement thin = SettlementOn(world, "ExtremeDesert", "Thin", tileOverride: OtherLandTile(world, settlement.tile));
            for (int i = 0; i < 5; i++) OffMapCitizen(thin, "Thin" + i);
            Assert.True(SettlementSubsistence.NutritionPerDay(thin) < SettlementSubsistence.BurnPerDay(thin),
                "an extreme desert is supposed to be land a town cannot live off unaided");
        }

        // -------------------------------------------------------------------------------------------
        // Determinism.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// This runs inside the tick loop, so one draw here would shift every subsequent roll in the game by
        /// the accident of when a settlement got hungry — the same statement <c>Quests.QuestRewardSink</c>,
        /// <c>Economy.SettlementStockInitiative</c> and <c>Economy.SettlementLarder</c> all make about
        /// themselves. What is grown and how much are both decided, never drawn.
        /// </summary>
        [Fact]
        public void Growing_a_settlements_food_takes_no_roll_at_all()
        {
            CoreWorld world = NewWorld("subsist-rand");
            Settlement settlement = SettlementOn(world, "TemperateForest");
            for (int i = 0; i < 5; i++) OffMapCitizen(settlement, "Hand" + i);

            uint before = Rand.Current.Iterations;
            Assert.True(RunPasses(settlement, 5) > 0);
            SettlementSubsistence.ProvisionIfNewlyFounded(settlement);
            Assert.Equal(before, Rand.Current.Iterations);
        }

        /// <summary>
        /// The fraction of a unit a pass earns is carried, not rounded away. A settlement of one on thin land
        /// earns a fraction of a single unit per pass; truncating each pass independently would round that to
        /// nothing for ever and starve exactly the settlements least able to survive it. No state and no draw
        /// — the carry lives in the pass-index arithmetic.
        /// </summary>
        [Fact]
        public void A_fraction_of_a_unit_per_pass_is_carried_rather_than_rounded_away()
        {
            CoreWorld world = NewWorld("subsist-dither");
            Settlement settlement = SettlementOn(world, "ExtremeDesert");
            OffMapCitizen(settlement, "Lonely");

            ThingDef grown = SettlementSubsistence.SubsistenceFoodDef()!;
            float perPass = SettlementSubsistence.NutritionPerDay(settlement)
                * SettlementSubsistenceTuning.IntervalTicks / GenDate.TicksPerDay;
            Assert.True(perPass < grown.ingestible!.nutrition,
                "this test needs a settlement earning less than one whole unit a pass");

            Assert.True(RunPasses(settlement, 200) > 0,
                "two hundred passes of a fraction of a unit each produced nothing at all");
        }

        // -------------------------------------------------------------------------------------------
        // The founding rations, for every settlement rather than the player's.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// <b>The second defect this lane closes.</b> The founding provision was called from
        /// <c>Sim.Game.NewGame</c>, which founds exactly one settlement, so every rival civilization
        /// <c>World.EmergenceManager</c> raises during play walked in with an empty ledger. It cannot be
        /// called from <c>World.SettlementFounder.Found</c> — that would make <c>World/</c> reference
        /// <c>Economy/</c>, against spec §2 — so the owner is here, on the pass that falls inside a
        /// settlement's founding window.
        /// </summary>
        [Fact]
        public void A_settlement_founded_during_play_walks_in_carrying_food()
        {
            CoreWorld world = NewWorld("subsist-founding");
            Faction faction = world.factions.First();
            Find.TickManager.DebugSetTicksGame(37_000); // founded mid-game, not at the start

            int tile = LandTile(world);
            world.grid.Tiles[tile].biome = Biome("TemperateForest");
            Settlement rival = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(4242), "Rivalhome");
            Assert.Equal(0f, Stored(rival));

            // The pass that falls inside the founding window pays the rations...
            Find.TickManager.DebugSetTicksGame(38_000);
            Assert.True(SettlementSubsistence.ProvisionIfNewlyFounded(rival) > 0,
                "a band founded during play walked in with nothing to eat");
            float rations = Stored(rival);
            Assert.True(rations > 0f);

            // ...and no later pass ever pays them again. Exactly once, with no marker and nothing to Scribe.
            for (int pass = 20; pass < 30; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * SettlementSubsistenceTuning.IntervalTicks);
                Assert.Equal(0, SettlementSubsistence.ProvisionIfNewlyFounded(rival));
            }
            Assert.Equal(rations, Stored(rival), 3);
        }

        /// <summary>Exactly one gated pass ever pays a settlement's rations, whatever tick it was founded on
        /// — the property the "no marker, no state" provisioning rests on, driven across every offset within
        /// an interval rather than only the one the other tests happen to use. Founded on a tick a pass falls
        /// on, one tick after one, halfway between two, one tick before the next: one crate, always.</summary>
        [Fact]
        public void Exactly_one_gated_pass_ever_pays_a_settlements_rations()
        {
            CoreWorld world = NewWorld("subsist-window");
            int interval = SettlementSubsistenceTuning.IntervalTicks;

            foreach (int offset in new[] { 0, 1, 7, interval / 2, interval - 1 })
            {
                Settlement settlement = SettlementOn(world, "TemperateForest", "Window" + offset,
                    tileOverride: LandTile(world));
                for (int i = 0; i < 4; i++) OffMapCitizen(settlement, "Window" + offset + "Hand" + i);
                settlement.foundingTick = 4 * interval + offset;

                int crates = 0;
                for (int pass = 1; pass <= 12; pass++)
                {
                    Find.TickManager.DebugSetTicksGame(pass * interval);
                    if (SettlementSubsistence.ProvisionIfNewlyFounded(settlement) > 0) crates++;
                }
                Assert.Equal(1, crates);
            }
        }

        /// <summary>A settlement of pure Statistical cohort — <c>SettlementFounder.FoundColony</c>'s shape —
        /// is provisioned with nothing and grows nothing, because it has no live roster on either side of the
        /// book. Named as a decision so it is not mistaken for an oversight; see the class doc.</summary>
        [Fact]
        public void A_settlement_of_pure_cohort_is_on_neither_side_of_the_book()
        {
            CoreWorld world = NewWorld("subsist-colony");
            Settlement colony = SettlementOn(world, "TropicalRainforest", "Cohorthome");
            colony.AddStatisticalPeople(500);
            colony.foundingTick = Find.TickManager.TicksGame;

            Find.TickManager.DebugSetTicksGame(SettlementSubsistenceTuning.IntervalTicks);
            Assert.Equal(0, SettlementSubsistence.ProvisionIfNewlyFounded(colony));
            Assert.Equal(0, RunPasses(colony, 10));
            Assert.Empty(colony.Stores);
        }

        // -------------------------------------------------------------------------------------------
        // The gate, and the world-wide entry point.
        // -------------------------------------------------------------------------------------------

        /// <summary>Self-gated on the same cadence the ledger's consumer runs on, so the two halves of one
        /// book are never read on two clocks — and a tick it is not due on costs a modulo.</summary>
        [Fact]
        public void The_pass_only_fires_on_its_own_interval()
        {
            CoreWorld world = NewWorld("subsist-gate");
            Settlement settlement = SettlementOn(world, "TropicalRainforest");
            for (int i = 0; i < 5; i++) OffMapCitizen(settlement, "Hand" + i);

            Find.TickManager.DebugSetTicksGame(SettlementSubsistenceTuning.IntervalTicks + 1);
            Assert.Equal(0, SettlementSubsistence.TickSettlement(settlement));
            Assert.Empty(settlement.Stores);

            Find.TickManager.DebugSetTicksGame(2 * SettlementSubsistenceTuning.IntervalTicks);
            Assert.True(SettlementSubsistence.TickSettlement(settlement) > 0);
        }

        /// <summary>The civilization-wide entry point reaches every settlement in the world, which is the
        /// whole difference between this and the one-settlement call it replaced.</summary>
        [Fact]
        public void The_world_pass_reaches_every_settlement()
        {
            CoreWorld world = NewWorld("subsist-world");
            var tiles = Enumerable.Range(0, world.grid.TilesCount).Where(i => !world.grid.Tiles[i].WaterCovered).Take(3).ToList();
            var settlements = new List<Settlement>();
            for (int i = 0; i < 3; i++)
            {
                Settlement settlement = SettlementOn(world, "TemperateForest", "Town" + i, tileOverride: tiles[i]);
                for (int c = 0; c < 4; c++) OffMapCitizen(settlement, "Town" + i + "Hand" + c);
                settlements.Add(settlement);
            }

            Find.TickManager.DebugSetTicksGame(3 * SettlementSubsistenceTuning.IntervalTicks);
            global::SimWorld.Economy.SettlementSubsistence.Tick();

            foreach (Settlement settlement in settlements)
            {
                Assert.True(Stored(settlement) > 0f, settlement.name + " was not reached by the world pass");
            }
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The module's round trip. This class holds no state of its own — the roster, the tile and the ledger
        /// are re-derived on every pass — so what a save has to preserve is the ledger, and what it has to
        /// prove is that a reloaded settlement goes on growing at exactly the rate its own state says it
        /// should. Holding nothing is the design, not an accident: see <c>SettlementLarder</c>, which takes the
        /// same position on the consumption side.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_keeps_the_ledger_and_the_settlement_goes_on_growing()
        {
            CoreWorld world = NewWorld("subsist-scribe");
            Faction faction = world.factions.First();

            // The tile's own generated biome, not one this test paints on: World.ExposeData rebuilds the grid
            // from the world seed on load (World.RegenerateGrid), so a hand-set biome would not survive the
            // round trip and the reloaded settlement would be standing on different land than the original.
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i =>
                !world.grid.Tiles[i].WaterCovered && (world.grid.Tiles[i].biome?.forageability ?? 0f) > 0f);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(4242), "Subsisthome");

            int producedBefore = RunPasses(settlement, 3);
            Assert.True(producedBefore > 0);
            float ledgerBefore = Stored(settlement);

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            Find.World = loaded;

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Subsisthome");
            Assert.Equal(ledgerBefore, Stored(reloaded), 3);

            // The next pass on the reloaded settlement produces exactly what it would have produced had
            // nothing been saved — the proof that nothing that mattered lived outside the ledger.
            Find.World = world;
            int next = RunPasses(settlement, 1, firstPass: 4);
            Find.World = loaded;
            Assert.Equal(next, RunPasses(reloaded, 1, firstPass: 4));
        }

        private static int OtherLandTile(CoreWorld world, int notThis) =>
            Enumerable.Range(0, world.grid.TilesCount).First(i => i != notThis && !world.grid.Tiles[i].WaterCovered);
    }
}
