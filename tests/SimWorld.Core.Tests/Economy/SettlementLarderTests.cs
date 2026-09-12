using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Scenario;
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
using SettlementLarderTuning = global::SimWorld.Economy.SettlementLarderTuning;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>The seam between the two halves of the game, in the ledger-to-citizen direction</b> (spec §11.2) —
    /// a citizen with no map to act in eats what the civilization has.
    ///
    /// <para/><b>The defect these pin.</b> Two lanes landed in one batch and joined badly. Off-map citizens
    /// started ticking (<c>Sim.CitizenTickRegistry</c>), and the food economy was built — but every part of
    /// that economy is map-based, and <c>Game.NewGame</c> focuses the settlement it founds without opening its
    /// interior. So a settlement nobody looked at held twenty-five Full-tier citizens with full-speed hunger
    /// and no world to eat in. Measured over eight in-game days, unwatched, before this class existed:
    ///
    /// <para/><code>
    /// day | alive | mean food | mean Malnutrition severity
    ///   0 |    25 |      0.80 | 0.00
    ///   1 |    25 |      0.00 | 0.01
    ///   4 |    22 |      0.00 | 0.35
    ///   8 |    21 |      0.00 | 0.80   (lethal at 1.00)
    /// </code>
    ///
    /// <para/>...and the ledger a settlement was founded with held <c>MeleeWeapon_Knife x2</c>, which is why
    /// closing the consumption path alone would not have been enough: see
    /// <see cref="A_founding_band_walks_in_carrying_food"/>.
    ///
    /// <para/><b>What these assert, and what they deliberately do not.</b> That nobody is lost to <i>hunger</i>
    /// — stated the way the simulation states it, with the <c>Malnutrition</c> hediff — and never that the
    /// population is untouched. It is not: every death in both configurations is a citizen beaten to death by
    /// another citizen in a mental break, which <c>docs/WORK-REGISTER.md</c> §9a traces to <c>Need_Joy</c>
    /// having no source in this codebase. Asserting survival here would be asserting that three other systems
    /// are healthy and would go red for reasons a food test cannot explain — the same line
    /// <c>Integration.SettlementFoodTests</c> draws for the watched half of the same problem.
    /// </summary>
    public class SettlementLarderTests : ContentTestBase
    {
        public SettlementLarderTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Berries => Def("RawBerries");

        private static ThingDef Meal => Def("MealSimple");

        private static ThingDef Knife => Def("MeleeWeapon_Knife");

        private static HediffDef Malnutrition => DefDatabase<HediffDef>.GetNamed("Malnutrition");

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "Larderhome", 0);

        /// <summary>A citizen of <paramref name="settlement"/>, off any map, at <paramref name="foodLevel"/>
        /// of a full stomach — the state this whole class is about.</summary>
        private static Pawn OffMapCitizen(Settlement settlement, float foodLevel, string name = "Eater")
        {
            Pawn pawn = NewHuman(name);
            settlement.AddCitizen(pawn);
            pawn.needs.food!.CurLevelPercentage = foodLevel;
            return pawn;
        }

        private static float Food(Pawn pawn) => pawn.needs.food!.CurLevel;

        // -------------------------------------------------------------------------------------------
        // The predicate: who eats from the ledger, and who must never.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The predicate is "has no map to act in", not a tier. <c>AI.JobGiver_GetFood</c> returns null the
        /// instant <c>pawn.Map</c> is null, so an unspawned citizen cannot reach food by any map-side route —
        /// at <i>any</i> tier, Full included, which is exactly the case this class exists for.
        /// </summary>
        [Fact]
        public void A_hungry_citizen_with_no_map_eats_from_the_ledger()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.2f);
            settlement.AddStore(Berries, 100);

            Assert.False(citizen.Spawned);
            Assert.Equal(PawnTier.Full, citizen.tier.Tier); // the tier says nothing; the missing map does
            Assert.True(SettlementLarder.WouldEatFromStores(citizen));

            float before = Food(citizen);
            Assert.Equal(1, SettlementLarder.Run(settlement));
            Assert.True(Food(citizen) > before);
        }

        /// <summary>
        /// The guard that keeps this class from being a double-feed: a citizen standing on a live map walks
        /// to a meal and eats it (<c>AI.JobGiver_GetFood</c> → <c>AI.JobDriver_Ingest</c>), and the ledger
        /// must not feed them as well. Feeding both ways would also break
        /// <c>Economy.SettlementStockInitiative</c>'s invariant from the other side — the same mouth paid for
        /// twice, once out of a Thing on the map and once out of a count in the ledger.
        /// </summary>
        [Fact]
        public void A_citizen_standing_on_a_map_is_never_fed_from_the_ledger()
        {
            var map = new CoreMap(12, 12, TerrainDefOf.Soil);
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.1f);
            settlement.AddStore(Berries, 100);

            GenSpawn.Spawn(citizen, new IntVec3(6, 0, 6), map);
            Assert.True(citizen.Spawned);

            float before = Food(citizen);
            Assert.False(SettlementLarder.WouldEatFromStores(citizen));
            Assert.Equal(0, SettlementLarder.Run(settlement));
            Assert.Equal(before, Food(citizen), 4);
            Assert.Equal(100, settlement.StoreCountOf(Berries));
        }

        /// <summary>Hunger is asked the same way the map path asks it (<c>AI.ThinkNode_ConditionalHungry</c>:
        /// any category but <see cref="HungerCategory.Fed"/>), so a citizen who is not hungry does not empty
        /// the civilization's stores to top itself up.</summary>
        [Fact]
        public void A_citizen_who_is_not_hungry_does_not_eat()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 1f);
            settlement.AddStore(Berries, 100);

            Assert.Equal(HungerCategory.Fed, citizen.needs.food!.CurCategory);
            Assert.Equal(0, SettlementLarder.Run(settlement));
            Assert.Equal(100, settlement.StoreCountOf(Berries));
        }

        // -------------------------------------------------------------------------------------------
        // Nothing is conjured: what a stomach gains, the ledger loses.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The claim that separates "ate from the ledger" from "was handed free food": nutrition gained by
        /// the citizen equals nutrition removed from the ledger, exactly, in the same pass. Stated as a
        /// conservation law rather than as a level, because the level is the food's own content number.
        /// </summary>
        [Fact]
        public void What_a_citizen_gains_is_exactly_what_the_ledger_loses()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.25f);
            settlement.AddStore(Berries, 200);

            float foodBefore = Food(citizen);
            float ledgerBefore = SettlementLarder.StoredNutrition(settlement);

            Assert.True(SettlementLarder.TryFeed(settlement, citizen));

            float gained = Food(citizen) - foodBefore;
            float spent = ledgerBefore - SettlementLarder.StoredNutrition(settlement);
            Assert.True(spent > 0f, "the ledger paid nothing, so the nutrition came from nowhere");
            Assert.Equal(spent, gained, 4);
        }

        /// <summary>
        /// A settlement whose ledger is empty starves, and that is the correct outcome — it means that
        /// settlement's production is broken, which is a different problem. Nothing here invents a meal for a
        /// hungry citizen who has nothing: the citizen still <i>wants</i> to eat, and still does not.
        /// </summary>
        [Fact]
        public void An_empty_ledger_feeds_nobody()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.1f);
            settlement.AddStore(Knife, 2); // held goods, but nothing edible — the shipped founding ledger

            float before = Food(citizen);
            Assert.True(SettlementLarder.WouldEatFromStores(citizen), "a starving citizen still wants to eat");
            Assert.Null(SettlementLarder.BestStoredFood(settlement));
            Assert.Equal(0, SettlementLarder.Run(settlement));
            Assert.Equal(before, Food(citizen), 4);
            Assert.Equal(2, settlement.StoreCountOf(Knife));
        }

        /// <summary>A sitting, not a mouthful: the same <c>Crafting.FoodUtility.WillIngestStackCountOf</c> the
        /// map path eats by, so a raw foodstuff at 0.05 nutrition against a 1.0 stomach still fills somebody
        /// up. One unit per pass would be the starvation bug <c>JobDriver_Ingest</c> already had.</summary>
        [Fact]
        public void A_citizen_eats_a_sitting_rather_than_a_unit()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.2f);
            settlement.AddStore(Berries, 500);

            Assert.True(SettlementLarder.TryFeed(settlement, citizen));

            int eaten = 500 - settlement.StoreCountOf(Berries);
            Assert.True(eaten > 1, "one unit per pass is the mouthful bug, one tier up");
            Assert.Equal(HungerCategory.Fed, citizen.needs.food!.CurCategory);
        }

        /// <summary>The best food the ledger holds goes first — <c>Crafting.FoodPreferability</c>, RimWorld's
        /// own ordering, then nutrition per unit, then defName. A total order: nothing here can tie, so the
        /// choice never falls through to ledger insertion order.</summary>
        [Fact]
        public void The_best_food_in_the_ledger_is_eaten_first()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.2f);
            settlement.AddStore(Berries, 100);
            settlement.AddStore(Meal, 10);

            Assert.Same(Meal, SettlementLarder.BestStoredFood(settlement));
            Assert.True(SettlementLarder.TryFeed(settlement, citizen));
            Assert.True(settlement.StoreCountOf(Meal) < 10, "the meal should have been the one eaten");
            Assert.Equal(100, settlement.StoreCountOf(Berries));
        }

        /// <summary>
        /// Determinism. This runs inside the tick loop, so one draw here would shift every subsequent roll in
        /// the game by the accident of when somebody got hungry — the same statement
        /// <c>Quests.QuestRewardSink</c> and <c>Economy.SettlementStockInitiative</c> make about themselves.
        /// Which food and how much are both decided, never drawn.
        /// </summary>
        [Fact]
        public void Feeding_a_settlement_takes_no_roll_at_all()
        {
            Settlement settlement = PlainSettlement();
            for (int i = 0; i < 5; i++) OffMapCitizen(settlement, 0.15f, "Eater" + i);
            settlement.AddStore(Berries, 400);
            settlement.AddStore(Meal, 20);

            uint before = Rand.Current.Iterations;
            Assert.Equal(5, SettlementLarder.Run(settlement));
            Assert.Equal(before, Rand.Current.Iterations);
        }

        // -------------------------------------------------------------------------------------------
        // Scale: the cohort is a count, not a roster.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The defined, cheap answer for a settlement of thousands (spec §11.3): the pass is O(citizens above
        /// Statistical) and never O(population). A Statistical citizen has no <c>Pawn</c> and no tracked
        /// need, so there is nothing there to feed — the same line <c>AI.HuntingInitiative</c> draws for the
        /// demand side, so supply and demand agree about who the mouths are. A cohort that ate would be one
        /// side of a book whose other side (its production) does not exist either.
        /// </summary>
        [Fact]
        public void A_statistical_cohort_of_thousands_costs_the_same_as_nobody()
        {
            Settlement settlement = PlainSettlement();
            Pawn citizen = OffMapCitizen(settlement, 0.2f);
            settlement.AddStatisticalPeople(10_000);
            settlement.AddStore(Berries, 300);

            float ledgerBefore = SettlementLarder.StoredNutrition(settlement);
            Assert.Equal(1, SettlementLarder.Run(settlement)); // one live mouth, ten thousand abstract ones
            float spent = ledgerBefore - SettlementLarder.StoredNutrition(settlement);

            Assert.True(spent > 0f);
            Assert.True(spent <= citizen.needs.food!.MaxLevel + 0.2f,
                "ten thousand cohort members drew on the ledger: spent " + spent.ToString("F2")
                + " for one live citizen with a stomach of " + citizen.needs.food!.MaxLevel.ToString("F2"));
            Assert.Equal(10_000, settlement.StatisticalPopulation);
        }

        // -------------------------------------------------------------------------------------------
        // The founding band's rations.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The other half of the measurement, and the reason the consumption path alone would not have been
        /// enough: <b>a settlement founded the ordinary way held nothing edible at all</b>. The three shipped
        /// scenarios each supply a weapon and no food, so an abstract eater would have correctly found an
        /// empty larder and correctly starved. Asserted against the food economy's own arithmetic — mouths ×
        /// the shipped crops' own time to a first harvest × the per-eater daily burn — never against the
        /// literal count, which is derived (see <see cref="SettlementLarderTuning"/>).
        /// </summary>
        [Fact]
        public void A_founding_band_walks_in_carrying_food()
        {
            Settlement settlement = PlainSettlement();
            for (int i = 0; i < 10; i++) OffMapCitizen(settlement, 0.8f, "Founder" + i);

            Assert.Null(SettlementLarder.BestStoredFood(settlement));
            int units = SettlementLarder.ProvisionFoundingBand(settlement);
            Assert.True(units > 0);

            float carried = SettlementLarder.StoredNutrition(settlement);
            float untilFirstHarvest = 10 * SettlementLarderTuning.DaysToFirstHarvest * HuntingTuning.NutritionPerEaterPerDay;
            Assert.True(carried >= untilFirstHarvest,
                "a band carrying " + carried.ToString("F0") + " nutrition cannot reach the first harvest its "
                + "own field could give it (" + untilFirstHarvest.ToString("F0") + ")");

            // Rations, not groceries: what a band carries is the densest thing content ships that is not a
            // cooked meal, so the choice follows the content rather than a name in the code.
            ThingDef ration = SettlementLarder.ProvisionDef()!;
            Assert.False(ration.IsMeal);
            Assert.True(ration.ingestible!.nutrition >= Berries.ingestible!.nutrition);
        }

        /// <summary>More mouths, more food — the provision is per eater, so it scales with the band rather
        /// than being a constant that is generous for twenty and thin for forty.</summary>
        [Fact]
        public void A_bigger_band_carries_more()
        {
            Settlement small = PlainSettlement(1);
            for (int i = 0; i < 5; i++) OffMapCitizen(small, 0.8f, "Small" + i);
            Settlement large = PlainSettlement(2);
            for (int i = 0; i < 20; i++) OffMapCitizen(large, 0.8f, "Large" + i);

            SettlementLarder.ProvisionFoundingBand(small);
            SettlementLarder.ProvisionFoundingBand(large);

            Assert.True(SettlementLarder.StoredNutrition(large) > SettlementLarder.StoredNutrition(small));
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The module's round trip. This class holds no state of its own — every decision is re-derived from
        /// the roster and the ledger on each pass — so what a save has to preserve is the ledger the pass
        /// spends, and what it has to prove is that a reloaded settlement goes on feeding its off-map
        /// citizens out of exactly what it had left.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_keeps_the_ledger_and_the_settlement_goes_on_eating()
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                "larder-scribe", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Larder", 2, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(4242), "Larderhome");
            settlement.AddStore(Berries, 400);

            foreach (Pawn founder in settlement.Citizens) founder.needs.food!.CurLevelPercentage = 0.1f;
            SettlementLarder.Run(settlement);
            int leftBefore = settlement.StoreCountOf(Berries);
            Assert.True(leftBefore < 400, "this test needs a settlement that has actually eaten something");

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Larderhome");
            Assert.Equal(leftBefore, reloaded.StoreCountOf(Berries));

            foreach (Pawn citizen in reloaded.Citizens) citizen.needs.food!.CurLevelPercentage = 0.1f;
            Assert.True(SettlementLarder.Run(reloaded) > 0, "a reloaded settlement stopped feeding its people");
            Assert.True(reloaded.StoreCountOf(Berries) < leftBefore);
        }

        // -------------------------------------------------------------------------------------------
        // The measurement, driven through the real game loop with nothing called by hand.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// <b>The deliverable.</b> A settlement founded the ordinary way and <i>never entered</i> — no
        /// interior map exists for the whole run, so not one link of the map-side food economy is reachable —
        /// ticked for a week through <c>Game</c>'s own tick loop with nothing called by hand.
        ///
        /// <para/>Before this lane the same run read mean nutrition 0.00 from day one onward and carried
        /// <c>Malnutrition</c> at 0.80 severity across the whole surviving town by day eight, with three
        /// citizens already dead carrying it. What it asserts now: food is never pinned at the floor, the
        /// citizens are demonstrably eating out of the ledger (it falls, and nothing else can spend it), and
        /// nobody — living or dead — is far along the hediff this simulation uses to mean starvation.
        /// </summary>
        [Fact]
        public void An_unwatched_settlement_feeds_itself_out_of_its_ledger_for_a_week()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "larder-unwatched",
                subdivisionOverride: 3, soloStart: true, bandSize: 25);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            var band = settlement.Citizens.ToList();

            // Founding state: no interior, nobody on a map, and a ledger that now holds food. All three are
            // load-bearing — the first two are what makes this the unwatched case, the third is what the
            // citizens are about to live on.
            Assert.Null(settlement.InteriorMap);
            Assert.All(band, p => Assert.False(p.Spawned));

            // The rations land on the settlement's first gated pass rather than before its first tick: they
            // are owned by Economy.SettlementSubsistence.ProvisionIfNewlyFounded now, so that every settlement
            // gets them and not only the one Game.NewGame founds (a rival raised by World.EmergenceManager
            // used to walk in with an empty ledger). One long tick is a thirtieth of a day against a founding
            // provision measured in days of food.
            for (int i = 0; i < GenTicks.TickLongInterval; i++) game.TickManager.DoSingleTick();
            float ledgerAtFounding = SettlementLarder.StoredNutrition(settlement);
            Assert.True(ledgerAtFounding > 0f, "a founded settlement holds nothing edible");

            const int days = 7;
            var meanFood = new List<float>();
            for (int day = 0; day < days; day++)
            {
                for (int i = 0; i < GenDate.TicksPerDay; i++) game.TickManager.DoSingleTick();
                Assert.Null(settlement.InteriorMap); // nothing may quietly generate an interior behind this
                List<Pawn> alive = band.Where(p => !p.Dead).ToList();
                Assert.NotEmpty(alive);
                meanFood.Add(alive.Average(p => p.needs.food!.CurLevelPercentage));
            }

            // The column that used to read 0.00 for ever. Stated as "never at the floor" plus "reaches fed
            // again", not as a level: a settlement eating on a cadence swings between hungry and full, and
            // pinning either end would be pinning the cadence rather than the economy.
            Assert.All(meanFood, mean => Assert.True(mean > 0f,
                "mean nutrition across the town hit zero, which is the reading this lane exists to fix"));
            Assert.True(meanFood.Max() > new Need_Food(band[0]).PercentageThreshHungry,
                "the town's mean nutrition never once rose back above hungry (peak "
                + meanFood.Max().ToString("F2") + ")");

            // They ate from the ledger, and there is nowhere else it could have gone: no map exists, so no
            // Thing was ever spawned, banked, traded or raided. The figure is net of what
            // Economy.SettlementSubsistence grew back into the ledger over the same week, which is why the
            // assertion is a direction rather than an amount — the founding crate is several times the larder
            // a settlement settles at, so a week of eating still shows through the refilling.
            float spent = ledgerAtFounding - SettlementLarder.StoredNutrition(settlement);
            Assert.True(spent > 0f, "a week passed and the civilization's food was untouched");

            // "Does not lose citizens to hunger", stated the way the simulation states it: Malnutrition is
            // lethal at severity 1. Living and dead alike — a citizen who starved to death would otherwise
            // simply leave the roster and take the evidence with them.
            foreach (Pawn citizen in band) AssertNotDyingOfHunger(citizen);
        }

        private static void AssertNotDyingOfHunger(Pawn pawn)
        {
            Hediff? malnutrition = pawn.health.hediffSet.GetFirstHediffOfDef(Malnutrition);
            if (malnutrition == null) return;
            Assert.True(malnutrition.Severity < 0.5f,
                pawn.Label + " carries malnutrition at severity " + malnutrition.Severity.ToString("F2")
                + " (lethal at 1) after a week in a settlement whose ledger was supposed to be feeding it");
        }
    }
}
