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
        /// On this seed the nearest standable ground to the map's centre is a pocket sealed in limestone, and the
        /// founding band used to be put down in it: eighteen of twenty-two citizens who could never walk out,
        /// since nothing designates rock to free them. Every citizen now starts where the open map is reachable.
        /// </summary>
        [Fact]
        public void No_citizen_is_founded_in_a_pocket_sealed_off_from_the_open_map()
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "stock-end-to-end", subdivisionOverride: 3, soloStart: true);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            CoreMap map = game.EnterSettlement(settlement);

            IntVec3 edge = map.AllCells.First(c => (c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1) && GenGrid.Standable(c, map));
            var spawned = settlement.Citizens.Where(c => c.Spawned).ToList();
            Assert.NotEmpty(spawned);
            Assert.All(spawned, c => Assert.True(map.reachability.CanReachCells(c.Position, edge), $"{c} at {c.Position} is sealed in"));
        }
    }
}
