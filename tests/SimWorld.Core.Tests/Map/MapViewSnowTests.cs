using System;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Tests.Content;
using SimWorld.Weather;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The snow layer on the map seam (<see cref="SnowLayer"/>, <see cref="MapViewTracker.SnowVersion"/>): the
    /// host redraws snow only when its version moves, and the version moves on a fixed rule in
    /// <c>SnowGrid.CheckVisualOrPathCostChange</c> — a cell crossing one of <see cref="SnowGrid.VisualSteps"/>
    /// depth steps or changing <see cref="SnowCategory"/> — rather than RimWorld's random remesh roll.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewSnowTests : ContentTestBase
    {
        public MapViewSnowTests(CoreContentFixture content) : base(content)
        {
            MapViewTracker.ResetSessionIdCounter();
        }

        private static CoreMap NewMap(int sizeX = 20, int sizeZ = 20) =>
            new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        [Fact]
        public void An_unchanged_grid_keeps_its_version_and_sends_no_snow()
        {
            CoreMap map = NewMap();
            map.snowGrid.SetDepth(new IntVec3(3, 0, 3), 0.40f);

            MapViewSnapshot first = MapViewSnapshot.Capture(map);
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, first.Versions);

            Assert.False(delta.FullResync);
            Assert.Null(delta.Snow);
            Assert.Equal(first.Versions.Snow, delta.Versions.Snow);
        }

        [Fact]
        public void Snow_creeping_up_within_one_step_does_not_move_the_version()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(3, 0, 3);
            map.snowGrid.SetDepth(cell, 0.40f);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            // 0.40 and 0.42 are both in step 6 (0.375..0.4375) and both Thin.
            map.snowGrid.AddDepth(cell, 0.02f);
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.Null(delta.Snow);
            Assert.Equal(held.Snow, delta.Versions.Snow);
        }

        [Fact]
        public void Snowfall_crossing_a_step_moves_the_version_and_the_delta_carries_the_depths()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(4, 0, 7);
            map.snowGrid.SetDepth(cell, 0.40f);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            map.snowGrid.AddDepth(cell, 0.05f); // 0.45: into step 7, still Thin
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.NotEqual(held.Snow, delta.Versions.Snow);
            Assert.NotNull(delta.Snow);
            Assert.Equal(delta.Versions.Snow, delta.Snow!.Version);
            Assert.Equal(map.Size.x, delta.Snow.SizeX);
            Assert.Equal(map.Size.z, delta.Snow.SizeZ);
            Assert.Equal(map.cellIndices.NumGridCells, delta.Snow.Depths.Count);
            Assert.Equal(map.snowGrid.GetDepth(cell), delta.Snow.DepthAt(4, 7));
            Assert.Equal(0f, delta.Snow.DepthAt(0, 0));
            Assert.Null(delta.Terrain);
            Assert.Empty(delta.ChangedChunks);
        }

        [Fact]
        public void Real_snowfall_moves_the_version()
        {
            CoreMap map = NewMap();
            map.outdoorTemperature = -10f;
            WeatherDef storm = DefDatabase<WeatherDef>.GetNamed("SnowHard");
            map.weatherManager.TransitionTo(storm);
            map.weatherManager.TransitionTo(storm);
            map.MapTick(); // floods the rooms; snow only falls on a cell that has one
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            for (int i = 0; i < 2000; i++) map.steadyEnvironmentEffects.SteadyEnvironmentEffectsTick();
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.True(map.snowGrid.TotalDepth > 0f);
            Assert.NotNull(delta.Snow);
            foreach (IntVec3 c in map.AllCells)
            {
                Assert.Equal(map.snowGrid.GetDepth(c), delta.Snow!.DepthAt(c.x, c.z));
            }
        }

        /// <summary>The two halves of the rule, each on its own: a step crossed inside one category, and a
        /// category changed inside one step (0.03, the edge of "no snow", is not a step boundary).</summary>
        [Theory]
        [InlineData(0.10f, 0.15f, true)]   // step 1 -> 2, Dusting both
        [InlineData(0.02f, 0.04f, true)]   // step 0 both, None -> Dusting
        [InlineData(0.26f, 0.30f, false)]  // step 4 both, Thin both
        [InlineData(0.70f, 0.80f, true)]   // step 11 -> 12, Medium -> Thick
        public void The_version_moves_on_a_step_or_a_category(float from, float to, bool moves)
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(2, 0, 2);
            map.snowGrid.SetDepth(cell, from);
            int before = map.mapView.SnowVersion;

            map.snowGrid.SetDepth(cell, to);

            Assert.Equal(moves, map.mapView.SnowVersion != before);
        }

        [Fact]
        public void Clearing_a_cell_moves_the_version()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(5, 0, 5);
            map.snowGrid.SetDepth(cell, 0.6f);
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;

            map.snowGrid.SetDepth(cell, 0f); // what JobDriver_ClearSnow does
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.NotEqual(held.Snow, delta.Versions.Snow);
            Assert.NotNull(delta.Snow);
            Assert.Equal(0f, delta.Snow!.DepthAt(5, 5));
        }

        [Fact]
        public void A_wall_going_up_on_snow_moves_the_version()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(6, 0, 6);
            map.snowGrid.SetDepth(cell, 0.6f);
            int before = map.mapView.SnowVersion;

            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Sandstone")), cell, map);
            map.snowGrid.AddDepth(cell, 0.01f); // the next touch finds it cannot hold snow and zeroes it

            Assert.Equal(0f, map.snowGrid.GetDepth(cell));
            Assert.NotEqual(before, map.mapView.SnowVersion);
        }

        [Fact]
        public void The_snapshot_carries_every_cells_depth()
        {
            CoreMap map = NewMap(13, 9);
            foreach (IntVec3 c in map.AllCells) map.snowGrid.SetDepth(c, ((c.x * 7 + c.z * 3) % 17) / 16f);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);

            Assert.Equal(map.mapView.SnowVersion, snapshot.Snow.Version);
            Assert.Equal(map.mapView.SnowVersion, snapshot.Versions.Snow);
            Assert.Equal(13, snapshot.Snow.SizeX);
            Assert.Equal(9, snapshot.Snow.SizeZ);
            Assert.Equal(13 * 9, snapshot.Snow.Depths.Count);
            foreach (IntVec3 c in map.AllCells)
            {
                float depth = map.snowGrid.GetDepth(c);
                Assert.Equal(depth, snapshot.Snow.DepthAt(c.x, c.z));
                Assert.Equal(depth, snapshot.Snow.Depths[map.cellIndices.CellToIndex(c)]);
            }
        }

        [Fact]
        public void A_snapshot_is_a_copy_not_a_live_view()
        {
            CoreMap map = NewMap();
            var cell = new IntVec3(1, 0, 1);
            map.snowGrid.SetDepth(cell, 0.5f);

            MapViewSnapshot snapshot = MapViewSnapshot.Capture(map);
            map.snowGrid.SetDepth(cell, 0f);

            Assert.Equal(0.5f, snapshot.Snow.DepthAt(1, 1));
        }

        [Fact]
        public void Snow_and_its_view_draw_nothing_from_the_ambient_stream()
        {
            CoreMap map = NewMap();
            uint before = Rand.Current.Iterations;

            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;
            foreach (IntVec3 c in map.AllCells)
            {
                for (int i = 0; i < 40; i++) map.snowGrid.AddDepth(c, 0.003f);
            }
            map.snowGrid.SetDepth(new IntVec3(0, 0, 0), 0f);
            MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);

            Assert.NotNull(delta.Snow);
            Assert.Equal(before, Rand.Current.Iterations);
        }

        [Fact]
        public void The_same_writes_give_the_same_version()
        {
            int Run()
            {
                CoreMap map = NewMap();
                foreach (IntVec3 c in map.AllCells)
                {
                    for (int i = 0; i < 25; i++) map.snowGrid.AddDepth(c, 0.007f * ((c.x + c.z) % 3 + 1));
                }
                return map.mapView.SnowVersion;
            }

            int first = Run();
            Assert.True(first > 1);
            Assert.Equal(first, Run());
        }
    }
}
