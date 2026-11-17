using System.Collections.Generic;
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
        /// the largest connected stretch of the map's open border. Citizens are put down around what the settlement has built, and that
        /// used to include the ruins map generation scatters; on "stock-end-to-end" their centroid sat in a pocket
        /// sealed in limestone, and eighteen of twenty-two founders never got out (nothing designates rock to
        /// free them). On "player-recalls-a-citizen" the nearest standable cell to the map's centre is such a pocket.
        /// "simworld-host" is the host's own seed, whose border has a sealed-off corner.
        /// </summary>
        [Theory]
        [InlineData("stock-end-to-end")]
        [InlineData("simworld-host")]
        [InlineData("player-recalls-a-citizen")]
        public void Every_citizen_is_founded_on_the_maps_main_open_ground(string seed)
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            CoreMap map = game.EnterSettlement(settlement);

            var border = map.AllCells.Where(c => (c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1) && GenGrid.Standable(c, map)).ToList();
            var spawned = settlement.Citizens.Where(c => c.Spawned).ToList();
            Assert.NotEmpty(spawned);
            // The largest group of mutually reachable open border cells: the map's main open ground.
            var groups = new List<List<IntVec3>>();
            foreach (IntVec3 b in border)
            {
                List<IntVec3>? g = groups.FirstOrDefault(x => map.reachability.CanReachCells(x[0], b));
                if (g == null) groups.Add(new List<IntVec3> { b });
                else g.Add(b);
            }
            List<IntVec3> main = groups.OrderByDescending(g => g.Count).First();
            Assert.All(spawned, c => Assert.True(map.reachability.CanReachCells(c.Position, main[0]),
                $"{c} at {c.Position} cannot walk to the map's main open ground ({main.Count} of {border.Count} open border cells)"));
        }
    }
}
