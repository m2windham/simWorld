using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Weather;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using FilthMaker = SimWorld.Filth.FilthMaker;

namespace SimWorld.Tests.Weather
{
    /// <summary>
    /// The non-snow half of RimWorld's <c>SteadyEnvironmentEffects</c>, plus the fire's own rain check it sits
    /// beside: rain puts out a fire under open sky and not under a roof, things left outside wear down and
    /// things indoors do not, rain washes filth off open ground, and a room hot enough sets what is in it
    /// alight. Every recalled number is pinned as a trend, never as the literal (CLAUDE.md).
    /// </summary>
    public class SteadyEnvironmentEffectsTests : ContentTestBase
    {
        public SteadyEnvironmentEffectsTests(CoreContentFixture content) : base(content)
        {
        }

        // 12×12 = 144 cells: one visited per tick, so every cell is seen once every 144 ticks.
        private const int Size = 12;
        private const int Sweep = Size * Size;

        private static readonly IntVec3 Outside = new IntVec3(2, 0, 2);

        // The middle of a walled, roofed 3×3 room whose walls run from (6,6) to (10,10).
        private static readonly IntVec3 Inside = new IntVec3(8, 0, 8);

        private static CoreMap NewMap()
        {
            var map = new CoreMap(Size, Size, TerrainDefOf.Soil);
            map.outdoorTemperature = 15f;
            return map;
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

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

        /// <summary>Walls round <see cref="Inside"/> and a roof over it, so its room is closed to the
        /// outdoors.</summary>
        private static void BuildRoom(CoreMap map)
        {
            var rect = new CellRect(6, 6, 5, 5);
            foreach (IntVec3 c in rect.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            foreach (IntVec3 c in rect.EdgeCells)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall"), Def("BlocksGranite")), c, map);
            }
            map.roomTracker.Notify_Dirty();
            map.roomTracker.RoomTrackerTick();
            Assert.False(map.roomTracker.RoomAt(Inside)!.Group.UsesOutdoorTemperature);
        }

        private static Thing SpawnItem(CoreMap map, string def, IntVec3 cell)
        {
            Thing thing = ThingMaker.MakeThing(Def(def));
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        // ---- rain on fire ----

        [Fact]
        public void Rain_puts_out_a_fire_under_open_sky_but_not_one_under_a_roof()
        {
            CoreMap map = NewMap();
            BuildRoom(map);
            SpawnItem(map, "WoodLog", Outside);
            SpawnItem(map, "WoodLog", Inside);
            Assert.True(FireUtility.TryStartFireIn(Outside, map, Fire.MinFireSize));
            Assert.True(FireUtility.TryStartFireIn(Inside, map, Fire.MinFireSize));
            Fire outdoor = FireUtility.AllFires(map).Cast<Fire>().Single(f => f.Position == Outside);
            Fire indoor = FireUtility.AllFires(map).Cast<Fire>().Single(f => f.Position == Inside);
            SetWeatherHard(map, DefDatabase<WeatherDef>.GetNamed("Rain"));

            RunGameTicks(map, Fire.ComplexCalcsInterval * 3);

            Assert.True(outdoor.Destroyed, "A new fire under open sky should go out at its first interval of rain.");
            Assert.False(indoor.Destroyed, "Rain cannot reach a fire under a roof.");
        }

        // ---- deterioration ----

        [Fact]
        public void A_log_left_outside_wears_down_and_one_indoors_does_not()
        {
            CoreMap map = NewMap();
            BuildRoom(map);
            Thing outdoor = SpawnItem(map, "WoodLog", Outside);
            Thing indoor = SpawnItem(map, "WoodLog", Inside);
            int max = outdoor.MaxHitPoints;

            RunGameTicks(map, Sweep * 150);

            Assert.True(outdoor.HitPoints < max, "Wood under open sky should lose hit points over many visits.");
            Assert.Equal(max, indoor.HitPoints);
        }

        [Fact]
        public void Steel_never_deteriorates_even_outside()
        {
            CoreMap map = NewMap();
            Thing steel = SpawnItem(map, "Steel", Outside);

            RunGameTicks(map, Sweep * 150);

            Assert.Equal(steel.MaxHitPoints, steel.HitPoints);
        }

        [Fact]
        public void Deterioration_rate_falls_from_open_sky_to_a_lone_roof_to_indoors_and_rises_in_shallow_water()
        {
            Thing log = ThingMaker.MakeThing(Def("WoodLog"));
            TerrainDef soil = TerrainDefOf.Soil;
            TerrainDef water = DefDatabase<TerrainDef>.GetNamed("WaterShallow");

            float open = SteadyEnvironmentEffects.FinalDeteriorationRate(log, roofed: false, roomUsesOutdoorTemperature: true, soil);
            float loneRoof = SteadyEnvironmentEffects.FinalDeteriorationRate(log, roofed: true, roomUsesOutdoorTemperature: true, soil);
            float indoors = SteadyEnvironmentEffects.FinalDeteriorationRate(log, roofed: true, roomUsesOutdoorTemperature: false, soil);
            float wet = SteadyEnvironmentEffects.FinalDeteriorationRate(log, roofed: false, roomUsesOutdoorTemperature: true, water);

            Assert.True(open > loneRoof);
            Assert.True(loneRoof > 0f);
            Assert.Equal(0f, indoors);
            Assert.True(wet > open);
        }

        // ---- filth ----

        [Fact]
        public void Rain_washes_filth_off_open_ground_but_not_from_under_a_roof()
        {
            CoreMap map = NewMap();
            BuildRoom(map);
            ThingDef dirt = Def("Filth_Dirt");
            Assert.True(FilthMaker.TryMakeFilth(Outside, map, dirt));
            Assert.True(FilthMaker.TryMakeFilth(Inside, map, dirt));
            SetWeatherHard(map, DefDatabase<WeatherDef>.GetNamed("Rain"));

            RunGameTicks(map, Sweep * 4);

            Assert.DoesNotContain(map.thingGrid.ThingsListAt(Outside), t => t is global::SimWorld.Filth.Filth);
            Assert.Contains(map.thingGrid.ThingsListAt(Inside), t => t is global::SimWorld.Filth.Filth);
        }

        [Fact]
        public void Dry_weather_leaves_outdoor_filth_alone()
        {
            CoreMap map = NewMap();
            Assert.True(FilthMaker.TryMakeFilth(Outside, map, Def("Filth_Dirt")));
            SetWeatherHard(map, WeatherDefOf.Clear);

            RunGameTicks(map, Sweep * 2);

            Assert.Contains(map.thingGrid.ThingsListAt(Outside), t => t is global::SimWorld.Filth.Filth);
        }

        // ---- auto-ignition ----

        [Theory]
        [InlineData(900f, true)]
        [InlineData(200f, false)]
        public void A_room_hotter_than_ignition_sets_its_contents_alight_and_a_merely_hot_one_does_not(float temperature, bool expectFire)
        {
            CoreMap map = NewMap();
            BuildRoom(map);
            SpawnItem(map, "WoodLog", Inside);
            RoomGroup group = map.roomTracker.RoomAt(Inside)!.Group;

            bool burned = false;
            for (int i = 0; i < Sweep * 10 && !burned; i++)
            {
                group.temperature = temperature;
                RunGameTicks(map, 1);
                burned = FireUtility.AllFires(map).Any();
            }

            Assert.Equal(expectFire, burned);
        }

        // ---- determinism ----

        [Fact]
        public void The_sweep_draws_nothing_from_the_ambient_stream()
        {
            CoreMap map = NewMap();
            BuildRoom(map);
            SpawnItem(map, "WoodLog", Outside);
            FilthMaker.TryMakeFilth(new IntVec3(3, 0, 3), map, Def("Filth_Dirt"));
            SetWeatherHard(map, DefDatabase<WeatherDef>.GetNamed("Rain"));

            uint before = Rand.Current.Iterations;
            for (int i = 0; i < Sweep * 3; i++)
            {
                Find.TickManager.DoSingleTick();
                map.steadyEnvironmentEffects.SteadyEnvironmentEffectsTick();
            }

            Assert.Equal(before, Rand.Current.Iterations);
        }
    }
}
