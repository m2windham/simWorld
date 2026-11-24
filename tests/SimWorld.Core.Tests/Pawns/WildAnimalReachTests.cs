using System.Linq;

using SimWorld.AI;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Pawns
{
    /// <summary>
    /// Wildlife stands where the band can walk to it. On the second playtest's kind of start, a hilly tile split
    /// into sealed rock pockets, five of seven animals were born in pockets no hunter could reach, and the
    /// hunter's mandate found nothing to hunt (measured on seed "hunters-mandate": 4,698 of 14,219 standable
    /// cells reachable from the band, 2 of 7 animals).
    /// </summary>
    [Collection("GlobalDefs")]
    public class WildAnimalReachTests : ContentTestBase
    {
        public WildAnimalReachTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        [Theory]
        [InlineData("hunters-mandate")]
        [InlineData("mandate-idle")]
        public void Every_animal_a_map_is_born_with_is_within_the_bands_reach(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = settlement.Citizens.First(p => p.Spawned && p.Map == map);

            var animals = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn).OfType<Pawn>().Where(HuntUtility.IsHuntableAnimal).ToList();
            Assert.NotEmpty(animals);
            Assert.All(animals, a => Assert.True(map.reachability.CanReachCells(citizen.Position, a.Position), a.Label + " at " + a.Position + " is out of the band's reach"));
        }
    }
}
