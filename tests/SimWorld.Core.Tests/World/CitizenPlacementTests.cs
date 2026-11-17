using System.Linq;
using SimWorld.Map;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.World
{
    public class CitizenPlacementTests : ContentTestBase
    {
        public CitizenPlacementTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>
        /// Every founding citizen starts on the map's main open ground: from where they stand they can walk to
        /// most of the map's open border. On "stock-end-to-end" the nearest standable ground to the map's centre
        /// is a pocket sealed in limestone, and the band used to be put down in it (eighteen of twenty-two, who
        /// never got out: nothing designates rock to free them). On "simworld-host" a border cell sealed off in
        /// a corner once stood for "the open map", and the whole band was founded beside it, idle for good.
        /// </summary>
        [Theory]
        [InlineData("stock-end-to-end")]
        [InlineData("simworld-host")]
        public void Every_citizen_is_founded_on_the_maps_main_open_ground(string seed)
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            CoreMap map = game.EnterSettlement(settlement);

            var border = map.AllCells.Where(c => (c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1) && GenGrid.Standable(c, map)).ToList();
            var spawned = settlement.Citizens.Where(c => c.Spawned).ToList();
            Assert.NotEmpty(spawned);
            Assert.All(spawned, c =>
            {
                int reached = border.Count(b => map.reachability.CanReachCells(c.Position, b));
                Assert.True(reached * 2 > border.Count, $"{c} at {c.Position} reaches {reached} of {border.Count} open border cells");
            });
        }
    }
}
