using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
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
using Xunit.Abstractions;

using CoreBuilding = SimWorld.Building.Building;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Renewal (<c>docs/design/era-progression.md</c> §7): the town rebuilds itself as it climbs. Every test here
    /// runs on a real generated settlement, as <c>EdictProgressionMapTests</c> does.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RenewalTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public RenewalTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static (Game Game, Settlement Settlement) Opened(string scenario, string seed)
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed(scenario).scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return (game, settlement);
        }

        private static Pawn ACitizen(Settlement settlement) => settlement.Citizens.First(p => p.Spawned && !p.Dead);

        /// <summary>A standable cell with nothing on it and nothing next to it, so a wall placed there blocks nobody.</summary>
        private static IntVec3 ClearCell(CoreMap map) =>
            map.AllCells.First(c => c.x > 5 && c.z > 5 && c.x < map.Size.x - 5 && c.z < map.Size.z - 5
                && GenAdj.CellsAdjacent8Way(c).Append(c).All(n => GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0));

        private T Reload<T>(Game game, System.Func<Game, T> find, out Game loaded)
        {
            string xml = Scribe.SaveToString(game, "game");
            loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            return find(loaded);
        }

        private static CoreMap MapOf(Game game) => game.World!.worldObjects.OfType<Settlement>().First().InteriorMap!;

        // ---- step 1: when a building was built ----

        [Fact]
        public void A_building_finished_on_a_tick_reports_that_tick_after_a_save_and_load()
        {
            (Game game, Settlement settlement) = Opened("TribalStart", "renewal-built");
            CoreMap map = settlement.InteriorMap!;
            for (int i = 0; i < 777; i++) game.TickManager.DoSingleTick();
            int finishedOn = game.TickManager.TicksGame;

            IntVec3 cell = ClearCell(map);
            var bp = (Blueprint)ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(Def("Wall"))!);
            GenSpawn.Spawn(bp, cell, map);
            var wall = (CoreBuilding)bp.ReplaceWithFrame().CompleteConstruction(ACitizen(settlement));
            Assert.Equal(finishedOn, wall.builtTick);
            int id = wall.thingIDNumber;

            for (int i = 0; i < 300; i++) game.TickManager.DoSingleTick();
            var loadedWall = Reload(game, g => (CoreBuilding)MapOf(g).listerThings.ThingsOfDef(Def("Wall")).Single(w => w.thingIDNumber == id), out _);
            Assert.Equal(finishedOn, loadedWall.builtTick);
            Assert.NotEqual(finishedOn, Find.TickManager.TicksGame);
        }

        [Fact]
        public void A_scenarios_starting_buildings_carry_the_start_tick()
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("AgrarianVillage").scenario, "renewal-start", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            int openedOn = game.TickManager.TicksGame;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);

            List<CoreBuilding> buildings = settlement.InteriorMap!.listerThings.ThingsInGroup(ThingRequestGroup.Building).OfType<CoreBuilding>().ToList();
            output.WriteLine(buildings.Count + " buildings at the start, opened on tick " + openedOn);
            Assert.NotEmpty(buildings);
            Assert.All(buildings, b => Assert.Equal(openedOn, b.builtTick));
        }
    }
}
