using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Bench.Suites
{
    /// <summary>One measured population size from the path-sharing sweep (baseline.md §9,
    /// <c>docs/status.json</c>'s <c>ai.pathing.sharing</c>).</summary>
    internal readonly struct PathingRow
    {
        public int Pawns { get; }
        public int Rooms { get; }
        public TimingResult Before { get; }
        public TimingResult After { get; }

        public PathingRow(int pawns, int rooms, TimingResult before, TimingResult after)
        {
            Pawns = pawns;
            Rooms = rooms;
            Before = before;
            After = after;
        }

        public double SpeedupX => After.MedianMs > 0 ? Before.MedianMs / After.MedianMs : double.NaN;
    }

    /// <summary>
    /// Measurement 9: N pawns scattered across distinct rooms of a door-connected room grid, all pathing once
    /// to the same destination room — the exact scenario <c>ai.pathing.sharing</c> exists for (many pawns, one
    /// shared destination). "before" runs with <see cref="PathFinder.DisableRegionCorridor"/> set — today's
    /// one-A*-per-pawn behaviour, unchanged since before this pass — and "after" is the shared default. Both
    /// sides use a fresh <see cref="PathFinder"/> per trial (a cold corridor cache) over the same
    /// already-region-mapped map, so "after" pays for exactly one corridor-tree build plus N cheap reads —
    /// what a fresh population converging on a destination in an existing settlement would actually cost, not
    /// an artificially pre-warmed best case.
    /// </summary>
    internal static class PathingSuite
    {
        public static List<PathingRow> Run(BenchOptions opt, int[] populationSizes)
        {
            Report.Heading("9. Path sharing (region-graph corridor cache, ai.pathing.sharing)");
            Report.Note(
                "Each row: N pawns, one per distinct room of a door-connected grid of 3x3 rooms sized to " +
                "comfortably exceed N, all pathing once (PathEndMode.OnCell) to the same destination room " +
                "near the grid's centre. 'before' disables region-graph sharing (today's PathFinder behaviour " +
                "before this pass: one full-map A* per pawn); 'after' is the shared default. A run is cut off " +
                "if any single trial exceeds the " + opt.GuardSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s guard.");

            var rows = new List<PathingRow>();
            foreach (int n in populationSizes)
            {
                int roomsPerSide = RoomsPerSideFor(n);
                CoreMap map = BuildConnectedRoomGrid(roomsPerSide, RoomSize, out IntVec3[,] centers);
                map.regionAndRoomUpdater.RebuildIfNeeded(); // the one-time region flood fill, paid once, outside every timed trial
                IntVec3 dest = centers[roomsPerSide / 2, roomsPerSide / 2];

                (List<Pawn> pawns, List<IntVec3> starts) = PlacePawns(map, centers, roomsPerSide, n);

                TimingResult before = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs,
                    () => RunOnceMs(map, pawns, starts, dest, disableSharing: true));
                TimingResult after = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs,
                    () => RunOnceMs(map, pawns, starts, dest, disableSharing: false));

                var row = new PathingRow(n, roomsPerSide * roomsPerSide, before, after);
                rows.Add(row);
                Console.WriteLine(
                    "  N=" + n.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                    "  rooms=" + row.Rooms.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                    "  before=" + Report.Ms(before.MedianMs).PadLeft(12) +
                    "  after=" + Report.Ms(after.MedianMs).PadLeft(12) +
                    "  " + row.SpeedupX.ToString("N2", CultureInfo.InvariantCulture).PadLeft(8) + "x" +
                    (before.GuardTripped ? "  [BEFORE GUARD TRIPPED]" : "") +
                    (after.GuardTripped ? "  [AFTER GUARD TRIPPED]" : ""));
                if (before.GuardTripped && after.GuardTripped)
                {
                    Console.WriteLine("  -- stopping the N sweep: both sides tripped the guard at N=" + n.ToString(CultureInfo.InvariantCulture) + ".");
                    break;
                }
            }

            Report.SubHeading("Results");
            Report.Table(
                new[] { "N", "rooms", "before (ms)", "after (ms)", "speedup" },
                RowsToTable(rows));

            return rows;
        }

        private const int RoomSize = 3;

        /// <summary>Rooms per side of the grid, sized so total rooms comfortably exceeds N (each pawn gets its
        /// own room) without building a needlessly huge map for small N.</summary>
        private static int RoomsPerSideFor(int n)
        {
            int minRooms = n + 4; // +4: destination room plus a little slack
            int side = (int)Math.Ceiling(Math.Sqrt(minRooms));
            return Math.Max(4, side);
        }

        private static double RunOnceMs(CoreMap map, List<Pawn> pawns, List<IntVec3> starts, IntVec3 dest, bool disableSharing)
        {
            var pf = new PathFinder(map) { DisableRegionCorridor = disableSharing };
            return Timing.TimeMs(() =>
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    PawnPath p = pf.FindPath(pawns[i], starts[i], dest, PathEndMode.OnCell);
                    p.ReleaseToPool();
                }
            });
        }

        private static (List<Pawn> pawns, List<IntVec3> starts) PlacePawns(CoreMap map, IntVec3[,] centers, int roomsPerSide, int n)
        {
            int destRx = roomsPerSide / 2, destRy = roomsPerSide / 2;
            int totalRooms = roomsPerSide * roomsPerSide;

            var starts = new List<IntVec3>(n);
            // A fixed stride coprime with most grid sizes spreads picks across the whole grid rather than
            // clustering near room (0,0) — deterministic given roomsPerSide, so the same N always measures
            // the same scenario.
            for (int i = 0; starts.Count < n; i++)
            {
                if (i > totalRooms * 4) throw new InvalidOperationException("population exceeds available rooms for this grid size.");
                int idx = (i * 977) % totalRooms;
                int rx = idx / roomsPerSide, ry = idx % roomsPerSide;
                if (rx == destRx && ry == destRy) continue;
                starts.Add(centers[rx, ry]);
            }

            List<Pawn> pawns = PawnFactory.GenerateColonists(n);
            for (int i = 0; i < n; i++) GenSpawn.Spawn(pawns[i], starts[i], map);
            return (pawns, starts);
        }

        private static IEnumerable<string[]> RowsToTable(List<PathingRow> rows)
        {
            foreach (PathingRow r in rows)
            {
                yield return new[]
                {
                    r.Pawns.ToString(CultureInfo.InvariantCulture),
                    r.Rooms.ToString(CultureInfo.InvariantCulture),
                    Report.Num(r.Before.MedianMs, 1) + (r.Before.GuardTripped ? " (guard)" : ""),
                    Report.Num(r.After.MedianMs, 1) + (r.After.GuardTripped ? " (guard)" : ""),
                    Report.Num(r.SpeedupX, 2) + "x",
                };
            }
        }

        /// <summary>A roomsPerSide x roomsPerSide grid of roomSize x roomSize rooms (same wall rule as
        /// <c>RegionTests</c>'s own room-grid test), each door-connected to its right and lower neighbour so
        /// the whole grid is one connected, many-region, many-door graph.</summary>
        private static CoreMap BuildConnectedRoomGrid(int roomsPerSide, int roomSize, out IntVec3[,] roomCenters)
        {
            int pitch = roomSize + 1;
            int mapSize = roomsPerSide * pitch - 1;
            var map = new CoreMap(mapSize, mapSize, TerrainDefOf.Soil);

            roomCenters = new IntVec3[roomsPerSide, roomsPerSide];
            for (int rx = 0; rx < roomsPerSide; rx++)
            {
                for (int ry = 0; ry < roomsPerSide; ry++)
                {
                    roomCenters[rx, ry] = new IntVec3(rx * pitch + roomSize / 2, 0, ry * pitch + roomSize / 2);
                }
            }

            var doorCells = new HashSet<IntVec3>();
            for (int rx = 0; rx < roomsPerSide; rx++)
            {
                for (int ry = 0; ry < roomsPerSide; ry++)
                {
                    if (rx + 1 < roomsPerSide) doorCells.Add(new IntVec3(rx * pitch + roomSize, 0, ry * pitch + roomSize / 2));
                    if (ry + 1 < roomsPerSide) doorCells.Add(new IntVec3(rx * pitch + roomSize / 2, 0, ry * pitch + roomSize));
                }
            }

            ThingDef wallDef = DefDatabase<ThingDef>.GetNamed("Wall");
            ThingDef doorDef = DefDatabase<ThingDef>.GetNamed("Door");
            for (int x = 0; x < mapSize; x++)
            {
                for (int z = 0; z < mapSize; z++)
                {
                    if (x % pitch != roomSize && z % pitch != roomSize) continue;
                    var c = new IntVec3(x, 0, z);
                    ThingDef def = doorCells.Contains(c) ? doorDef : wallDef;
                    GenSpawn.Spawn(ThingMaker.MakeThing(def), c, map);
                }
            }

            return map;
        }
    }
}
