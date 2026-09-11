using System.Collections.Generic;
using System.Linq;

using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Conditions
{
    /// <summary>
    /// The three incidents through a whole running <c>Sim.Game</c>, with nothing hand-ticked.
    ///
    /// <para/><b>Why this exists on top of <see cref="GameConditionTests"/>.</b> That suite drives the tick
    /// order by hand — clock, then world, then maps — because it is the order
    /// <c>Sim.Game.WireTickHooks</c> declares. A civilization-scale condition depends on that order being
    /// real: it advances its own clock in <c>World.WorldTick</c>, a pre-ticker, and acts on each map from
    /// <c>Map.MapTick</c>, a post-ticker, so a game that ticked them the other way round would resolve every
    /// strike one tick late and, worse, would look fine in a suite that ticked them by hand. Here nothing is
    /// ticked by hand at all: <c>TickManager.DoSingleTick</c> is the only call, and the hooks a real
    /// <c>Game</c> wired are what drive everything else.
    /// </summary>
    public class ConditionsInARealGameTests : ContentTestBase
    {
        public ConditionsInARealGameTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) =>
            game.World!.worldObjects.OfType<Settlement>().First();

        private static bool FireThrough(Game game, IncidentDef incident) =>
            Find.Storyteller.TryFire(new FiringIncident(
                incident, null, new IncidentParms { target = game.CivilizationTarget, forced = true }));

        /// <summary>Scatters fuel on cells that are free and under open sky, so a strike anywhere has a fair
        /// chance of finding something — without walling in a settlement that has people living in it.</summary>
        private static int ScatterFuel(CoreMap map)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            int placed = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (GenGrid.Roofed(cell, map)) continue;
                if (map.thingGrid.ThingsListAt(cell).Count > 0) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(wall), cell, map);
                placed++;
            }
            return placed;
        }

        private static void RunGame(int ticks)
        {
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();
        }

        /// <summary>
        /// One game, one open settlement, two incidents fired through the storyteller, and the whole thing
        /// left to tick itself: the thermometer on the settlement's interior carries the heat wave, lightning
        /// sets that interior alight, the chronicle records both, and the god view can say what is going on.
        /// </summary>
        [Fact]
        public void A_heat_wave_and_a_flashstorm_land_on_an_open_settlement_through_the_games_own_tick_order()
        {
            Game game = NewSoloGame("conditions-end-to-end");
            Settlement settlement = PlayerSettlement(game);
            CoreMap map = game.EnterSettlement(settlement);
            Assert.True(ScatterFuel(map) > 0);

            RunGame(10);
            Assert.NotNull(map.weatherManager.Climate);
            float noConditions = GenTemperature.OutdoorTemperatureAt(
                map.weatherManager.Climate!, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset);
            Assert.Equal(noConditions, map.outdoorTemperature, 3);
            Assert.Empty(FireUtility.AllFires(map));

            Assert.True(FireThrough(game, ConditionIncidentDefOf.HeatWave));
            Assert.True(FireThrough(game, ConditionIncidentDefOf.Flashstorm));

            RunGame(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3);

            // The temperature the settlement's rooms, plants and larders read now carries the heat wave — and
            // it got there without this test touching a map, a weather manager or a condition.
            Assert.Equal(
                GameConditionDefOf.HeatWave.temperatureOffset,
                map.outdoorTemperature - GenTemperature.OutdoorTemperatureAt(
                    map.weatherManager.Climate!, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset),
                2);

            Assert.True(FireUtility.AllFires(map).Count > 0, "The storm ran over an open settlement and burned nothing.");

            IReadOnlyList<ChronicleEntry> chronicle = Find.Storyteller.Chronicle;
            Assert.Contains(chronicle, e => e.incidentDefName == "HeatWave");
            Assert.Contains(chronicle, e => e.incidentDefName == "Flashstorm");

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            Assert.Equal(2, snapshot.Conditions.Count);
            Assert.Contains(snapshot.Conditions, c => c.DefName == "HeatWave" && c.TemperatureOffset > 0f);
            Assert.Contains(snapshot.Conditions, c => c.DefName == "Flashstorm");
        }

        /// <summary>
        /// A condition outlives the settlement being open: it is registered on the civilization, so it keeps
        /// its clock whether or not anybody is looking, and a settlement opened afterwards walks into the
        /// weather it is already having. This is the case that would have been dormant had conditions been
        /// registered on maps — see <see cref="GameConditionManager"/>.
        /// </summary>
        [Fact]
        public void A_condition_started_with_nothing_open_is_waiting_when_a_settlement_is_entered()
        {
            Game game = NewSoloGame("conditions-unwatched");
            Settlement settlement = PlayerSettlement(game);
            Assert.Null(settlement.InteriorMap);

            Assert.True(FireThrough(game, ConditionIncidentDefOf.ColdSnap));
            RunGame(5000);

            GameCondition condition = Assert.Single(game.World!.gameConditionManager.ActiveConditions);
            Assert.True(condition.TicksPassed >= 5000, "The condition's own clock ran with no map in sight.");

            CoreMap map = game.EnterSettlement(settlement);
            RunGame(2);

            Assert.Equal(
                GameConditionDefOf.ColdSnap.temperatureOffset,
                map.gameConditionManager.AggregateTemperatureOffset(),
                3);
            Assert.Equal(
                GameConditionDefOf.ColdSnap.temperatureOffset,
                map.outdoorTemperature - GenTemperature.OutdoorTemperatureAt(
                    map.weatherManager.Climate!, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset),
                2);
        }

        /// <summary>A whole game, conditions included, survives a save and a load.</summary>
        [Fact]
        public void A_game_saved_mid_condition_reloads_still_in_it()
        {
            Game game = NewSoloGame("conditions-save");
            Assert.True(FireThrough(game, ConditionIncidentDefOf.HeatWave));
            RunGame(1000);

            int ticksLeftBefore = game.World!.gameConditionManager.ActiveConditions[0].TicksLeft;

            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            GameCondition after = Assert.Single(loaded.World!.gameConditionManager.ActiveConditions);
            Assert.Same(GameConditionDefOf.HeatWave, after.def);
            Assert.Equal(ticksLeftBefore, after.TicksLeft);
            Assert.Same(loaded.World!.gameConditionManager, after.manager);
        }
    }
}
