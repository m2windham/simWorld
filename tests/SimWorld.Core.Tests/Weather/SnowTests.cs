using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Weather
{
    /// <summary>
    /// Snow (RimWorld's <c>SnowGrid</c>, <c>SnowUtility</c>, the snow half of <c>SteadyEnvironmentEffects</c>
    /// and <c>JobDriver_ClearSnow</c>): it lies where it is cold and open, melts where it is warm, slows
    /// walking, keeps seeds out of the ground, can be shovelled and survives a save. Every tuned number is
    /// pinned as a trend or a round trip, never as the recalled literal (CLAUDE.md).
    /// </summary>
    public class SnowTests : ContentTestBase
    {
        public SnowTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static WeatherDef Weather(string name) => DefDatabase<WeatherDef>.GetNamed(name);

        private static void SetWeatherHard(CoreMap map, WeatherDef weather)
        {
            map.weatherManager.TransitionTo(weather);
            map.weatherManager.TransitionTo(weather);
        }

        private static void RunGameTicks(CoreMap map, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                map.MapTick();
            }
        }

        private static float MeanDepth(CoreMap map, IEnumerable<IntVec3> cells)
        {
            float total = 0f;
            int n = 0;
            foreach (IntVec3 c in cells)
            {
                total += map.snowGrid.GetDepth(c);
                n++;
            }
            return n == 0 ? 0f : total / n;
        }

        private static void SnowEverywhere(CoreMap map, float depth)
        {
            foreach (IntVec3 c in map.AllCells) map.snowGrid.SetDepth(c, depth);
        }

        // ---- falling ----

        [Fact]
        public void Snow_builds_up_on_open_ground_while_it_snows_below_freezing()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -10f;
            SetWeatherHard(map, Weather("SnowHard"));

            RunGameTicks(map, 2000);
            float early = map.snowGrid.TotalDepth;
            RunGameTicks(map, 2000);
            float later = map.snowGrid.TotalDepth;

            Assert.True(early > 0f, "Snow should lie on open ground after a snowstorm has been blowing a while.");
            Assert.True(later > early, "It should keep deepening while the storm goes on.");
            Assert.True(MeanDepth(map, map.AllCells) <= SnowGrid.MaxDepth);
        }

        [Fact]
        public void No_snow_settles_under_a_roof()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -10f;
            var roofed = new CellRect(4, 4, 6, 6);
            foreach (IntVec3 c in roofed.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            SetWeatherHard(map, Weather("SnowHard"));

            RunGameTicks(map, 4000);

            Assert.Equal(0f, MeanDepth(map, roofed.Cells));
            Assert.True(map.snowGrid.TotalDepth > 0f, "The open ground around the roof should still have snow on it.");
        }

        [Fact]
        public void Clear_skies_lay_no_snow_however_cold()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -25f;
            SetWeatherHard(map, WeatherDefOf.Clear);

            RunGameTicks(map, 4000);

            Assert.Equal(0f, map.snowGrid.TotalDepth);
        }

        [Fact]
        public void Water_and_walls_hold_no_snow()
        {
            CoreMap map = NewMap();
            var pond = new IntVec3(3, 0, 3);
            var wallCell = new IntVec3(8, 0, 8);
            map.terrainGrid.SetTerrain(pond, DefDatabase<TerrainDef>.GetNamed("WaterShallow"));
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), wallCell, map);

            map.snowGrid.SetDepth(pond, 1f);
            map.snowGrid.AddDepth(wallCell, 1f);

            Assert.Equal(0f, map.snowGrid.GetDepth(pond));
            Assert.Equal(0f, map.snowGrid.GetDepth(wallCell));
            Assert.Equal(0f, map.snowGrid.TotalDepth);
        }

        [Fact]
        public void Depth_is_clamped_and_the_total_follows_every_change()
        {
            CoreMap map = NewMap(5);
            var c = new IntVec3(2, 0, 2);

            map.snowGrid.AddDepth(c, 5f);
            Assert.Equal(SnowGrid.MaxDepth, map.snowGrid.GetDepth(c));
            map.snowGrid.AddDepth(c, -0.25f);
            Assert.Equal(0.75f, map.snowGrid.GetDepth(c), 4);
            Assert.Equal(0.75f, map.snowGrid.TotalDepth, 4);
            map.snowGrid.AddDepth(c, -5f);
            Assert.Equal(0f, map.snowGrid.GetDepth(c));
            Assert.Equal(0f, map.snowGrid.TotalDepth, 4);
        }

        // ---- melting ----

        [Fact]
        public void Melt_is_nothing_below_freezing_and_grows_with_warmth()
        {
            Assert.Equal(0f, SteadyEnvironmentEffects.MeltAmountAt(-5f));
            Assert.True(SteadyEnvironmentEffects.MeltAmountAt(2f) > 0f);
            Assert.True(SteadyEnvironmentEffects.MeltAmountAt(5f) > SteadyEnvironmentEffects.MeltAmountAt(2f));
            Assert.True(SteadyEnvironmentEffects.MeltAmountAt(20f) > SteadyEnvironmentEffects.MeltAmountAt(5f));
        }

        [Fact]
        public void Snow_melts_away_when_it_warms_up_and_stays_while_it_is_cold()
        {
            CoreMap cold = NewMap();
            cold.outdoorTemperature = -5f;
            SnowEverywhere(cold, 0.8f);
            CoreMap warm = NewMap();
            warm.outdoorTemperature = 15f;
            SnowEverywhere(warm, 0.8f);

            RunGameTicks(cold, 4000);
            for (int i = 0; i < 4000; i++) warm.MapTick();

            Assert.Equal(0.8f, MeanDepth(cold, cold.AllCells), 3);
            Assert.Equal(0f, warm.snowGrid.TotalDepth);
        }

        [Fact]
        public void A_heated_room_melts_its_snow_on_a_freezing_day()
        {
            CoreMap map = NewMap();
            var rect = new CellRect(4, 4, 8, 8);
            foreach (IntVec3 c in rect.EdgeCells) GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Wall")), c, map);
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            map.outdoorTemperature = -10f;
            map.MapTick();

            CellRect inside = rect.ContractedBy(1);
            Room room = map.roomTracker.RoomAt(new IntVec3(8, 0, 8))!;
            Assert.False(room.Group.UsesOutdoorTemperature);
            foreach (IntVec3 c in inside.Cells) map.snowGrid.SetDepth(c, 0.5f);
            var outside = new IntVec3(1, 0, 1);
            map.snowGrid.SetDepth(outside, 0.5f);

            // Hold the room warm the way a heater would, every tick, against the cold outside.
            for (int i = 0; i < 4000; i++)
            {
                room.Group.temperature = 20f;
                map.MapTick();
            }

            Assert.Equal(0f, MeanDepth(map, inside.Cells));
            Assert.Equal(0.5f, map.snowGrid.GetDepth(outside), 3);
        }

        // ---- what it does ----

        [Fact]
        public void Snow_categories_rise_with_depth()
        {
            Assert.Equal(SnowCategory.None, SnowUtility.GetSnowCategory(0f));
            SnowCategory last = SnowCategory.None;
            for (float d = 0f; d <= 1f; d += 0.01f)
            {
                SnowCategory cur = SnowUtility.GetSnowCategory(d);
                Assert.True(cur >= last);
                last = cur;
            }
            Assert.Equal(SnowCategory.Thick, SnowUtility.GetSnowCategory(1f));
        }

        [Fact]
        public void Deeper_snow_costs_more_to_walk_through_and_a_dusting_costs_nothing()
        {
            CoreMap map = NewMap(5);
            var c = new IntVec3(2, 0, 2);
            int bare = map.pathGrid.CalculatedCostAt(c);
            int barePerceived = map.pathGrid.PerceivedPathCostAt(c);

            map.snowGrid.SetDepth(c, 0.1f);
            Assert.Equal(SnowCategory.Dusting, map.snowGrid.GetCategory(c));
            Assert.Equal(bare, map.pathGrid.CalculatedCostAt(c));

            int last = bare;
            foreach (float depth in new[] { 0.3f, 0.6f, 1f })
            {
                map.snowGrid.SetDepth(c, depth);
                int cost = map.pathGrid.CalculatedCostAt(c);
                Assert.True(cost > last, $"Snow {depth} should cost more to cross than the band below it.");
                last = cost;
            }

            // The cached grid the pathfinder reads was re-costed when the category changed, not left stale.
            Assert.True(map.pathGrid.PerceivedPathCostAt(c) > barePerceived);
            map.snowGrid.SetDepth(c, 0f);
            Assert.Equal(barePerceived, map.pathGrid.PerceivedPathCostAt(c));
        }

        [Fact]
        public void A_pawn_takes_longer_to_cross_deep_snow()
        {
            int Crossing(float depth)
            {
                CoreMap map = NewMap(30);
                foreach (IntVec3 c in map.AllCells) map.snowGrid.SetDepth(c, depth);
                Pawn pawn = NewHuman("Walker");
                GenSpawn.Spawn(pawn, new IntVec3(2, 0, 15), map);
                var goal = new IntVec3(27, 0, 15);
                // The pather alone, so nothing the think tree decides can interrupt the walk being timed.
                pawn.pather.StartPath(goal, global::SimWorld.AI.PathEndMode.OnCell);
                for (int t = 1; t <= 3000; t++)
                {
                    pawn.pather.PatherTick();
                    if (pawn.Position == goal) return t;
                }
                return int.MaxValue;
            }

            int bare = Crossing(0f);
            int deep = Crossing(1f);
            Assert.True(bare < 3000, "The walk on bare ground should finish.");
            Assert.True(deep > bare, "The same walk through thick snow should take longer.");
        }

        [Fact]
        public void Seeds_do_not_go_into_snowed_in_ground()
        {
            CoreMap map = NewMap(5);
            var c = new IntVec3(2, 0, 2);
            Assert.True(PlantUtility.SnowAllowsPlanting(c, map));
            map.snowGrid.SetDepth(c, 0.1f);
            Assert.True(PlantUtility.SnowAllowsPlanting(c, map));
            map.snowGrid.SetDepth(c, 0.5f);
            Assert.False(PlantUtility.SnowAllowsPlanting(c, map));
        }

        // ---- clearing ----

        [Fact]
        public void Nobody_shovels_snow_outside_the_snow_clear_area_or_a_mere_dusting_inside_it()
        {
            CoreMap map = NewMap();
            Pawn pawn = NewHuman("Shoveller");
            GenSpawn.Spawn(pawn, new IntVec3(2, 0, 2), map);
            var giver = new WorkGiver_ClearSnow();
            var deep = new IntVec3(8, 0, 8);
            var dusted = new IntVec3(9, 0, 8);
            map.snowGrid.SetDepth(deep, 0.9f);
            map.snowGrid.SetDepth(dusted, 0.1f);

            Assert.True(giver.ShouldSkip(pawn), "With no snow-clear area painted there is nothing to do.");

            map.areaManager.SnowClear[deep] = true;
            map.areaManager.SnowClear[dusted] = true;
            Assert.False(giver.ShouldSkip(pawn));
            Assert.True(giver.HasJobOnCell(pawn, deep));
            Assert.False(giver.HasJobOnCell(pawn, dusted));
        }

        [Fact]
        public void A_citizen_shovels_the_snow_clear_area_bare()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -10f;
            Pawn pawn = NewHuman("Shoveller");
            GenSpawn.Spawn(pawn, new IntVec3(2, 0, 2), map);
            var path = new CellRect(8, 8, 3, 1);
            foreach (IntVec3 c in path.Cells)
            {
                map.snowGrid.SetDepth(c, 1f);
                map.areaManager.SnowClear[c] = true;
            }
            var untouched = new IntVec3(15, 0, 15);
            map.snowGrid.SetDepth(untouched, 1f);

            RunTicks(3000, pawn);

            Assert.Equal(0f, MeanDepth(map, path.Cells));
            Assert.Equal(1f, map.snowGrid.GetDepth(untouched));
        }

        [Fact]
        public void Deeper_snow_takes_longer_to_shovel()
        {
            int TicksToClear(float depth)
            {
                CoreMap map = NewMap();
                Pawn pawn = NewHuman("Shoveller");
                var cell = new IntVec3(5, 0, 5);
                GenSpawn.Spawn(pawn, new IntVec3(5, 0, 4), map);
                map.snowGrid.SetDepth(cell, depth);
                pawn.jobs.StartJob(new global::SimWorld.AI.Job(SnowJobDefOf.ClearSnow, cell));
                for (int t = 1; t <= 2000; t++)
                {
                    RunTicks(1, pawn);
                    if (map.snowGrid.GetDepth(cell) == 0f) return t;
                }
                return int.MaxValue;
            }

            int thin = TicksToClear(0.3f);
            int thick = TicksToClear(1f);
            Assert.True(thin < 2000, "A thin layer should be cleared.");
            Assert.True(thick < 2000, "A full depth should be cleared.");
            Assert.True(thick > thin);
        }

        // ---- the weather decider ----

        [Fact]
        public void A_cold_climate_snows_through_the_weather_decider_and_the_snow_lies()
        {
            CoreMap map = NewMap(10);
            map.weatherManager.Climate = new MapClimate
            {
                biome = DefDatabase<global::SimWorld.World.BiomeDef>.GetNamed("Tundra"),
                averageTemperature = -25f,
                rainfall = 600f,
                latitude = 70f,
                longitude = 0f,
            };

            bool snowed = false;
            for (int t = 0; t < 400000 && !snowed; t += 1000)
            {
                RunGameTicks(map, 1000);
                if ((map.weatherManager.CurWeather?.snowRate ?? 0f) > 0f) snowed = true;
            }
            Assert.True(snowed, "A tundra at -25 °C should start snowing on its own within a few days.");

            RunGameTicks(map, 6000);
            Assert.True(map.snowGrid.TotalDepth > 0f, "And the snow it chose should be lying on the ground.");
        }

        // ---- determinism and Scribe ----

        [Fact]
        public void Snowfall_draws_nothing_from_the_ambient_stream()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -10f;
            SetWeatherHard(map, Weather("SnowHard"));
            map.MapTick(); // floods the rooms; snow only falls on a cell that has one
            uint before = Rand.Current.Iterations;

            for (int i = 0; i < 2000; i++) map.steadyEnvironmentEffects.SteadyEnvironmentEffectsTick();

            Assert.True(map.snowGrid.TotalDepth > 0f);
            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void The_same_map_snows_the_same_way_twice()
        {
            float[] Run()
            {
                CoreMap.ResetMapIdCounter();
                CoreMap map = NewMap();
                map.outdoorTemperature = -10f;
                SetWeatherHard(map, Weather("SnowGentle"));
                map.MapTick();
                for (int i = 0; i < 3000; i++) map.steadyEnvironmentEffects.SteadyEnvironmentEffectsTick();
                var depths = new List<float>();
                foreach (IntVec3 c in map.AllCells) depths.Add(map.snowGrid.GetDepth(c));
                return depths.ToArray();
            }

            float[] first = Run();
            Assert.Contains(first, d => d > 0f);
            Assert.Equal(first, Run());
        }

        [Fact]
        public void Snow_and_the_snow_clear_area_round_trip_through_scribe()
        {
            CoreMap map = NewMap();
            var deep = new IntVec3(3, 0, 4);
            var thin = new IntVec3(7, 0, 2);
            map.snowGrid.SetDepth(deep, 0.9f);
            map.snowGrid.SetDepth(thin, 0.3f);
            map.areaManager.SnowClear[deep] = true;
            map.areaManager.Home[thin] = true;
            int deepCost = map.pathGrid.PerceivedPathCostAt(deep);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(0.9f, loaded.snowGrid.GetDepth(deep), 4);
            Assert.Equal(0.3f, loaded.snowGrid.GetDepth(thin), 4);
            Assert.Equal(map.snowGrid.TotalDepth, loaded.snowGrid.TotalDepth, 3);
            Assert.Equal(deepCost, loaded.pathGrid.PerceivedPathCostAt(deep));
            Assert.True(loaded.areaManager.SnowClear[deep]);
            Assert.False(loaded.areaManager.SnowClear[thin]);
            Assert.Equal(1, loaded.areaManager.SnowClear.TrueCount);
            Assert.True(loaded.areaManager.Home[thin]);
            Assert.Equal(1, loaded.areaManager.Home.TrueCount);
        }

        [Fact]
        public void A_snowless_map_saves_no_snow_at_all()
        {
            CoreMap map = NewMap();
            string xml = Scribe.SaveToString(map, "map");
            Assert.DoesNotContain("snowDepth", xml);
        }
    }
}
