using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// What the <c>Map/View</c> seam costs on a real settlement interior (docs/perf/map-view.md).
    ///
    /// <para/>Cost is the design constraint for this seam, so it is measured rather than argued: a full
    /// capture of a generated interior against the incremental read a host actually runs every frame. The
    /// comparison between the two is the load-bearing number; the absolute milliseconds are this box's.
    /// </summary>
    internal static class MapViewSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("Map/View: settlement interior read model");

            Bootstrap.ResetSim(opt.Seed);
            CoreMap.ResetMapIdCounter();
            MapViewTracker.ResetSessionIdCounter();

            Game game = Game.NewGame(
                ScenarioDefOf.TribalStart.scenario,
                "map-view-bench-" + opt.Seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3,
                soloStart: true,
                bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            GodCommands.OpenSettlement(settlement.tile);
            CoreMap map = settlement.InteriorMap!;

            int rocks = map.listerThings.AllThings.Count(t => t.def.mineable);
            int plants = map.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Plant);
            int nonPawn = map.listerThings.AllThings.Count(t => t.def.category != ThingCategory.Pawn);

            Console.WriteLine(
                "  map=" + map.Size.x + "x" + map.Size.z +
                " cells=" + Report.Int(map.cellIndices.NumGridCells) +
                " chunks=" + Report.Int(map.mapView.ChunkCount) + " (" + MapViewTracker.ChunkSize + " cells square)" +
                " things=" + Report.Int(nonPawn) + " (rock " + Report.Int(rocks) + ", plants " + Report.Int(plants) + ")" +
                " pawns=" + Report.Int(map.mapPawns.AllPawnsSpawned.Count));
            Console.WriteLine();

            var rows = new List<string[]>();

            // 1. The shape that is dead on arrival: everything, every time.
            MapViewSnapshot full = MapViewSnapshot.Capture(map);
            rows.Add(Measure(opt, "full Capture()", () => MapViewSnapshot.Capture(map)));

            // 2. The steady state: nothing moved, so nothing but the pawns comes back.
            MapViewVersions held = full.Versions;
            rows.Add(Measure(opt, "CaptureChanges(), nothing changed", () => MapViewSnapshot.CaptureChanges(map, held)));

            // 3. One wall built: exactly one chunk re-sent.
            rows.Add(Measure(opt, "CaptureChanges(), one chunk dirty", () =>
            {
                map.mapView.Notify_ChunkChangedAt(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2));
                return MapViewSnapshot.CaptureChanges(map, held);
            }));

            // 4. Terrain re-pulled: the expensive branch a floor being laid takes.
            rows.Add(Measure(opt, "CaptureChanges(), terrain dirty", () =>
            {
                map.mapView.Notify_TerrainChanged();
                return MapViewSnapshot.CaptureChanges(map, held);
            }));

            // 5. The one a host actually pays: read the changes once per tick, on a live settlement, holding
            //    the versions from the tick before. This is the whole seam in its steady state — the other
            //    rows are its branches in isolation.
            rows.Add(PerTick(map, game, ticks: 600));

            // 6. And the same span read as one delta, to show what the host is buying by asking every tick
            //    rather than once in a while: a settlement moves a real fraction of its chunks over ten
            //    seconds of game time, and paying for that once per frame instead would be the naive shape.
            MapViewVersions beforeTicks = MapViewSnapshot.Capture(map).Versions;
            for (int i = 0; i < 500; i++) game.TickManager.DoSingleTick();
            MapViewDelta afterTicks = MapViewSnapshot.CaptureChanges(map, beforeTicks);
            Console.WriteLine(
                "  after 500 ticks in one delta: " + afterTicks.ChangedChunks.Count + " of " + map.mapView.ChunkCount +
                " chunks dirty, terrain " + (afterTicks.Terrain == null ? "unchanged" : "re-sent") +
                ", roofs " + (afterTicks.Roofs == null ? "unchanged" : "re-sent"));
            Console.WriteLine();

            Report.SubHeading("Results");
            Report.Table(new[] { "call", "median ms", "allocated" }, rows);
        }

        /// <summary>
        /// The steady state: one <c>CaptureChanges</c> per tick, versions rolled forward each time, exactly as
        /// a host bound to this seam would run it. Reported as a mean rather than a median because the
        /// interesting quantity is what a frame costs <i>on average</i> over a span that includes the rare
        /// expensive frames — a median would report the common cheap frame and hide them.
        /// </summary>
        private static string[] PerTick(CoreMap map, Game game, int ticks)
        {
            MapViewVersions held = MapViewSnapshot.Capture(map).Versions;
            double totalMs = 0;
            long allocated = 0;
            var chunksSent = 0;

            for (int i = 0; i < ticks; i++)
            {
                // The tick itself is outside both meters: what is being measured is the seam's cost, not the
                // simulation's, and a tick allocates far more than a capture does.
                game.TickManager.DoSingleTick();

                MapViewDelta delta = null!;
                long before = GC.GetAllocatedBytesForCurrentThread();
                totalMs += Timing.TimeMs(() => delta = MapViewSnapshot.CaptureChanges(map, held));
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                chunksSent += delta.ChangedChunks.Count;
                held = delta.Versions;
            }

            double meanMs = totalMs / ticks;
            double meanChunks = chunksSent / (double)ticks;

            Console.WriteLine(
                "  " + ("CaptureChanges(), once per tick x" + ticks.ToString(CultureInfo.InvariantCulture)).PadRight(36) +
                " mean=" + Report.Num(meanMs, 3) + " ms" +
                "  allocated=" + Report.Num(allocated / 1024.0 / ticks, 1) + " KiB/tick" +
                "  chunks=" + Report.Num(meanChunks, 2) + "/tick of " + map.mapView.ChunkCount);

            return new[]
            {
                "CaptureChanges(), once per tick (mean over " + ticks.ToString(CultureInfo.InvariantCulture) + ")",
                Report.Num(meanMs, 3) + " ms",
                Report.Num(allocated / 1024.0 / ticks, 1) + " KiB",
            };
        }

        private static string[] Measure(BenchOptions opt, string label, Func<object> call)
        {
            long allocated = 0;
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                object? result = null;
                double ms = Timing.TimeMs(() => result = call());
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(result);
                return ms;
            });

            Console.WriteLine(
                "  " + label.PadRight(36) +
                " median=" + Report.Ms(timing.MedianMs) +
                "  allocated=" + Report.Num(allocated / 1024.0, 0) + " KiB" +
                (timing.GuardTripped ? "  [GUARD TRIPPED]" : ""));

            return new[]
            {
                label,
                Report.Num(timing.MedianMs, 2) + " ms" + (timing.GuardTripped ? " (guard)" : ""),
                Report.Num(allocated / 1024.0, 0) + " KiB",
            };
        }
    }
}
