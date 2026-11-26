using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// "Food (days)" on the stock panel (host UX request after playtest 4): <see cref="SettlementSummary.FoodDays"/>,
    /// the food in both books over what the citizens eat in a day at their own hunger rates, on a real generated
    /// settlement.
    /// </summary>
    [Collection("GlobalDefs")]
    public class FoodDaysTests : ContentTestBase
    {
        public FoodDaysTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static SettlementSummary Summary(int tile) => GodViewSnapshot.Capture().Settlements.Single(s => s.Tile == tile);

        [Fact]
        public void Food_days_are_the_stores_over_what_the_citizens_eat_a_day()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "food-days", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;

            SettlementSummary before = Summary(settlement.tile);
            float eaten = settlement.Citizens.Where(p => !p.Dead).Sum(p => HuntingTuning.NutritionPerEaterPerDay * p.HungerRate);
            Assert.True(eaten > 0f);
            Assert.Equal(eaten, before.NutritionPerDay, 3);
            Assert.NotNull(before.FoodDays);
            Assert.Equal(before.StoredNutrition / before.NutritionPerDay, before.FoodDays!.Value, 3);

            // More food in store is more days, at the same consumption.
            ThingDef berries = DefDatabase<ThingDef>.GetNamed("RawBerries");
            Thing stack = ThingMaker.MakeThing(berries);
            stack.stackCount = 50;
            IntVec3 near = settlement.Citizens.First(p => p.Spawned && p.Map == map).Position;
            IntVec3 cell = GenRadial.RadialPattern.Select(o => near + o)
                .First(c => GenGrid.InBounds(c, map) && GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0);
            GenSpawn.Spawn(stack, cell, map);

            SettlementSummary after = Summary(settlement.tile);
            Assert.Equal(before.NutritionPerDay, after.NutritionPerDay, 5);
            Assert.Equal(before.FoodDays!.Value + 50 * berries.ingestible!.nutrition / before.NutritionPerDay, after.FoodDays!.Value, 3);
        }
    }
}
