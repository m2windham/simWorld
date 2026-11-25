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

        // ---- step 2: replace in place ----

        /// <summary>A finished wooden wall of the settlement's own, on a clear cell.</summary>
        private static CoreBuilding WoodWall(Settlement settlement, CoreMap map)
        {
            var wall = (CoreBuilding)ThingMaker.MakeThing(Def("Wall"), Def("WoodLog"));
            wall.SetFaction(ACitizen(settlement).Faction);
            GenSpawn.Spawn(wall, ClearCell(map), map);
            return wall;
        }

        private static int[] PathAround(CoreMap map, IntVec3 c) =>
            GenAdj.CellsAdjacent8Way(c).Append(c).Select(n => map.pathGrid.PerceivedPathCostAt(n)).ToArray();

        [Fact]
        public void A_stone_wall_over_a_wood_wall_leaves_the_wood_standing_until_the_frame_completes()
        {
            (Game game, Settlement settlement) = Opened("TribalStart", "renewal-replace");
            CoreMap map = settlement.InteriorMap!;
            CoreBuilding wood = WoodWall(settlement, map);
            IntVec3 cell = wood.Position;
            int[] path = PathAround(map, cell);
            Assert.False(map.pathGrid.Walkable(cell));

            // An ordinary blueprint cannot go on a standing wall; a replacement can.
            Assert.False(GenConstruct.CanPlaceBlueprintAt(Def("WallGranite"), cell, map, out _));
            Blueprint bp = Assert.IsType<Blueprint>(GenConstruct.PlaceReplacement(Def("WallGranite"), null, cell, map));
            Assert.Same(wood, GenConstruct.ThingToReplace(bp));
            Assert.Null(GenConstruct.PlaceReplacement(Def("WallGranite"), null, cell, map)); // one site per cell

            Frame frame = bp.ReplaceWithFrame();
            Assert.True(wood.Spawned);
            Assert.Same(wood, map.edificeGrid[cell]);
            Assert.Equal(path, PathAround(map, cell));
            Assert.True(Dwelling.IsBuilt(map, DwellingPartKind.Wall, cell));

            // Saved and loaded mid-replacement, the old wall still holds the cell and the frame still finishes.
            for (int i = 0; i < 60; i++) game.TickManager.DoSingleTick();
            CoreMap loaded = Reload(game, MapOf, out _);
            Assert.Equal(Def("Wall"), loaded.edificeGrid[cell]!.def);
            Frame loadedFrame = loaded.thingGrid.ThingsListAt(cell).OfType<Frame>().Single();
            Thing built = loadedFrame.CompleteConstruction(loaded.mapPawns.AllPawnsSpawned.First(p => p.RaceProps.Humanlike));

            List<Thing> standing = loaded.thingGrid.ThingsListAt(cell).Where(t => t.def.category == ThingCategory.Building).ToList();
            Assert.Same(built, Assert.Single(standing));
            Assert.Equal(Def("WallGranite"), built.def);
            Assert.Same(built, loaded.edificeGrid[cell]);
            Assert.DoesNotContain(loaded.listerThings.ThingsOfDef(Def("Wall")), w => w.Position == cell);
            Assert.Equal(path, PathAround(loaded, cell));
            // The wood comes back as deconstructing it would, beside the wall and never inside it.
            Assert.DoesNotContain(loaded.thingGrid.ThingsListAt(cell), t => t.def == Def("WoodLog"));
        }

        [Fact]
        public void A_cancelled_replacement_leaves_the_old_wall_untouched()
        {
            (_, Settlement settlement) = Opened("TribalStart", "renewal-cancel");
            CoreMap map = settlement.InteriorMap!;
            CoreBuilding wood = WoodWall(settlement, map);
            IntVec3 cell = wood.Position;
            int hp = wood.HitPoints;
            int[] path = PathAround(map, cell);

            GenConstruct.PlaceReplacement(Def("WallGranite"), null, cell, map)!.Destroy(DestroyMode.Cancel);
            Assert.True(wood.Spawned);
            Assert.Same(wood, map.edificeGrid[cell]);

            Frame frame = GenConstruct.PlaceReplacement(Def("WallGranite"), null, cell, map)!.ReplaceWithFrame();
            frame.AddMaterial(Def("BlocksGranite"), 3);
            frame.Cancel();
            Assert.True(wood.Spawned);
            Assert.Equal(hp, wood.HitPoints);
            Assert.Same(wood, map.edificeGrid[cell]);
            Assert.Equal(path, PathAround(map, cell));
            Assert.Single(map.thingGrid.ThingsListAt(cell), t => t.def.category == ThingCategory.Building);
        }

        [Fact]
        public void Only_a_wall_replaces_a_wall_and_never_the_same_wall_again()
        {
            (_, Settlement settlement) = Opened("TribalStart", "renewal-tags");
            CoreMap map = settlement.InteriorMap!;
            CoreBuilding wood = WoodWall(settlement, map);
            Assert.False(GenConstruct.CanReplace(Def("Wall"), Def("WoodLog"), wood));
            Assert.True(GenConstruct.CanReplace(Def("Wall"), Def("Steel"), wood));
            Assert.True(GenConstruct.CanReplace(Def("WallGranite"), null, wood));
            Assert.False(GenConstruct.CanReplace(Def("Door"), Def("WoodLog"), wood));
            Assert.False(GenConstruct.CanReplace(Def("Bed"), Def("WoodLog"), wood));
        }
    }
}
