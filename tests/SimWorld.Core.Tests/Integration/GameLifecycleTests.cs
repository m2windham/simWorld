using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Quests;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreScenario = SimWorld.Scenario.Scenario;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Integration
{
    /// <summary>
    /// <see cref="Game"/> walking the same seams <see cref="FullStackTests"/> walks by hand — world
    /// generation, settlement founding, an entered interior map, ticking pawns — but through the orchestrator
    /// that is now supposed to own them, plus the whole-game Scribe round trip: the test that matters most
    /// for this module, since nothing before it ever saved and reloaded a running game as a single unit.
    /// </summary>
    public class GameLifecycleTests : ContentTestBase
    {
        public GameLifecycleTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreScenario TribalStart() => ScenarioDefOf.TribalStart.scenario;

        private static Game NewSoloGame(string seed, int subdivision = 3) =>
            Game.NewGame(TribalStart(), seed, subdivisionOverride: subdivision, soloStart: true);

        [Fact]
        public void NewGame_walks_world_generation_through_founding_the_same_way_FullStackTests_does_by_hand()
        {
            Game game = NewSoloGame("game-lifecycle-fullstack");

            CoreWorld world = Assert.IsType<CoreWorld>(game.World);
            Assert.NotEmpty(world.regions);
            Assert.NotEmpty(world.Settlements);

            Settlement settlement = world.worldObjects.OfType<Settlement>().First();
            Assert.InRange(settlement.TotalPopulation, SettlementTuning.FoundingBandRange.min, SettlementTuning.FoundingBandRange.max);

            CoreMap map = game.EnterSettlement(settlement);
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                Assert.NotNull(map.terrainGrid.TerrainAt(map.cellIndices.IndexToCell(i)));
            }

            // The founding band is Full-tier data (spec §11.3) but is never itself spawned onto the interior
            // map by SettlementFounder — a settlement's citizens live off-map until something places them
            // there. Prove the map/pawn-generation/needs/AI seam directly the way FullStackTests does, on the
            // very map Game.EnterSettlement just handed back.
            var band = new List<Pawn>();
            IntVec3 origin = FirstWalkable(map);
            for (int i = 0; i < 4; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist));
                GenSpawn.Spawn(pawn, OffsetWalkable(map, origin, i + 1), map);
                band.Add(pawn);
            }

            for (int i = 0; i < 2000; i++) game.TickManager.DoSingleTick();

            Assert.All(band, p => Assert.False(p.Dead, "a pawn died during an ordinary 2,000-tick run"));
            Assert.Contains(band, p => p.jobs?.curJob != null);
        }

        [Fact]
        public void Scribe_round_trip_of_a_whole_game_preserves_world_settlement_population_and_time_and_keeps_ticking()
        {
            Game game = NewSoloGame("game-lifecycle-scribe");

            Settlement settlementBefore = game.World!.worldObjects.OfType<Settlement>().First();
            int populationBefore = settlementBefore.TotalPopulation;
            string settlementName = settlementBefore.name;
            int tile = settlementBefore.tile;

            var quest = new Quest { id = 99, appearanceTick = game.TickManager.TicksGame, ticksUntilAcceptanceExpiry = 100_000 };
            game.QuestManager.Add(quest);

            for (int i = 0; i < 3000; i++) game.TickManager.DoSingleTick();

            int ticksBefore = game.TickManager.TicksGame;
            int chronicleCountBefore = game.Storyteller.Chronicle.Count;
            int letterCountBefore = game.LetterStack.LettersListForReading.Count;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            // Loading must hand Find back to the loaded game, not leave the game that saved it current.
            Assert.Same(loaded, Find.CurrentGame);
            Assert.NotSame(game, loaded);

            Assert.Equal(ticksBefore, loaded.TickManager.TicksGame);
            Assert.Equal(chronicleCountBefore, loaded.Storyteller.Chronicle.Count);
            Assert.Equal(letterCountBefore, loaded.LetterStack.LettersListForReading.Count);

            Settlement settlementAfter = loaded.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(settlementName, settlementAfter.name);
            Assert.Equal(tile, settlementAfter.tile);
            Assert.Equal(populationBefore, settlementAfter.TotalPopulation);

            Quest loadedQuest = Assert.Single(loaded.QuestManager.QuestsListForReading);
            Assert.Equal(QuestState.NotYetAccepted, loadedQuest.State);

            // The tick order must have been rebuilt (PreTickers/PostTickers can't themselves be Scribed), not
            // merely the data underneath it.
            Assert.Single(loaded.TickManager.PreTickers);
            Assert.Equal(9, loaded.TickManager.PostTickers.Count);

            // And the loaded game must actually keep running: tick it as far past the load as it ran before
            // the save, and confirm nothing throws and time keeps moving forward.
            for (int i = 0; i < 3000; i++) loaded.TickManager.DoSingleTick();
            Assert.Equal(ticksBefore + 3000, loaded.TickManager.TicksGame);
        }

        [Fact]
        public void Scribe_round_trip_of_an_entered_settlement_keeps_its_spawned_pawn_ticking_after_load()
        {
            Game game = NewSoloGame("game-lifecycle-scribe-map");
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            CoreMap map = game.EnterSettlement(settlement);

            Pawn extra = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist));
            GenSpawn.Spawn(extra, FirstWalkable(map), map);
            Need_Food? hunger = extra.needs.food;
            Assert.NotNull(hunger);
            hunger!.CurLevel = 1f;

            for (int i = 0; i < 2000; i++) game.TickManager.DoSingleTick();
            float foodBeforeSave = hunger.CurLevel;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement loadedSettlement = loaded.World!.worldObjects.OfType<Settlement>().First();
            Assert.NotNull(loadedSettlement.InteriorMap);
            CoreMap loadedMap = loadedSettlement.InteriorMap!;

            Pawn loadedPawn = Assert.Single(loadedMap.mapPawns.AllPawns);
            Assert.Equal(foodBeforeSave, loadedPawn.needs.food!.CurLevel, 4);

            // The strong claim: the loaded pawn is really back in TickManager's normal tick list, not merely
            // present as saved data — its need keeps falling once the loaded game keeps ticking. This is the
            // ordering Game.ExposeData depends on: Find.CurrentGame must already point at the loading Game,
            // with its TickManager already loaded, by the time Map.FinalizeLoading calls SpawnSetup on this
            // very pawn during PostLoadInit.
            for (int i = 0; i < 2000; i++) loaded.TickManager.DoSingleTick();
            Assert.True(loadedPawn.needs.food!.CurLevel < foodBeforeSave, "a respawned pawn's hunger never fell after load — it isn't really ticking");
        }

        private static IntVec3 FirstWalkable(CoreMap map)
        {
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                if (map.pathGrid.Walkable(cell)) return cell;
            }
            return IntVec3.Zero;
        }

        private static IntVec3 OffsetWalkable(CoreMap map, IntVec3 origin, int step)
        {
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell((map.cellIndices.CellToIndex(origin) + step * 7 + i)
                    % map.cellIndices.NumGridCells);
                if (map.pathGrid.Walkable(cell)) return cell;
            }
            return origin;
        }
    }
}
