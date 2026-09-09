using System.Collections.Generic;
using System.Linq;

using SimWorld.Director;
using SimWorld.Quests;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreScenario = SimWorld.Scenario.Scenario;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Sim
{
    /// <summary>
    /// <see cref="Game"/>: <c>Find</c> resolving through the current game (and falling back exactly as
    /// before when there is none), the tick order <see cref="Game.NewGame"/> wires, and the quest/autosave
    /// hooks that had never been driven by anything before this module.
    /// </summary>
    public class GameTests : ContentTestBase
    {
        public GameTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreScenario TribalStart() => ScenarioDefOf.TribalStart.scenario;

        private static Game NewSoloGame(string seed = "game-tests-seed", int subdivision = 3) =>
            Game.NewGame(TribalStart(), seed, subdivisionOverride: subdivision, soloStart: true);

        // ---- Find <-> Game ----

        [Fact]
        public void Find_behaves_exactly_as_before_when_no_game_is_current()
        {
            Assert.Null(Find.CurrentGame);

            var tm = new TickManager();
            Find.TickManager = tm;
            Assert.Same(tm, Find.TickManager);

            var storyteller = new global::SimWorld.Director.Storyteller();
            Find.Storyteller = storyteller;
            Assert.Same(storyteller, Find.Storyteller);

            // Reset() must still drop every thread-static service, exactly as it always has.
            Find.Reset();
            Assert.Null(Find.CurrentGame);
            Assert.NotSame(tm, Find.TickManager);
        }

        [Fact]
        public void Find_resolves_through_the_current_game_once_one_exists()
        {
            Game game = NewSoloGame();

            Assert.Same(game, Find.CurrentGame);
            Assert.Same(game.TickManager, Find.TickManager);
            Assert.Same(game.ResearchManager, Find.ResearchManager);
            Assert.Same(game.Storyteller, Find.Storyteller);
            Assert.Same(game.FactionManager, Find.FactionManager);
            Assert.Same(game.LetterStack, Find.LetterStack);
            Assert.Same(game.QuestManager, Find.QuestManager);
            Assert.Same(game.Scenario, Find.Scenario);
            Assert.Same(game.FamilyManager, Find.FamilyManager);
            Assert.Same(game.God, Find.God);
            Assert.Same(game.World, Find.World);
        }

        [Fact]
        public void Find_setters_write_through_to_the_current_game_instead_of_the_thread_static_fallback()
        {
            Game game = NewSoloGame();

            var freshTickManager = new TickManager();
            Find.TickManager = freshTickManager;
            Assert.Same(freshTickManager, game.TickManager);

            var freshQuestManager = new QuestManager();
            Find.QuestManager = freshQuestManager;
            Assert.Same(freshQuestManager, game.QuestManager);
        }

        [Fact]
        public void Reset_drops_the_current_game_and_every_test_wiring_pattern_keeps_working()
        {
            NewSoloGame();
            Assert.NotNull(Find.CurrentGame);

            Find.Reset();
            Assert.Null(Find.CurrentGame);

            // The exact pattern every other test in this suite relies on (ContentTestBase's own
            // constructor): wire a bare TickManager by hand with no Game anywhere in sight.
            Find.TickManager = new TickManager();
            Assert.Null(Find.CurrentGame);
        }

        // ---- NewGame ----

        [Fact]
        public void NewGame_generates_a_world_and_founds_a_settlement_within_the_spec_band_range()
        {
            Game game = NewSoloGame();

            Assert.NotNull(game.World);
            CoreWorld world = game.World!;
            List<global::SimWorld.World.Settlement> settlements = world.worldObjects.OfType<global::SimWorld.World.Settlement>().ToList();
            Assert.Single(settlements);

            global::SimWorld.World.Settlement settlement = settlements[0];
            Assert.InRange(settlement.TotalPopulation, global::SimWorld.World.SettlementTuning.FoundingBandRange.min, global::SimWorld.World.SettlementTuning.FoundingBandRange.max);
            Assert.All(settlement.Citizens, p => Assert.Equal(global::SimWorld.Pawns.PawnTier.Full, p.tier.Tier));
            Assert.Contains(game.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Founding:"));
        }

        [Fact]
        public void NewGame_runs_PostGameStart_against_the_actual_founding_population()
        {
            // TribalStart carries ScenPart_StartingEra(SticksAndStones), ScenPart_GameStartDialog and
            // ScenPart_StartingThing_Defined(MeleeWeapon_Knife x2) — all three should land somewhere real.
            Game game = NewSoloGame();

            global::SimWorld.Research.EraDef sticksAndStones = global::SimWorld.Defs.DefDatabase<global::SimWorld.Research.EraDef>.GetNamed("SticksAndStones");
            Assert.Equal(sticksAndStones.techLevel, game.ResearchManager.ResearcherTechLevel);

            Assert.NotEmpty(game.LetterStack.LettersListForReading);

            global::SimWorld.World.Settlement settlement = game.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();
            global::SimWorld.Defs.ThingDef knife = global::SimWorld.Defs.DefDatabase<global::SimWorld.Defs.ThingDef>.GetNamed("MeleeWeapon_Knife");
            Assert.Equal(2, settlement.StoreCountOf(knife));
        }

        [Fact]
        public void NewGame_wires_a_pre_and_post_tick_order()
        {
            Game game = NewSoloGame();

            Assert.Single(game.TickManager.PreTickers);
            Assert.Equal(11, game.TickManager.PostTickers.Count);
        }

        [Fact]
        public void Same_seed_founds_the_same_size_settlement_on_the_same_tile()
        {
            Game a = NewSoloGame("same-seed-game");
            Game b = NewSoloGame("same-seed-game");

            global::SimWorld.World.Settlement sa = a.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();
            global::SimWorld.World.Settlement sb = b.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();

            Assert.Equal(sa.tile, sb.tile);
            Assert.Equal(sa.TotalPopulation, sb.TotalPopulation);
            Assert.Equal(sa.name, sb.name);
        }

        // ---- ticking ----

        [Fact]
        public void Ticking_the_game_advances_time_and_every_wired_manager_keeps_running()
        {
            Game game = NewSoloGame();

            // Long enough to cross the storyteller (1000), god (2000) and social/faction (2500) cadences at
            // least once each without paying for a real year of demography (3,600,000 ticks).
            for (int i = 0; i < 5000; i++) game.TickManager.DoSingleTick();

            Assert.Equal(5000, game.TickManager.TicksGame);
        }

        [Fact]
        public void Autosave_fires_once_per_interval_and_hands_back_this_game()
        {
            Game game = NewSoloGame();
            game.AutosaveIntervalTicks = 100;

            var fired = new List<Game>();
            game.AutosaveDue += g => fired.Add(g);

            for (int i = 0; i < 250; i++) game.TickManager.DoSingleTick();

            Assert.Equal(2, fired.Count);
            Assert.All(fired, g => Assert.Same(game, g));
        }

        [Fact]
        public void Quest_tick_loop_expires_an_unaccepted_offer_once_the_game_ticks_past_it()
        {
            Game game = NewSoloGame();

            var quest = new Quest
            {
                id = 1,
                appearanceTick = game.TickManager.TicksGame,
                ticksUntilAcceptanceExpiry = 500,
            };
            game.QuestManager.Add(quest);

            for (int i = 0; i < 499; i++) game.TickManager.DoSingleTick();
            Assert.Equal(QuestState.NotYetAccepted, quest.State);

            game.TickManager.DoSingleTick();
            Assert.Equal(QuestState.EndedOfferExpired, quest.State);
        }

        [Fact]
        public void EnterSettlement_generates_an_interior_map_that_the_game_then_ticks()
        {
            Game game = NewSoloGame();
            global::SimWorld.World.Settlement settlement = game.World!.worldObjects.OfType<global::SimWorld.World.Settlement>().First();

            Assert.Empty(game.Maps);

            CoreMap map = game.EnterSettlement(settlement);

            Assert.Same(map, Assert.Single(game.Maps));
            Assert.Same(map, settlement.EnterMap(game.World!));

            // Doesn't throw with a live map wired into the post-tick sweep.
            for (int i = 0; i < 50; i++) game.TickManager.DoSingleTick();
        }
    }
}
