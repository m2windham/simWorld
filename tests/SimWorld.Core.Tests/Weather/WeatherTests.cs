using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;
using SimWorld.Weather;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Weather
{
    /// <summary>
    /// Weather (system: weather — RimWorld's <c>Verse.WeatherDef</c>/<c>WeatherManager</c>/<c>WeatherDecider</c>):
    /// what the sky is doing, what it does to the map, and that it does the same thing twice for the same
    /// seed. Every tuned number here is pinned as a band, a trend or a round trip — never as the literal in
    /// the content, which is RimWorld's shape recalled rather than sourced (CLAUDE.md).
    /// </summary>
    public class WeatherTests : ContentTestBase
    {
        public WeatherTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static WeatherDef Weather(string name) => DefDatabase<WeatherDef>.GetNamed(name);

        private static Thing SpawnThing(CoreMap map, IntVec3 cell, string defName)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>One tick of a running game as far as a map is concerned: the clock moves, then the map's
        /// own systems run — which is exactly the order <c>Sim.Game</c> wires up (<c>MapTick</c> is one of
        /// <see cref="TickManager.PostTickers"/>).</summary>
        private static void RunGameTicks(CoreMap map, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }
        }

        /// <summary>
        /// Puts the map in <paramref name="weather"/> at full strength immediately. Transitioning twice makes
        /// both ends of <see cref="WeatherManager.TransitionLerpFactor"/>'s lerp the same weather, so there is
        /// nothing left to fade in — the fade itself is what
        /// <see cref="Weather_fades_in_over_the_transition_rather_than_snapping"/> is for.
        /// </summary>
        private static void SetWeatherHard(CoreMap map, WeatherDef weather)
        {
            map.weatherManager.TransitionTo(weather);
            map.weatherManager.TransitionTo(weather);
        }

        private static MapClimate Climate(string biome, float averageTemperature, float rainfall, float latitude = 45f) =>
            new MapClimate
            {
                biome = DefDatabase<global::SimWorld.World.BiomeDef>.GetNamed(biome),
                averageTemperature = averageTemperature,
                rainfall = rainfall,
                latitude = latitude,
                longitude = 0f,
            };

        // ---- content ----

        [Fact]
        public void Weather_content_loads_and_every_weather_def_is_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(WeatherDefOf.Clear);
            Assert.NotNull(WeatherDefOf.Rain);
            Assert.NotNull(WeatherDefOf.SnowGentle);
            Assert.NotNull(WeatherThoughtDefOf.SoakingWet);
            Assert.True(DefDatabase<WeatherDef>.DefCount >= 5);

            Assert.Equal(0f, WeatherDefOf.Clear.rainRate);
            Assert.True(WeatherDefOf.Rain.rainRate > 0f, "Rain has to actually rain.");
            Assert.True(WeatherDefOf.SnowGentle.snowRate > 0f);

            // Every weather has to be able to happen somewhere, or it is content nobody will ever see.
            foreach (WeatherDef weather in DefDatabase<WeatherDef>.AllDefs)
            {
                Assert.True(weather.durationRange.min > 0, weather.defName + " has no duration.");
                Assert.True(weather.CommonalityIn(null) > 0f, weather.defName + " can never be chosen.");
            }
        }

        [Fact]
        public void A_new_map_starts_clear_and_dry()
        {
            CoreMap map = NewMap();
            Assert.Same(WeatherDefOf.Clear, map.weatherManager.CurWeather);
            Assert.Equal(0f, map.weatherManager.RainRate);
            Assert.Equal(1f, map.weatherManager.CurMoveSpeedMultiplier, 4);
            Assert.Equal(1f, map.weatherManager.CurWeatherAccuracyMultiplier, 4);
        }

        // ---- the lane's own test: rain puts fires out ----

        /// <summary>
        /// The end-to-end one. Fires are lit the ordinary way, the sky is set to rain, and the map is ticked
        /// as a running game ticks it — nothing here calls <c>FireUtility</c> or <c>Fire</c> directly. The
        /// walls are asserted intact afterwards so the only thing that can have removed a fire is the rain:
        /// a fire that ate its fuel would have taken its wall with it.
        /// </summary>
        [Fact]
        public void Rain_puts_out_fires_under_open_sky_through_the_map_tick()
        {
            CoreMap map = NewMap(40);
            var walls = new List<Thing>();
            var openCells = new List<IntVec3>();
            for (int i = 0; i < 12; i++)
            {
                var cell = new IntVec3(2 + i * 3, 0, 4);
                walls.Add(SpawnThing(map, cell, "Wall"));
                openCells.Add(cell);
                Assert.True(FireUtility.TryStartFireIn(cell, map, Fire.MinFireSize));
            }

            var shelteredCell = new IntVec3(20, 0, 30);
            Thing shelteredWall = SpawnThing(map, shelteredCell, "Wall");
            map.roofGrid.SetRoof(shelteredCell, RoofDefOf.RoofConstructed);
            Assert.True(FireUtility.TryStartFireIn(shelteredCell, map, Fire.MinFireSize));

            SetWeatherHard(map, WeatherDefOf.Rain);
            Assert.True(map.weatherManager.RainRate > 0.9f);

            RunGameTicks(map, 4000);

            int extinguished = openCells.Count - openCells.Count(c => FireAt(map, c) != null);

            // 4,000 ticks is ~26 of Fire.ComplexCalcsInterval, and Fire.BaseSkyExtinguishChance is the chance
            // per interval at full rain — so most of a dozen unroofed fires should be out, not one lucky one.
            // A band, never the literal count (CLAUDE.md).
            Assert.InRange(extinguished, 4, openCells.Count);
            Assert.NotNull(FireAt(map, shelteredCell));
            Assert.All(walls, w => Assert.False(w.Destroyed, "A wall burned away, so 'the fire went out' would not prove the rain did it."));
            Assert.False(shelteredWall.Destroyed);
        }

        [Fact]
        public void Clear_weather_puts_nothing_out()
        {
            CoreMap map = NewMap(20);
            var cell = new IntVec3(5, 0, 5);
            SpawnThing(map, cell, "Wall");
            Assert.True(FireUtility.TryStartFireIn(cell, map, Fire.MinFireSize));

            Assert.Same(WeatherDefOf.Clear, map.weatherManager.CurWeather);
            RunGameTicks(map, 4000);

            Assert.NotNull(FireAt(map, cell));
        }

        private static Fire? FireAt(CoreMap map, IntVec3 cell) =>
            FireUtility.AllFires(map).OfType<Fire>().FirstOrDefault(f => f.Position == cell && !f.Destroyed);

        // ---- transitions ----

        [Fact]
        public void Weather_fades_in_over_the_transition_rather_than_snapping()
        {
            CoreMap map = NewMap();
            map.weatherManager.TransitionTo(WeatherDefOf.Rain);

            Assert.Same(WeatherDefOf.Rain, map.weatherManager.CurWeather);
            Assert.Same(WeatherDefOf.Clear, map.weatherManager.LastWeather);
            Assert.Equal(0f, map.weatherManager.RainRate, 4);

            RunGameTicks(map, WeatherManager.TransitionTicks / 4);
            float quarterWay = map.weatherManager.RainRate;
            Assert.InRange(quarterWay, 0.15f, 0.35f);

            RunGameTicks(map, WeatherManager.TransitionTicks);
            Assert.Equal(WeatherDefOf.Rain.rainRate, map.weatherManager.RainRate, 4);
            Assert.True(map.weatherManager.RainRate > quarterWay);
        }

        [Fact]
        public void Weather_changes_on_its_own_over_time()
        {
            CoreMap map = NewMap();
            List<string> seen = WeatherHistory(map, 200000);

            Assert.True(seen.Count >= 2, "The weather never changed in over three in-game days.");
            Assert.True(seen.Distinct().Count() >= 2, "The weather changed but only ever to the same thing.");
        }

        /// <summary>Every weather this map moved through, in order, over <paramref name="ticks"/>.</summary>
        private static List<string> WeatherHistory(CoreMap map, int ticks)
        {
            var seen = new List<string>();
            WeatherDef? last = null;
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
                WeatherDef? cur = map.weatherManager.CurWeather;
                if (!ReferenceEquals(cur, last))
                {
                    seen.Add(cur!.defName);
                    last = cur;
                }
            }
            return seen;
        }

        // ---- determinism ----

        [Fact]
        public void The_same_seed_gives_the_same_weather_and_a_different_seed_does_not()
        {
            List<string> WithSeed(int seed)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();
                return WeatherHistory(NewMap(), 200000);
            }

            List<string> first = WithSeed(4242);
            List<string> again = WithSeed(4242);
            List<string> other = WithSeed(99);

            Assert.Equal(first, again);
            Assert.NotEqual(first, other);
        }

        // ---- chosen by biome, rainfall and temperature ----

        [Fact]
        public void Rain_is_far_likelier_in_a_rainforest_than_in_a_desert()
        {
            Assert.True(WetDrawsOutOf(2000, Climate("TropicalRainforest", 26f, 3000f))
                > 5 * WetDrawsOutOf(2000, Climate("Desert", 24f, 150f)),
                "A rainforest should see wet weather many times more often than a desert.");
        }

        [Fact]
        public void A_desert_still_gets_rain_sometimes()
        {
            Assert.True(WetDrawsOutOf(2000, Climate("Desert", 24f, 150f)) > 0,
                "A desert that can never rain is a desert nothing will ever put a fire out in.");
        }

        /// <summary>
        /// How many of <paramref name="draws"/> next-weather choices come up wet, for a map in
        /// <paramref name="climate"/>. Drives the decider directly rather than ticking years of game time.
        /// <para/>
        /// The map is put in fog first because a weather never immediately repeats itself: from a clear map,
        /// every draw would exclude Clear — the dominant dry weather in a desert — and the desert would look
        /// far wetter than it is. Fog is the small, symmetric thing to give up in both climates.
        /// </summary>
        private static int WetDrawsOutOf(int draws, MapClimate climate)
        {
            CoreMap map = NewMap(10);
            map.weatherManager.Climate = climate;
            SetWeatherHard(map, Weather("Fog"));
            int wet = 0;
            for (int i = 0; i < draws; i++)
            {
                WeatherDef? next = map.weatherManager.Decider.ChooseNextWeather();
                if (next != null && next.rainRate > 0f) wet++;
            }
            return wet;
        }

        [Fact]
        public void Cold_snows_and_warm_rains()
        {
            CoreMap map = NewMap(10);
            WeatherDecider decider = map.weatherManager.Decider;

            map.outdoorTemperature = 25f;
            Assert.True(decider.CurrentWeatherCommonality(WeatherDefOf.Rain) > 0f);
            Assert.Equal(0f, decider.CurrentWeatherCommonality(WeatherDefOf.SnowGentle));

            map.outdoorTemperature = -20f;
            Assert.Equal(0f, decider.CurrentWeatherCommonality(WeatherDefOf.Rain));
            Assert.True(decider.CurrentWeatherCommonality(WeatherDefOf.SnowGentle) > 0f);
        }

        [Fact]
        public void A_weather_never_immediately_repeats_itself()
        {
            CoreMap map = NewMap(10);
            SetWeatherHard(map, WeatherDefOf.Rain);
            Assert.False(WeatherDefOf.Rain.repeatable);
            Assert.Equal(0f, map.weatherManager.Decider.CurrentWeatherCommonality(WeatherDefOf.Rain));

            for (int i = 0; i < 200; i++)
            {
                Assert.NotSame(WeatherDefOf.Rain, map.weatherManager.Decider.ChooseNextWeather());
            }
        }

        // ---- outdoor temperature ----

        [Fact]
        public void Outdoor_temperature_swings_with_the_season_and_with_the_hour()
        {
            MapClimate temperate = Climate("TemperateForest", averageTemperature: 8f, rainfall: 1200f, latitude: 45f);

            int midsummerNoon = 25 * GenDate.TicksPerDay + GenDate.TicksPerDay / 2;
            int midwinterNoon = 55 * GenDate.TicksPerDay + GenDate.TicksPerDay / 2;
            float summer = global::SimWorld.Weather.GenTemperature.OutdoorTemperatureAt(temperate, midsummerNoon, 0f);
            float winter = global::SimWorld.Weather.GenTemperature.OutdoorTemperatureAt(temperate, midwinterNoon, 0f);

            Assert.True(summer > temperate.averageTemperature, "Midsummer is warmer than the annual mean.");
            Assert.True(winter < temperate.averageTemperature, "Midwinter is colder than the annual mean.");
            Assert.Equal(Season.Summer, GenDate.SeasonAt(midsummerNoon, temperate.longitude, temperate.latitude));
            Assert.Equal(Season.Winter, GenDate.SeasonAt(midwinterNoon, temperate.longitude, temperate.latitude));

            float noon = global::SimWorld.Weather.GenTemperature.OutdoorTemperatureAt(temperate, midsummerNoon, 0f);
            float midnight = global::SimWorld.Weather.GenTemperature.OutdoorTemperatureAt(temperate, midsummerNoon + GenDate.TicksPerDay / 2, 0f);
            Assert.True(noon > midnight, "Noon is warmer than midnight.");
        }

        [Fact]
        public void The_tropics_have_seasons_and_the_poles_have_them_harder()
        {
            float equator = Math.Abs(global::SimWorld.Weather.GenTemperature.SeasonalShiftAmplitudeAt(0f));
            float temperate = Math.Abs(global::SimWorld.Weather.GenTemperature.SeasonalShiftAmplitudeAt(45f));
            float pole = Math.Abs(global::SimWorld.Weather.GenTemperature.SeasonalShiftAmplitudeAt(89f));
            Assert.True(equator < temperate);
            Assert.True(temperate < pole);

            // Southern hemisphere: same size, opposite sign, so one cosine serves both.
            Assert.Equal(-global::SimWorld.Weather.GenTemperature.SeasonalShiftAmplitudeAt(45f),
                global::SimWorld.Weather.GenTemperature.SeasonalShiftAmplitudeAt(-45f), 4);
        }

        [Fact]
        public void A_map_that_knows_its_tile_has_its_outdoor_temperature_driven_by_the_weather()
        {
            CoreMap map = NewMap();
            map.weatherManager.Climate = Climate("TemperateForest", 8f, 1200f);
            SetWeatherHard(map, WeatherDefOf.Clear);
            RunGameTicks(map, 1);
            float clear = map.outdoorTemperature;

            SetWeatherHard(map, Weather("Rain"));
            RunGameTicks(map, 1);
            float raining = map.outdoorTemperature;

            Assert.True(Weather("Rain").temperatureOffset < 0f);
            Assert.True(raining < clear, "Rain should cool the map it falls on.");
            Assert.Equal(clear + Weather("Rain").temperatureOffset, raining, 2);
        }

        /// <summary>
        /// The wiring that cannot be faked with a hand-built <see cref="MapClimate"/>: a settlement's real
        /// interior map, generated on a real world tile, finds its own climate through <see cref="Find.World"/>
        /// and has its outdoor temperature driven by it. Without this the whole climate path could be dead in
        /// a running game and every other test here would still pass.
        /// </summary>
        [Fact]
        public void A_real_settlement_interior_picks_up_its_tile_climate_and_is_driven_by_it()
        {
            global::SimWorld.World.World world = global::SimWorld.World.Gen.WorldGenerator.GenerateWorld(
                "weather-climate",
                0.3f,
                global::SimWorld.World.OverallRainfall.Normal,
                global::SimWorld.World.OverallTemperature.Normal,
                global::SimWorld.World.OverallPopulation.Normal,
                "Test",
                subdivisionOverride: 4,
                soloStart: true);
            Find.World = world;

            int tile = Enumerable.Range(0, world.grid.TilesCount)
                .First(i => !world.grid.Tiles[i].WaterCovered && world.grid.Tiles[i].biome != null);
            global::SimWorld.World.Settlement settlement = global::SimWorld.World.SettlementFounder.Found(
                world, tile, world.factions.First(), 20, new RandomStream(31));

            CoreMap map = settlement.EnterMap(world);
            Assert.Equal(tile, map.tile);

            MapClimate? climate = map.weatherManager.Climate;
            Assert.NotNull(climate);
            Assert.Same(world.grid.Tiles[tile].biome, climate!.biome);
            Assert.Equal(world.grid.Tiles[tile].temperature, climate.averageTemperature, 4);
            Assert.Equal(world.grid.Tiles[tile].rainfall, climate.rainfall, 4);

            map.MapTick();
            Assert.Equal(
                global::SimWorld.Weather.GenTemperature.OutdoorTemperatureAt(climate, Find.TickManager.TicksGame, map.weatherManager.TemperatureOffset),
                map.outdoorTemperature,
                3);
        }

        /// <summary>
        /// The no-regression guarantee every other module depends on: a map with no world tile behind it
        /// keeps whatever outdoor temperature its owner set. Rooms, plant growth and food spoilage all read
        /// that field, and their tests set it by hand.
        /// </summary>
        [Fact]
        public void A_map_with_no_world_tile_keeps_the_outdoor_temperature_it_was_given()
        {
            CoreMap map = NewMap();
            Assert.Null(map.weatherManager.Climate);

            map.outdoorTemperature = -10f;
            RunGameTicks(map, 500);

            Assert.Equal(-10f, map.outdoorTemperature, 4);
        }

        // ---- what weather does to people ----

        [Fact]
        public void Standing_out_in_the_rain_soaks_a_pawn_and_shelter_keeps_them_dry()
        {
            CoreMap map = NewMap();
            Pawn outside = NewHuman("Outside");
            Pawn inside = NewHuman("Inside");
            var shelteredCell = new IntVec3(10, 0, 10);
            map.roofGrid.SetRoof(shelteredCell, RoofDefOf.RoofConstructed);
            GenSpawn.Spawn(outside, new IntVec3(3, 0, 3), map);
            GenSpawn.Spawn(inside, shelteredCell, map);

            SetWeatherHard(map, WeatherDefOf.Rain);
            RunOneExposureSweep(map);

            Assert.Contains(outside.needs.mood!.thoughts.memories.Memories, m => m.def == WeatherThoughtDefOf.SoakingWet);
            Assert.DoesNotContain(inside.needs.mood!.thoughts.memories.Memories, m => m.def == WeatherThoughtDefOf.SoakingWet);
            Assert.True(outside.needs.mood!.thoughts.TotalMoodOffset() < inside.needs.mood!.thoughts.TotalMoodOffset(),
                "Being soaked should cost mood, not just carry a label.");
        }

        [Fact]
        public void Clear_weather_soaks_nobody()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman("Dry");
            GenSpawn.Spawn(pawn, new IntVec3(3, 0, 3), map);

            RunOneExposureSweep(map);

            Assert.DoesNotContain(pawn.needs.mood!.thoughts.memories.Memories, m => m.def == WeatherThoughtDefOf.SoakingWet);
        }

        /// <summary>
        /// Runs exactly one weather tick, on a tick the exposed-thought sweep is due. Deliberately not 500
        /// ticks of a running game: a spawned pawn ticks too, and one that wandered out from under its roof
        /// would make "the sheltered pawn stayed dry" a test of the think tree rather than of the weather.
        /// </summary>
        private static void RunOneExposureSweep(CoreMap map)
        {
            Find.TickManager.DebugSetTicksGame(WeatherManager.ExposedThoughtIntervalTicks);
            map.MapTick();
        }

        [Fact]
        public void Bad_weather_slows_a_pawn_down_outdoors_and_not_indoors()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman("Walker");
            var open = new IntVec3(3, 0, 3);
            GenSpawn.Spawn(pawn, open, map);

            int clearTicks = pawn.pather.TicksPerMoveCardinal;

            WeatherDef storm = Weather("SnowHard");
            Assert.True(storm.moveSpeedMultiplier < 1f);
            SetWeatherHard(map, storm);
            int stormTicks = pawn.pather.TicksPerMoveCardinal;
            Assert.True(stormTicks > clearTicks, "A pawn should be slower crossing open ground in a snowstorm.");

            map.roofGrid.SetRoof(open, RoofDefOf.RoofConstructed);
            Assert.Equal(clearTicks, pawn.pather.TicksPerMoveCardinal);
        }

        [Fact]
        public void Bad_weather_costs_accuracy_only_when_the_shot_crosses_open_sky()
        {
            CoreMap map = NewMap();
            Pawn shooter = NewHuman("Shooter");
            Pawn target = NewHuman("Target");
            var shooterCell = new IntVec3(4, 0, 4);
            var targetCell = new IntVec3(4, 0, 9);
            GenSpawn.Spawn(shooter, shooterCell, map);
            GenSpawn.Spawn(target, targetCell, map);

            var props = new VerbProperties { accuracyTouch = 1f, accuracyShort = 1f, accuracyMedium = 1f, accuracyLong = 1f };
            var verb = new Verb_LaunchProjectile(shooter, props);

            Assert.Equal(1f, ShotReport.HitReportFor(shooter, verb, target, 5f, Array.Empty<CoverInfo>()).factorFromWeather, 4);

            WeatherDef storm = Weather("RainyThunderstorm");
            Assert.True(storm.accuracyMultiplier < 1f);
            SetWeatherHard(map, storm);
            ShotReport inTheOpen = ShotReport.HitReportFor(shooter, verb, target, 5f, Array.Empty<CoverInfo>());
            Assert.Equal(storm.accuracyMultiplier, inTheOpen.factorFromWeather, 4);

            // Both ends under a roof: the weather never touches the shot.
            map.roofGrid.SetRoof(shooterCell, RoofDefOf.RoofConstructed);
            map.roofGrid.SetRoof(targetCell, RoofDefOf.RoofConstructed);
            ShotReport indoors = ShotReport.HitReportFor(shooter, verb, target, 5f, Array.Empty<CoverInfo>());
            Assert.Equal(1f, indoors.factorFromWeather, 4);
            Assert.True(indoors.TotalEstimatedHitChance > inTheOpen.TotalEstimatedHitChance);
        }

        // ---- Scribe ----

        [Fact]
        public void Weather_round_trips_through_scribe_mid_transition()
        {
            CoreMap map = NewMap();
            map.weatherManager.TransitionTo(WeatherDefOf.Rain);
            RunGameTicks(map, WeatherManager.TransitionTicks / 2);

            WeatherManager before = map.weatherManager;
            Assert.InRange(before.TransitionLerpFactor, 0.4f, 0.6f);
            float rainRateBefore = before.RainRate;
            int ageBefore = before.CurWeatherAge;
            int durationBefore = before.Decider.CurWeatherDuration;

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            WeatherManager after = loaded.weatherManager;
            Assert.Same(WeatherDefOf.Rain, after.CurWeather);
            Assert.Same(WeatherDefOf.Clear, after.LastWeather);
            Assert.Equal(ageBefore, after.CurWeatherAge);
            Assert.Equal(durationBefore, after.Decider.CurWeatherDuration);
            Assert.Equal(rainRateBefore, after.RainRate, 4);

            // And it carries on from there rather than restarting the transition.
            RunGameTicks(loaded, WeatherManager.TransitionTicks);
            Assert.Equal(WeatherDefOf.Rain.rainRate, after.RainRate, 4);
        }

        /// <summary>Two games loaded from the same save, with the same seed and the same clock, get the same
        /// weather from there on — the property that makes a reloaded save continue rather than diverge.</summary>
        [Fact]
        public void Two_loads_of_one_save_get_the_same_weather_from_there_on()
        {
            CoreMap source = NewMap();
            RunGameTicks(source, 20000);
            string xml = Scribe.SaveToString(source, "map");

            List<string> HistoryOfAFreshLoad()
            {
                Rand.Current = new RandomStream(777);
                Find.TickManager = new TickManager();
                CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
                Assert.Empty(errors);
                Find.TickManager.DebugSetTicksGame(20000);
                return WeatherHistory(loaded, 120000);
            }

            List<string> first = HistoryOfAFreshLoad();
            Assert.NotEmpty(first);
            Assert.Equal(first, HistoryOfAFreshLoad());
        }
    }
}
