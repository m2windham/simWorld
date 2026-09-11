using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Conditions
{
    /// <summary>
    /// Game conditions (system: conditions — RimWorld's <c>GameCondition</c> / <c>GameConditionManager</c> /
    /// <c>GameConditionDef</c>), and the three incidents that were named in content and did nothing.
    ///
    /// <para/><b>What this suite exists to stop happening again.</b> <c>HeatWave</c>, <c>ColdSnap</c> and
    /// <c>Flashstorm</c> shipped as <c>IncidentDef</c>s wired to <c>IncidentWorker_Placeholder</c>. They were
    /// in the storyteller's repertoire, they could be selected, they fired, they returned true, and the
    /// chronicle recorded them. Measured on exactly the fixture
    /// <see cref="The_three_incidents_used_to_fire_and_change_nothing"/> builds — a map on a real world tile,
    /// covered in wooden walls — firing all three produced: zero conditions in force before and after, zero
    /// fires on the map before and after, and an outdoor temperature that stayed, to three decimal places,
    /// exactly the season-plus-hour-plus-weather number it would have been had nothing fired at all. Three
    /// chronicle lines said a heat wave, a cold snap and a flashstorm had happened. Nothing else moved.
    ///
    /// <para/>Everything tuned here is pinned as a band, a trend or a round trip. The two temperature offsets
    /// and the flashstorm's strike cadence are RimWorld's shapes recalled rather than decompiled (their defs
    /// and <see cref="GameCondition_Flashstorm"/> say so), so what is asserted is what they <i>do</i> — a
    /// body that could not rot at all starts going off, a body that was going off freezes, a storm sets
    /// something alight — never the literals that produce it.
    /// </summary>
    public class GameConditionTests : ContentTestBase
    {
        public GameConditionTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
        }

        // ---- fixtures ----

        /// <summary>A civilization for conditions to stand over. Bare on purpose: nothing here needs a tile
        /// grid, and <see cref="GameConditionManager"/> is a field initializer, so an ungenerated world is
        /// still a perfectly good home for a condition.</summary>
        private static CoreWorld NewWorld()
        {
            var world = new CoreWorld();
            Find.World = world;
            return world;
        }

        private static CoreMap NewMap(int size = 12) => new CoreMap(size, size, TerrainDefOf.Soil);

        /// <summary>
        /// A map that knows where on the planet it is, which is what makes its outdoor temperature a real
        /// function of season, hour and offsets rather than a number its owner set
        /// (<see cref="WeatherManager.Climate"/>). Equatorial and on the prime meridian, so the only things
        /// moving the thermometer are the daily sun cycle (±7 °C), a small seasonal one (±3 °C at the
        /// equator), the weather's own few degrees, and whatever conditions are in force — a swing every
        /// fixture below is chosen against.
        /// </summary>
        private static CoreMap MapWithClimate(float averageTemperature, int size = 10)
        {
            CoreMap map = NewMap(size);
            map.weatherManager.Climate = new MapClimate
            {
                biome = null,
                averageTemperature = averageTemperature,
                rainfall = 1000f,
                latitude = 0f,
                longitude = 0f,
            };
            return map;
        }

        /// <summary>One tick of a running game, in the order <c>Sim.Game.WireTickHooks</c> wires it: the
        /// clock, then the world (a pre-ticker — which is where a civilization-scale condition advances its
        /// own clock), then every open map (a post-ticker).</summary>
        private static void RunGameTicks(int ticks, params CoreMap[] maps)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                Find.World?.gameConditionManager.GameConditionManagerTick();
                for (int m = 0; m < maps.Length; m++) maps[m].MapTick();
            }
        }

        /// <summary>What this map's outdoor temperature would be with no condition in force: the weather
        /// module's own model, at this tick, with only the weather's offset in it. The difference between
        /// this and <c>Map.outdoorTemperature</c> is exactly what the conditions are contributing.</summary>
        private static float TemperatureWithoutConditions(CoreMap map) =>
            GenTemperature.OutdoorTemperatureAt(
                map.weatherManager.Climate!, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Corpse BodyOnTheGround(CoreMap map, IntVec3 cell)
        {
            var husky = new Pawn(Husky, "Body");
            GenSpawn.Spawn(husky, cell, map);
            husky.health.Kill(null, null);
            Assert.NotNull(husky.corpse);
            return husky.corpse!;
        }

        /// <summary>A field of fuel: every cell gets a wall, so a strike landing anywhere has something to
        /// catch. Spawned the ordinary way, so what burns is an ordinary flammable Thing.</summary>
        private static void CoverInFuel(CoreMap map)
        {
            ThingDef wall = Def("Wall");
            foreach (IntVec3 cell in map.AllCells)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(wall), cell, map);
            }
        }

        private static int FireCount(CoreMap map) => FireUtility.AllFires(map).Count;

        /// <summary>Fires the incident the way the storyteller does — through the worker on its own def,
        /// against the civilization.</summary>
        private static bool FireIncident(IncidentDef incident, CivilizationTarget target) =>
            incident.Worker.TryExecute(new IncidentParms { target = target, forced = true });

        // ---- content ----

        [Fact]
        public void Content_loads_and_the_three_incidents_no_longer_use_the_placeholder()
        {
            Assert.Empty(Content.Result.Errors);

            Assert.NotNull(GameConditionDefOf.HeatWave);
            Assert.NotNull(GameConditionDefOf.ColdSnap);
            Assert.NotNull(GameConditionDefOf.Flashstorm);

            foreach (IncidentDef incident in new[]
            {
                ConditionIncidentDefOf.HeatWave, ConditionIncidentDefOf.ColdSnap, ConditionIncidentDefOf.Flashstorm,
            })
            {
                Assert.False(
                    typeof(IncidentWorker_Placeholder).IsAssignableFrom(incident.workerClass),
                    incident.defName + " is still wired to the placeholder worker.");
                Assert.True(
                    typeof(IncidentWorker_MakeGameCondition).IsAssignableFrom(incident.workerClass),
                    incident.defName + " does not make a game condition.");
            }

            // A heat wave that warms and a cold snap that chills: the direction, never the magnitude.
            Assert.True(GameConditionDefOf.HeatWave.temperatureOffset > 0f);
            Assert.True(GameConditionDefOf.ColdSnap.temperatureOffset < 0f);
            foreach (GameConditionDef condition in DefDatabase<GameConditionDef>.AllDefs)
            {
                Assert.True(condition.durationDays.min > 0f, condition.defName + " can be made with no duration.");
            }
        }

        /// <summary>
        /// The inverted link works from both ends, and only once: every condition that names an incident is
        /// the <i>only</i> condition naming it, and each of the three incidents finds exactly one. Two
        /// conditions claiming one incident is the single failure mode this inversion has (see
        /// <see cref="GameConditionDef"/>), and it would otherwise be silent — the second one would simply
        /// never happen.
        /// </summary>
        [Fact]
        public void Every_incident_maps_to_exactly_one_condition()
        {
            List<GameConditionDef> withIncidents = DefDatabase<GameConditionDef>.AllDefs
                .Where(c => c.incident != null)
                .ToList();
            Assert.NotEmpty(withIncidents);

            List<string> duplicated = withIncidents
                .GroupBy(c => c.incident!.defName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.Empty(duplicated);

            Assert.Same(GameConditionDefOf.HeatWave, IncidentWorker_MakeGameCondition.ConditionFor(ConditionIncidentDefOf.HeatWave));
            Assert.Same(GameConditionDefOf.ColdSnap, IncidentWorker_MakeGameCondition.ConditionFor(ConditionIncidentDefOf.ColdSnap));
            Assert.Same(GameConditionDefOf.Flashstorm, IncidentWorker_MakeGameCondition.ConditionFor(ConditionIncidentDefOf.Flashstorm));
        }

        // ---- the before/after ----

        /// <summary>
        /// The measurement the class doc quotes, run both ways on one fixture so the two columns are
        /// comparable rather than remembered. The placeholder half is the behaviour that shipped: three
        /// incidents fire, all three report success, and nothing whatsoever is different afterwards.
        /// </summary>
        [Fact]
        public void The_three_incidents_used_to_fire_and_change_nothing()
        {
            NewWorld();
            CoreMap map = MapWithClimate(averageTemperature: 20f);
            CoverInFuel(map);
            var target = new CivilizationTarget();

            RunGameTicks(1, map);
            Assert.Equal(0, FireCount(map));

            // --- what shipped: the same three incidents on IncidentWorker_Placeholder ---
            foreach (string name in new[] { "HeatWave", "ColdSnap", "Flashstorm" })
            {
                var asShipped = new IncidentDef
                {
                    defName = name + "_AsShipped",
                    category = IncidentCategoryDefOf.Misc,
                    workerClass = typeof(IncidentWorker_Placeholder),
                };
                Assert.True(
                    asShipped.Worker.TryExecute(new IncidentParms { target = target, forced = true }),
                    "The placeholder always reported success — that is the whole problem.");
            }
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3, map);

            Assert.Empty(Find.World!.gameConditionManager.ActiveConditions);
            Assert.Equal(0, FireCount(map));
            Assert.Equal(0f, map.gameConditionManager.AggregateTemperatureOffset());
            Assert.Equal(TemperatureWithoutConditions(map), map.outdoorTemperature, 3);

            // --- what happens now ---
            Assert.True(FireIncident(ConditionIncidentDefOf.HeatWave, target));
            Assert.True(FireIncident(ConditionIncidentDefOf.Flashstorm, target));
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3, map);

            Assert.Equal(2, Find.World!.gameConditionManager.ActiveConditions.Count);
            Assert.Equal(
                GameConditionDefOf.HeatWave.temperatureOffset,
                map.outdoorTemperature - TemperatureWithoutConditions(map),
                2);
            Assert.True(FireCount(map) > 0, "Lightning fell on a field of walls and nothing caught.");
        }

        // ---- duration ----

        [Fact]
        public void A_condition_expires_on_schedule_and_not_before()
        {
            CoreWorld world = NewWorld();
            GameCondition condition = GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 2f);
            world.gameConditionManager.RegisterCondition(condition);

            int duration = condition.duration;
            Assert.True(duration > 0);
            Assert.False(condition.Permanent);

            RunGameTicks(duration - 1);
            Assert.Single(world.gameConditionManager.ActiveConditions);
            Assert.Equal(1, condition.TicksLeft);

            RunGameTicks(1);
            Assert.Empty(world.gameConditionManager.ActiveConditions);
            Assert.True(condition.Expired);
        }

        [Fact]
        public void A_permanent_condition_never_expires()
        {
            CoreWorld world = NewWorld();
            world.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeCondition(GameConditionDefOf.ColdSnap, GameCondition.PermanentDuration));

            RunGameTicks(GenDate.TicksPerDay * 5);

            Assert.Single(world.gameConditionManager.ActiveConditions);
            Assert.True(world.gameConditionManager.ActiveConditions[0].Permanent);
        }

        /// <summary>A condition already in force is not started a second time — otherwise a storyteller that
        /// picked the same incident twice in a quadrum would stack two heat waves into one unsurvivable
        /// one.</summary>
        [Fact]
        public void A_condition_does_not_stack_with_itself()
        {
            NewWorld();
            var target = new CivilizationTarget();

            Assert.True(FireIncident(ConditionIncidentDefOf.HeatWave, target));
            Assert.False(ConditionIncidentDefOf.HeatWave.Worker.CanFireNow(new IncidentParms { target = target, forced = true }));
            Assert.False(FireIncident(ConditionIncidentDefOf.HeatWave, target));

            Assert.Single(Find.World!.gameConditionManager.ActiveConditions);
        }

        /// <summary>Durations come out of the def's own range, through the seeded stream, rather than being
        /// one constant every firing shares.</summary>
        [Fact]
        public void Duration_comes_from_the_defs_own_range()
        {
            var lengths = new HashSet<int>();
            for (int i = 0; i < 25; i++)
            {
                NewWorld();
                Assert.True(FireIncident(ConditionIncidentDefOf.ColdSnap, new CivilizationTarget()));
                GameCondition condition = Find.World!.gameConditionManager.ActiveConditions[0];
                lengths.Add(condition.duration);

                Assert.InRange(
                    condition.duration / (float)GenDate.TicksPerDay,
                    GameConditionDefOf.ColdSnap.durationDays.min,
                    GameConditionDefOf.ColdSnap.durationDays.max);
            }
            Assert.True(lengths.Count > 1, "Every cold snap was exactly the same length.");
        }

        // ---- scope ----

        /// <summary>
        /// The scope decision, asserted rather than only written down: an incident registers at civilization
        /// scale, and a map feels it through its own manager's parent link — including a map opened after the
        /// condition started, which is the case that would have been dormant had conditions lived on maps.
        /// </summary>
        [Fact]
        public void An_incident_registers_at_civilization_scale_and_reaches_maps_opened_afterwards()
        {
            NewWorld();
            var target = new CivilizationTarget();
            Assert.True(FireIncident(ConditionIncidentDefOf.ColdSnap, target));

            Assert.Single(Find.World!.gameConditionManager.ActiveConditions);

            CoreMap openedLater = NewMap();
            Assert.Empty(openedLater.gameConditionManager.ActiveConditions);
            Assert.True(openedLater.gameConditionManager.ConditionIsActive(GameConditionDefOf.ColdSnap));
            Assert.Equal(
                GameConditionDefOf.ColdSnap.temperatureOffset,
                openedLater.gameConditionManager.AggregateTemperatureOffset(),
                3);
        }

        /// <summary>Offsets add up across the two scopes rather than one shadowing the other.</summary>
        [Fact]
        public void Temperature_offsets_aggregate_across_the_map_and_the_world()
        {
            CoreWorld world = NewWorld();
            CoreMap map = NewMap();

            world.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 1f));
            map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.ColdSnap, 1f));

            float expected = GameConditionDefOf.HeatWave.temperatureOffset + GameConditionDefOf.ColdSnap.temperatureOffset;
            Assert.Equal(expected, map.gameConditionManager.AggregateTemperatureOffset(), 3);

            // The world only ever sees its own: a condition over one settlement is not a condition over the
            // civilization.
            Assert.Equal(
                GameConditionDefOf.HeatWave.temperatureOffset,
                world.gameConditionManager.AggregateTemperatureOffset(),
                3);
        }

        // ---- what the temperature conditions actually do ----

        /// <summary>
        /// The end-to-end heat wave. Nothing here sets <c>outdoorTemperature</c> or calls <c>RotUtility</c> to
        /// make its point: a body lies on a map cold enough that rot cannot run at all, the incident fires,
        /// and the body starts going off.
        /// <para/>
        /// The fixture's −12 °C annual mean at the equator cannot reach freezing on its own — the daily sun
        /// cycle (±7), the seasonal swing (±3) and the coldest weather in content (−3) together leave it at
        /// −2 °C at the very warmest — so the control day below is genuinely a day on which nothing could
        /// have rotted for any other reason.
        /// </summary>
        [Fact]
        public void A_heat_wave_raises_the_temperature_a_citizen_and_a_rotting_body_read()
        {
            NewWorld();
            CoreMap map = MapWithClimate(averageTemperature: -12f);

            // One map tick before the body exists. Map.outdoorTemperature starts at its own 21 °C default and
            // only becomes the climate's number once the map has ticked once; a corpse spawned before that can
            // catch a single rare tick at room temperature, which is a property of the fixture rather than of
            // the weather and would otherwise be mistaken for rot the cold allowed.
            RunGameTicks(1, map);
            Corpse body = BodyOnTheGround(map, new IntVec3(4, 0, 4));

            RunGameTicks(GenDate.TicksPerDay, map);
            Assert.True(map.outdoorTemperature < 0f);
            Assert.Equal(0f, body.RotComp!.RotProgress);

            Assert.True(FireIncident(ConditionIncidentDefOf.HeatWave, new CivilizationTarget()));
            RunGameTicks(1, map);

            // The thermometer moved by exactly the condition's offset, and by nothing else.
            Assert.Equal(
                GameConditionDefOf.HeatWave.temperatureOffset,
                map.outdoorTemperature - TemperatureWithoutConditions(map),
                2);

            // And it is the same number a citizen standing there reads, because that is the field the room
            // and the rot pipeline both go through.
            Assert.Equal(map.outdoorTemperature, RotUtility.AmbientTemperatureAt(map, new IntVec3(4, 0, 4)), 3);

            RunGameTicks(GenDate.TicksPerDay, map);
            Assert.True(body.RotComp!.RotProgress > 0f,
                "A body that could not rot at all started rotting once the heat wave arrived.");
        }

        /// <summary>
        /// The mirror. A body that is going off freezes, and the growing season ends with it — the growth
        /// factor sampled here is the very number <c>Building.Plant.TickLong</c> reads from
        /// <c>Map.outdoorTemperature</c>, sampled rather than grown because plant growth is also gated on
        /// daylight and a one-day window would be measuring the hour rather than the cold.
        /// <para/>
        /// 5 °C at the equator crosses freezing in both directions over a day, which is what makes the
        /// control day rot; −20 on top of it cannot reach freezing from below however warm the hour
        /// (5 − 20 + 7 + 3 = −5), which is what makes the cold day stop dead.
        /// </summary>
        [Fact]
        public void A_cold_snap_stops_rot_dead_and_takes_the_growing_season_with_it()
        {
            NewWorld();
            CoreMap map = MapWithClimate(averageTemperature: 5f);
            RunGameTicks(1, map);
            Corpse body = BodyOnTheGround(map, new IntVec3(4, 0, 4));

            float growingBefore = 0f;
            for (int i = 0; i < GenDate.TicksPerDay; i++)
            {
                RunGameTicks(1, map);
                growingBefore = Math.Max(growingBefore, PlantUtility.GrowthRateFactor_Temperature(map.outdoorTemperature));
            }

            float rottedBefore = body.RotComp!.RotProgress;
            Assert.True(rottedBefore > 0f, "The fixture is meant to start with a body that is going off.");
            Assert.True(growingBefore > 0f, "The fixture is meant to start with a growing season.");

            Assert.True(FireIncident(ConditionIncidentDefOf.ColdSnap, new CivilizationTarget()));

            float growingAfter = 0f;
            for (int i = 0; i < GenDate.TicksPerDay; i++)
            {
                RunGameTicks(1, map);
                growingAfter = Math.Max(growingAfter, PlantUtility.GrowthRateFactor_Temperature(map.outdoorTemperature));
                Assert.True(map.outdoorTemperature < 0f, "The cold snap is meant to hold the map below freezing all day.");
            }

            Assert.Equal(rottedBefore, body.RotComp!.RotProgress);
            Assert.Equal(0f, growingAfter);
        }

        // ---- what the flashstorm actually does ----

        /// <summary>
        /// The end-to-end flashstorm, fired as the incident and resolved on an open map. Fuel is placed the
        /// ordinary way and every fire that appears is an ordinary <c>Things.Fire</c> found through
        /// <c>FireUtility.AllFires</c> — nothing here spawns a fire or calls the ignition path directly, so a
        /// fire on this map can only have come from a strike.
        /// </summary>
        [Fact]
        public void A_flashstorm_sets_something_alight_through_the_real_fire_path()
        {
            NewWorld();
            CoreMap map = NewMap(10);
            CoverInFuel(map);

            Assert.Equal(0, FireCount(map));
            Assert.True(FireIncident(ConditionIncidentDefOf.Flashstorm, new CivilizationTarget()));

            var storm = (GameCondition_Flashstorm)Find.World!.gameConditionManager.ActiveConditions[0];
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3, map);

            Assert.True(storm.StrikeCount > 0, "A storm that never struck proves nothing.");
            Assert.True(FireCount(map) > 0, "Lightning fell on a field of walls and nothing caught.");
            Assert.All(FireUtility.AllFires(map), f => Assert.IsType<Fire>(f));
        }

        /// <summary>A roof keeps the lightning out: the same storm over the same fuel, fully roofed, starts
        /// nothing. This is also what stops a storm setting fire to the inside of a building.</summary>
        [Fact]
        public void A_flashstorm_does_not_strike_through_a_roof()
        {
            NewWorld();
            CoreMap map = NewMap(10);
            CoverInFuel(map);
            foreach (IntVec3 cell in map.AllCells) map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

            Assert.True(FireIncident(ConditionIncidentDefOf.Flashstorm, new CivilizationTarget()));

            var storm = (GameCondition_Flashstorm)Find.World!.gameConditionManager.ActiveConditions[0];
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3, map);

            Assert.Equal(0, storm.StrikeCount);
            Assert.Equal(0, FireCount(map));
        }

        /// <summary>A flashstorm holds the rain off while it burns — the pairing
        /// <c>Weather.WeatherDecider.DisableRainFor</c> was written for and had no caller for until this
        /// module.</summary>
        [Fact]
        public void A_flashstorm_holds_the_rain_off_while_it_burns()
        {
            NewWorld();
            CoreMap map = NewMap(10);
            CoverInFuel(map);
            map.weatherManager.TransitionTo(WeatherDefOf.Rain);
            map.weatherManager.TransitionTo(WeatherDefOf.Rain);
            Assert.True(map.weatherManager.RainRate > 0f);

            Assert.True(FireIncident(ConditionIncidentDefOf.Flashstorm, new CivilizationTarget()));
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max + 1, map);

            Assert.True(map.weatherManager.Decider.RainDisabled);
            Assert.Equal(0f, map.weatherManager.CurWeather!.rainRate);
        }

        // ---- determinism ----

        /// <summary>Two runs of one seed produce the same storm: the same number of strikes, landing in the
        /// same cells. Everything a condition rolls goes through the seeded stream.</summary>
        [Fact]
        public void The_same_seed_gives_the_same_storm()
        {
            string RunOnce(int seed)
            {
                Find.Reset();
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                NewWorld();
                CoreMap map = NewMap(10);
                CoverInFuel(map);
                Assert.True(FireIncident(ConditionIncidentDefOf.Flashstorm, new CivilizationTarget()));

                var storm = (GameCondition_Flashstorm)Find.World!.gameConditionManager.ActiveConditions[0];
                RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 3, map);

                Assert.True(storm.StrikeCount > 0, "A storm that never struck proves nothing about determinism.");
                Assert.True(FireUtility.AllFires(map).Count > 0);

                IEnumerable<string> where = FireUtility.AllFires(map)
                    .Select(f => f.Position.ToString())
                    .OrderBy(cell => cell, StringComparer.Ordinal);
                return storm.StrikeCount + ":" + string.Join(",", where);
            }

            Assert.Equal(RunOnce(4242), RunOnce(4242));
        }

        // ---- scribe ----

        /// <summary>
        /// A save taken with conditions in force reloads with them still in force, still counting down, and
        /// still striking — not restarted, and not lost. A condition the save forgets is a condition that did
        /// nothing, which is the failure this whole module is about.
        /// </summary>
        [Fact]
        public void A_condition_round_trips_through_scribe_and_carries_on()
        {
            NewWorld();
            CoreMap map = NewMap(10);
            CoverInFuel(map);
            map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.Flashstorm, 1f));
            map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 1f));

            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 2, map);

            int ticksLeftBefore = map.gameConditionManager.ActiveConditions[0].TicksLeft;
            float offsetBefore = map.gameConditionManager.AggregateTemperatureOffset();
            int strikesBefore = ((GameCondition_Flashstorm)map.gameConditionManager.ActiveConditions[0]).StrikeCount;
            Assert.True(strikesBefore > 0);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            // Held by reference rather than by index: ActiveConditions is the manager's own live list, and
            // these two are about to be removed from it by expiring.
            var after = new List<GameCondition>(loaded.gameConditionManager.ActiveConditions);
            Assert.Equal(2, after.Count);
            var storm = Assert.IsType<GameCondition_Flashstorm>(after[0]);
            Assert.Same(GameConditionDefOf.Flashstorm, storm.def);
            Assert.Same(GameConditionDefOf.HeatWave, after[1].def);
            Assert.Equal(ticksLeftBefore, storm.TicksLeft);
            Assert.Equal(strikesBefore, storm.StrikeCount);
            Assert.Equal(offsetBefore, loaded.gameConditionManager.AggregateTemperatureOffset(), 3);

            // Each condition is linked back to the manager that loaded it, so it can still find its scope.
            Assert.All(after, c => Assert.Same(loaded.gameConditionManager, c.manager));

            // And it carries on rather than restarting: it still strikes, and it still ends on the tick it
            // was always going to end on.
            RunGameTicks(GameCondition_Flashstorm.TicksBetweenStrikes.max * 2, loaded);
            Assert.True(storm.StrikeCount > strikesBefore);

            RunGameTicks(ticksLeftBefore, loaded);
            Assert.Empty(loaded.gameConditionManager.ActiveConditions);
            Assert.Equal(0f, loaded.gameConditionManager.AggregateTemperatureOffset());
        }

        // ---- the god layer ----

        /// <summary>
        /// A civilization-scale condition is visible to a civilization-scale view, and stops being visible
        /// when it ends. See <see cref="ConditionLine"/> for why this widens <c>God/View</c> where the weather
        /// module deliberately did not.
        /// </summary>
        [Fact]
        public void Conditions_reach_the_god_view_and_leave_it_when_they_end()
        {
            NewWorld();
            Assert.Empty(GodViewSnapshot.Capture().Conditions);

            Assert.True(FireIncident(ConditionIncidentDefOf.HeatWave, new CivilizationTarget()));

            GodViewSnapshot during = GodViewSnapshot.Capture();
            ConditionLine line = Assert.Single(during.Conditions);
            Assert.Equal(GameConditionDefOf.HeatWave.defName, line.DefName);
            Assert.False(line.Permanent);
            Assert.True(line.TicksLeft > 0);
            Assert.Equal(GameConditionDefOf.HeatWave.temperatureOffset, line.TemperatureOffset, 3);

            RunGameTicks(line.TicksLeft);
            Assert.Empty(GodViewSnapshot.Capture().Conditions);
        }

        /// <summary>A map-local condition is deliberately not civilization news — see
        /// <see cref="ConditionLine"/>.</summary>
        [Fact]
        public void A_map_local_condition_is_not_reported_as_civilization_news()
        {
            NewWorld();
            CoreMap map = NewMap();
            map.gameConditionManager.RegisterCondition(
                GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 1f));

            Assert.Empty(GodViewSnapshot.Capture().Conditions);
            Assert.True(map.gameConditionManager.ConditionIsActive(GameConditionDefOf.HeatWave));
        }

        // ---- letters ----

        /// <summary>A condition that starts and ends says so, so the player is not left inferring a heat wave
        /// from their crops.</summary>
        [Fact]
        public void Starting_and_ending_a_condition_both_reach_the_letter_stack()
        {
            NewWorld();
            var seen = new List<string>();
            Find.LetterStack.LetterReceived += letter => seen.Add(letter.label);

            Assert.True(FireIncident(ConditionIncidentDefOf.ColdSnap, new CivilizationTarget()));
            Assert.Single(seen);

            RunGameTicks(Find.World!.gameConditionManager.ActiveConditions[0].TicksLeft);
            Assert.Equal(2, seen.Count);
        }

        // ---- gating ----

        /// <summary>Without a world there is no civilization for a condition to stand over, and the incident
        /// says so up front rather than firing into nothing.</summary>
        [Fact]
        public void An_incident_cannot_fire_before_there_is_a_world()
        {
            Find.World = null;
            var target = new CivilizationTarget();

            Assert.False(ConditionIncidentDefOf.HeatWave.Worker.CanFireNow(new IncidentParms { target = target, forced = true }));
            Assert.False(FireIncident(ConditionIncidentDefOf.HeatWave, target));
        }
    }
}
