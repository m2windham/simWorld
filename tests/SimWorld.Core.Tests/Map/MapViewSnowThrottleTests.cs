using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// A storm moves the snow version almost every tick, and a host pulls a delta per frame. Each delta that
    /// carries snow is a new whole-map list (40,000 floats on a 200x200 map), so deltas carry a new layer at
    /// most once per <see cref="MapViewTracker.SnowResendIntervalTicks"/>, and the last change is never lost:
    /// a delta that holds snow back reports the version the host already has, so the change is still owed.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewSnowThrottleTests : ContentTestBase
    {
        public MapViewSnowThrottleTests(CoreContentFixture content) : base(content)
        {
            MapViewTracker.ResetSessionIdCounter();
        }

        private static CoreMap NewMap() => new CoreMap(20, 20, TerrainDefOf.Soil);

        /// <summary>One cell crosses a visual step on every call: the storm's worst case for the version.</summary>
        private static void SnowTick(CoreMap map, int tick)
        {
            var cell = new IntVec3(tick % 20, 0, (tick / 20) % 20);
            map.snowGrid.SetDepth(cell, map.snowGrid.GetDepth(cell) >= 0.5f ? 0.1f : 0.9f);
        }

        [Fact]
        public void A_storm_sends_the_snow_layer_at_most_once_per_interval_and_the_last_change_arrives()
        {
            CoreMap map = NewMap();
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;
            const int stormTicks = 600;
            int sent = 0;

            for (int t = 1; t <= stormTicks; t++)
            {
                Find.TickManager.DebugSetTicksGame(t);
                int before = map.mapView.SnowVersion;
                SnowTick(map, t);
                Assert.NotEqual(before, map.mapView.SnowVersion); // the version itself still moves every tick

                MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);
                if (delta.Snow != null)
                {
                    sent++;
                    Assert.Equal(delta.Versions.Snow, delta.Snow.Version);
                }
                else
                {
                    Assert.Equal(held.Snow, delta.Versions.Snow); // held back, so still owed
                }
                held = delta.Versions;
            }

            Assert.InRange(sent, 2, stormTicks / MapViewTracker.SnowResendIntervalTicks + 1);

            // The storm stops. Within one interval the last change goes out, and it is the grid as it now is.
            MapViewDelta? last = null;
            for (int t = stormTicks + 1; t <= stormTicks + MapViewTracker.SnowResendIntervalTicks; t++)
            {
                Find.TickManager.DebugSetTicksGame(t);
                MapViewDelta delta = MapViewSnapshot.CaptureChanges(map, held);
                held = delta.Versions;
                if (delta.Snow != null) last = delta;
            }

            Assert.NotNull(last);
            Assert.Equal(map.mapView.SnowVersion, held.Snow);
            foreach (IntVec3 c in map.AllCells) Assert.Equal(map.snowGrid.GetDepth(c), last!.Snow!.DepthAt(c.x, c.z));

            // Settled: nothing more is sent.
            Find.TickManager.DebugSetTicksGame(stormTicks + 10 * MapViewTracker.SnowResendIntervalTicks);
            Assert.Null(MapViewSnapshot.CaptureChanges(map, held).Snow);
        }

        /// <summary>The host may hold a layer across later deltas (it applies at most twice a second); what it
        /// holds must still be the snow it was read at.</summary>
        [Fact]
        public void A_layer_the_host_holds_is_never_written_by_a_later_delta()
        {
            CoreMap map = NewMap();
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;
            Find.TickManager.DebugSetTicksGame(1);
            map.snowGrid.SetDepth(new IntVec3(2, 0, 2), 0.9f);
            MapViewDelta first = MapViewSnapshot.CaptureChanges(map, held);
            Assert.NotNull(first.Snow);
            var copy = new List<float>(first.Snow!.Depths);

            held = first.Versions;
            for (int t = 2; t <= 4 * MapViewTracker.SnowResendIntervalTicks; t++)
            {
                Find.TickManager.DebugSetTicksGame(t);
                SnowTick(map, t);
                held = MapViewSnapshot.CaptureChanges(map, held).Versions;
            }

            Assert.Equal(copy, first.Snow.Depths);
        }
    }
}
