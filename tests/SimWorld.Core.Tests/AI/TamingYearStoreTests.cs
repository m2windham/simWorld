using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// The taming gate reads the settlement's whole store against a year of everybody's eating — see
    /// <see cref="TamingTuning.DaysOfFeedWanted"/>. Measured before this: the gate read four days of the map
    /// alone, so a harvest lying unhauled in the field bought a herd that then ate out of the ledger all
    /// year, and animals out-ate the people (10-year arc, band 20 seed 99: 4,392 nutrition a year to the
    /// animals against 2,478 to the people).
    /// </summary>
    public class TamingYearStoreTests : ContentTestBase
    {
        public TamingYearStoreTests(CoreContentFixture content) : base(content)
        {
        }

        private const int Citizens = 5;

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Food => Def("MealSimple");

        private static (CoreMap map, Settlement settlement) NewSettlement()
        {
            var map = new CoreMap(24, 24, SimWorld.Map.TerrainDefOf.Soil);
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Herders", 0);
            for (int i = 0; i < Citizens; i++)
            {
                Pawn p = NewHuman("Citizen" + i);
                settlement.AddCitizen(p);
                GenSpawn.Spawn(p, new IntVec3(1 + i, 0, 1), map);
            }
            for (int i = 0; i < 6; i++)
            {
                GenSpawn.Spawn(new Pawn(Def("Chicken"), "Wild" + i), new IntVec3(12 + i, 0, 12), map);
            }
            return (map, settlement);
        }

        private static float PeoplePerDay(Settlement settlement, CoreMap map) =>
            HuntingInitiative.NutritionWanted(settlement, map) / HuntingTuning.DaysOfFoodWanted;

        private static void PutOnMap(CoreMap map, float nutrition)
        {
            Thing pile = ThingMaker.MakeThing(Food);
            pile.stackCount = (int)(nutrition / Food.ingestible!.nutrition) + 1;
            GenSpawn.Spawn(pile, new IntVec3(20, 0, 20), map);
        }

        private static void PutInLedger(Settlement settlement, float nutrition) =>
            settlement.AddStore(Food, (int)(nutrition / Food.ingestible!.nutrition) + 1);

        [Fact]
        public void A_harvest_lying_in_the_field_does_not_buy_a_herd_the_store_cannot_feed()
        {
            // Twelve days of the people's food on the ground and nothing in the ledger: well above the four-day
            // larder, nowhere near a year. That pile is next week's dinner, not a herd's year of feed.
            (CoreMap map, Settlement settlement) = NewSettlement();
            PutOnMap(map, PeoplePerDay(settlement, map) * 12f);

            Assert.False(HuntingInitiative.WantsMeat(settlement, map), "Twelve days of food is not short.");
            Assert.Equal(0, TamingInitiative.HerdWanted(settlement, map));
            Assert.False(TamingInitiative.WantsLivestock(settlement, map));
        }

        [Fact]
        public void Food_banked_in_the_ledger_counts_toward_the_herd()
        {
            // The map held at exactly the four-day larder, as SettlementStockInitiative holds it, and the
            // granary's surplus banked in the ledger: the store is full, so it can keep livestock.
            (CoreMap map, Settlement settlement) = NewSettlement();
            PutOnMap(map, HuntingInitiative.NutritionWanted(settlement, map));
            PutInLedger(settlement, TamingInitiative.NutritionReserved(settlement, map)
                + TamingInitiative.FeedCostPerHead(map) * 3f);

            Assert.True(TamingInitiative.HerdWanted(settlement, map) > 0,
                "A year's food banked over the people's own buys livestock wherever it is stored.");
            Assert.True(TamingInitiative.WantsLivestock(settlement, map));
        }

        [Theory]
        [InlineData(4f)]
        [InlineData(15f)]
        [InlineData(45f)]
        [InlineData(90f)]
        [InlineData(240f)]
        public void The_herd_a_store_buys_is_a_herd_that_store_can_feed_for_a_year(float daysOfFood)
        {
            // Whatever the store holds, and wherever it lies, the herd's year of feed never comes to more than
            // what the store holds beyond the people's own year — and below that year it buys no herd at all.
            (CoreMap map, Settlement settlement) = NewSettlement();
            float perDay = PeoplePerDay(settlement, map);
            PutOnMap(map, perDay * daysOfFood);

            float held = HuntingInitiative.NutritionAvailable(settlement, map);
            int herd = TamingInitiative.HerdWanted(settlement, map);
            float herdYear = herd * TamingInitiative.FeedCostPerHead(map);
            float peopleYear = perDay * SimWorld.Building.FarmingTuning.DaysOfFoodStored;

            Assert.True(herdYear <= System.Math.Max(0f, held - peopleYear),
                $"{daysOfFood} days held ({held:0}) bought {herd} head: people's year {peopleYear:0} + herd's year {herdYear:0}");
        }
    }
}
