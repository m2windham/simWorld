using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The orders offered beside one person. The second playtest offered a baby Mine, Hunt and Tend patient.
    /// RimWorld's right-click menu offers a colonist only what they are capable of.
    /// </summary>
    [Collection("GlobalDefs")]
    public class OrderableJobsForTests : ContentTestBase
    {
        public OrderableJobsForTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        [Fact]
        public void A_baby_is_offered_no_orders_and_an_able_adult_is_offered_their_work()
        {
            Settlement settlement = OpenedSettlement("orders-baby");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] people = settlement.Citizens.Where(p => p.Spawned && p.Map == map && !p.Downed).ToArray();
            Pawn adult = people.First(p => !p.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("Mining")));
            Pawn baby = people.First(p => p != adult);

            var adultOrders = LeverCatalogue.OrderableJobsFor(adult.thingIDNumber).Select(o => o.DefName).ToList();
            Assert.Contains("Goto", adultOrders);
            Assert.Contains("Mine", adultOrders);

            baby.ageTracker.DebugSetAge(0.5f);
            Assert.Equal(DevelopmentalStage.Baby, baby.ageTracker.CurLifeStage!.developmentalStage);
            Assert.Empty(LeverCatalogue.OrderableJobsFor(baby.thingIDNumber));
        }

        [Fact]
        public void Work_a_person_may_never_do_is_not_offered_to_them()
        {
            Settlement settlement = OpenedSettlement("orders-disabled");
            CoreMap map = settlement.InteriorMap!;
            Pawn[] people = settlement.Citizens.Where(p => p.Spawned && p.Map == map && !p.Downed).ToArray();

            foreach (Pawn p in people)
            {
                var offeredInOrder = LeverCatalogue.OrderableJobsFor(p.thingIDNumber).Select(o => o.DefName).ToList();
                var offered = offeredInOrder.ToHashSet();
                Assert.Equal(!p.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("Hunting")), offered.Contains("Hunt"));
                Assert.Equal(!p.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("Doctor")), offered.Contains("TendPatient"));
                // Offered ⊆ the full catalogue, in its order.
                Assert.Equal(LeverCatalogue.OrderableJobs().Select(o => o.DefName).Where(offered.Contains), offeredInOrder);
            }
        }

        [Fact]
        public void Nobody_by_that_id_is_offered_nothing()
        {
            OpenedSettlement("orders-nobody");
            Assert.Empty(LeverCatalogue.OrderableJobsFor(-1));
        }
    }
}
