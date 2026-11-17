using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Director;
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

namespace SimWorld.Tests.World
{
    /// <summary>
    /// A band founded on a map broken into sealed rock pockets plans and builds only on ground it can walk
    /// to (<see cref="SettlementReach"/>, <c>Settlement.GroundToJoin</c>). Seed 2024 of the storyteller
    /// bench (tile 284, AridShrubland, 46 pockets) is the case: the band was stood in one pocket and
    /// everything it planned drawn in another, so no frame was ever worked; with the band's spawn moved
    /// off the ruins and no planner gated, 19 of 20 starved by day 10.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BandReachTests : ContentTestBase
    {
        public BandReachTests(CoreContentFixture content) : base(content) { }

        [Fact]
        public void On_a_map_of_sealed_pockets_the_band_and_everything_it_plans_share_one_pocket()
        {
            Rand.Current = new RandomStream(2024);
            Pawn.ResetThingIdCounter();
            CoreMap.ResetMapIdCounter();
            var difficulty = new DifficultyDef { defName = "MediumWithoutThreats", threatScale = DifficultyDefOf.Medium.threatScale, allowBigThreats = false, allowIntroThreats = false };
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "2024", subdivisionOverride: 3, soloStart: false, bandSize: 20, difficultyDef: difficulty);
            Settlement home = game.CivilizationTarget.Seat!;
            GodCommands.OpenSettlement(home.tile);
            CoreMap map = home.InteriorMap!;

            // A quarter of a day: the compound is planned, its first frames are up and the fields are drawn.
            for (int i = 0; i < 15000; i++) game.TickManager.DoSingleTick();

            List<Pawn> band = home.Citizens.Where(c => c.Spawned && c.Map == map).ToList();
            Assert.NotEmpty(band);
            IntVec3 from = band[0].Position;
            Assert.All(band, c => Assert.True(map.reachability.CanReachCells(from, c.Position), $"{c} stands in another pocket"));

            Assert.NotNull(map.settlementCompound);
            Assert.True(map.reachability.CanReachCells(from, map.settlementCompound!.DoorCell), "the compound's door is out of the band's reach");

            var planned = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                .Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).Where(t => t.Faction != null))
                .ToList();
            Assert.NotEmpty(planned);
            Assert.All(planned, t => Assert.True(
                GenAdj.CellsAdjacent8Way(t.Position).Append(t.Position).Any(c => GenGrid.InBounds(c, map) && map.reachability.CanReachCells(from, c)),
                $"{t} at {t.Position} is out of the band's reach"));

            // A field is drawn on reachable ground; a few of its cells can be closed in afterwards by what the
            // settlement itself builds round them, which is not a pocket. Drawn in another pocket, none would be.
            var fieldCells = map.zoneManager.AllZones.OfType<Zone_Growing>().SelectMany(z => z.Cells).ToList();
            Assert.NotEmpty(fieldCells);
            int reachable = fieldCells.Count(c => map.reachability.CanReachCells(from, c));
            Assert.True(reachable * 10 >= fieldCells.Count * 9, $"only {reachable} of {fieldCells.Count} field cells are in the band's reach");
        }
    }
}
