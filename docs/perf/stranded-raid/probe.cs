// lane/stranded private probe. Runs the storyteller bench's arm (same setup) with raid instrumentation.
// usage: StrandedProbe <seed> <days> <solo:true|false> [noisy]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SimWorld.AI;
using SimWorld.AI.Group;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;

namespace StrandedProbe
{
    internal static class Program
    {
        sealed class RaidTrack
        {
            public Lord Lord = null!;
            public int Id;
            public int StartTick;
            public string Faction = "";
            public int Count;
            public List<IntVec3> Arrival = new();
            public bool EverClose;        // any raider within 45 cells of a living player pawn
            public int FirstCloseTick = -1;
            public bool EverAttacked;     // any raider ever held a non-wander job whose def is an attack
            public int NoJobPawnTicks;    // sampled (every 25 ticks) pawn-samples with no job
            public int Samples;
            public int EndTick = -1;
            public string EndWhy = "";
            public int ReachableAtArrival = -1; // 1/0: raider region connected to a player pawn's region
            public string LastToil = "";
            public int ToilExitTick = -1;
        }

        static int Main(string[] args)
        {
            int seed = int.Parse(args[0], CultureInfo.InvariantCulture);
            int days = int.Parse(args[1], CultureInfo.InvariantCulture);
            bool solo = bool.Parse(args[2]);
            bool noisy = args.Length > 3 && args[3] == "noisy";
            double dumpDay = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : -1;
            bool dumped = false;

            var database = new DefDatabase();
            DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true });
            if (!result.Success) { foreach (var e in result.Errors) Console.Error.WriteLine(e); return 2; }
            DefDatabase.Global = database;

            Find.Reset();
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
            SimWorld.Map.Map.ResetMapIdCounter();
            Ablation.Clear();

            var sw = Stopwatch.StartNew();
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed.ToString(CultureInfo.InvariantCulture),
                subdivisionOverride: 3, soloStart: solo, bandSize: 25);
            SimWorld.World.Settlement home = game.CivilizationTarget.Seat!;
            GodCommands.OpenSettlement(home.tile);
            SimWorld.Map.Map map = home.InteriorMap!;
            Console.WriteLine($"# probe seed={seed} days={days} solo={solo} map={map.Size.x}x{map.Size.z}");

            var tracks = new List<RaidTrack>();
            var byLord = new Dictionary<Lord, RaidTrack>();
            long totalNoJobSamples = 0, totalSamples = 0;
            var dayWatch = Stopwatch.StartNew();
            var perDay = new List<(int day, double secs, int idleRaiders, int raiders)>();
            long ticks = 0;

            for (int day = 1; day <= days; day++)
            {
                int idleSeen = 0, raiderSeen = 0;
                for (int i = 0; i < 60000; i++)
                {
                    game.TickManager.DoSingleTick();
                    ticks++;
                    int now = Find.TickManager.TicksGame;
                    if (dumpDay > 0 && !dumped && now >= dumpDay * 60000)
                    {
                        dumped = true;
                        Dump(map, tracks);
                        return 0;
                    }

                    // new lords: look every tick (cheap: a handful of lords)
                    var lords = map.lordManager.lords;
                    for (int l = 0; l < lords.Count; l++)
                    {
                        Lord lord = lords[l];
                        if (byLord.ContainsKey(lord)) continue;
                        var t = new RaidTrack { Lord = lord, Id = tracks.Count + 1, StartTick = now, Faction = lord.faction?.name ?? "?", Count = lord.ownedPawns.Count };
                        for (int p = 0; p < lord.ownedPawns.Count; p++) t.Arrival.Add(lord.ownedPawns[p].Position);
                        // is the arrival reachable from the settlement? (region connectivity from a player pawn)
                        t.ReachableAtArrival = ConnectedToColony(map, lord.ownedPawns[0].Position) ? 1 : 0;
                        tracks.Add(t); byLord[lord] = t;
                        Console.WriteLine($"day {now / 60000.0:F2} RAID#{t.Id} {t.Faction} x{t.Count} arrive@{string.Join(" ", t.Arrival.Distinct().Take(4))} connectedToColony={t.ReachableAtArrival}");
                    }

                    if (now % 25 == 0)
                    {
                        for (int k = 0; k < tracks.Count; k++)
                        {
                            RaidTrack t = tracks[k];
                            if (t.EndTick >= 0) continue;
                            bool alive = map.lordManager.lords.Contains(t.Lord);
                            if (!alive)
                            {
                                t.EndTick = now;
                                t.EndWhy = "lord dissolved";
                                Console.WriteLine($"day {now / 60000.0:F2} RAID#{t.Id} lord gone (last toil {t.LastToil})");
                                continue;
                            }
                            string toil = t.Lord.CurLordToil?.ToString() ?? "none";
                            if (toil != t.LastToil)
                            {
                                Console.WriteLine($"day {now / 60000.0:F2} RAID#{t.Id} toil -> {toil} ({t.Lord.ownedPawns.Count} pawns, ticksInToil {t.Lord.ticksInToil})");
                                t.LastToil = toil;
                            }
                            for (int p = 0; p < t.Lord.ownedPawns.Count; p++)
                            {
                                Pawn pawn = t.Lord.ownedPawns[p];
                                if (!pawn.Spawned) continue;
                                t.Samples++; totalSamples++;
                                bool noJob = pawn.jobs.curJob == null;
                                if (noJob) { t.NoJobPawnTicks++; totalNoJobSamples++; idleSeen++; }
                                raiderSeen++;
                                string jn = pawn.jobs.curJob?.def.defName ?? "";
                                if (jn.Contains("Attack")) t.EverAttacked = true;
                                if (!t.EverClose && NearestPlayerPawnDist(map, pawn) <= 45f) { t.EverClose = true; t.FirstCloseTick = now; }
                            }
                            if (noisy && now % 2500 == 0)
                            {
                                var sb = string.Join(" | ", t.Lord.ownedPawns.Select(p => $"{p.Position}:{p.jobs.curJob?.def.defName ?? "NONE"}:{p.mindState.duty?.defName}"));
                                Console.WriteLine($"   day {now / 60000.0:F2} RAID#{t.Id} {toil} {sb}");
                            }
                        }
                    }
                }
                perDay.Add((day, dayWatch.Elapsed.TotalSeconds, idleSeen, raiderSeen));
                dayWatch.Restart();
            }
            sw.Stop();
            Console.WriteLine();
            Console.WriteLine($"## seed {seed}: {ticks} ticks in {sw.Elapsed.TotalSeconds:F1}s = {ticks / sw.Elapsed.TotalSeconds:F0} ticks/s");
            Console.WriteLine("day secs | raider-samples no-job/total (sampled every 25 ticks)");
            foreach (var d in perDay) if (d.secs > 8 || d.raiders > 0) Console.WriteLine($"  day {d.day,2}: {d.secs,6:F1}s  idle {d.idleRaiders}/{d.raiders}");
            Console.WriteLine();
            Console.WriteLine("raid | day | faction | n | arrival cells | connected | everClose | everAttacked | firstClose(day) | end | noJob% ");
            foreach (var t in tracks)
            {
                Console.WriteLine($"{t.Id} | {t.StartTick / 60000.0:F2} | {t.Faction} | {t.Count} | {string.Join(" ", t.Arrival.Distinct().Take(3))} | {t.ReachableAtArrival} | {t.EverClose} | {t.EverAttacked} | {(t.FirstCloseTick < 0 ? "-" : (t.FirstCloseTick / 60000.0).ToString("F2"))} | {(t.EndTick < 0 ? "STILL RUNNING" : (t.EndTick / 60000.0).ToString("F2") + " " + t.EndWhy)} | {(t.Samples == 0 ? 0 : 100.0 * t.NoJobPawnTicks / t.Samples):F0}%");
            }
            var player = Find.FactionManager.OfPlayer;
            int pop = map.mapPawns.AllPawns.Count(p => p.faction == player && p.RaceProps.Humanlike && !p.Dead);
            Console.WriteLine($"end: player humanlikes on map {pop}; total raider samples idle {totalNoJobSamples}/{totalSamples}");
            return 0;
        }

        static void Dump(SimWorld.Map.Map map, List<RaidTrack> tracks)
        {
            map.regionAndRoomUpdater.RebuildIfNeeded();
            foreach (var t in tracks)
            {
                Console.WriteLine($"DUMP RAID#{t.Id} lord={t.Lord} toil={t.Lord.CurLordToil} ticksInToil={t.Lord.ticksInToil} everGained={t.Lord.numPawnsEverGained} lostViolently={t.Lord.numPawnsLostViolently} alive={map.lordManager.lords.Contains(t.Lord)}");
                foreach (var p in t.Lord.ownedPawns)
                {
                    IntVec3 c = p.Position;
                    Region? r = map.regionGrid.RegionAt(c);
                    Console.WriteLine($"  pawn {p.GetUniqueLoadID()} at {c} spawned={p.Spawned} dead={p.Dead} downed={p.Downed} job={(p.jobs.curJob?.def.defName ?? "NONE")} duty={p.mindState.duty?.defName}");
                    Console.WriteLine($"    walkable={GenGrid.Walkable(c, map)} standable={GenGrid.Standable(c, map)} terrain={map.terrainGrid.TerrainAt(c)?.defName} region={(r == null ? "null" : r.ToString())} roofed={map.roofGrid.Roofed(c)}");
                    var things = map.thingGrid.ThingsListAt(c);
                    Console.WriteLine($"    things here: {string.Join(", ", things.Select(x => x.def.defName + "(" + x.def.passability + ")"))}");
                    Console.WriteLine("    neighbourhood (W=walkable . = not) rows z+3..z-3, x-3..x+3 :");
                    for (int dz = 3; dz >= -3; dz--)
                    {
                        var sb = new System.Text.StringBuilder("      ");
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            var n = new IntVec3(c.x + dx, 0, c.z + dz);
                            sb.Append(!GenGrid.InBounds(n, map) ? '#' : (dx == 0 && dz == 0 ? (GenGrid.Walkable(n, map) ? 'P' : 'p') : GenGrid.Walkable(n, map) ? 'W' : '.'));
                        }
                        Console.WriteLine(sb.ToString());
                    }
                    Console.WriteLine($"    connected to a colonist: {ConnectedToColony(map, c)}  (region cells {(r == null ? 0 : r.Cells.Count)})");
                }
            }
        }

        static bool ConnectedToColony(SimWorld.Map.Map map, IntVec3 cell)
        {
            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region? from = map.regionGrid.RegionAt(cell);
            if (from == null) return false;
            var player = Find.FactionManager.OfPlayer;
            foreach (Pawn p in map.mapPawns.AllPawns)
            {
                if (p.faction != player || p.Dead || !p.Spawned) continue;
                Region? to = map.regionGrid.RegionAt(p.Position);
                if (to != null && RegionTraverser.WithinRegions(from, to)) return true;
            }
            return false;
        }

        static float NearestPlayerPawnDist(SimWorld.Map.Map map, Pawn raider)
        {
            var player = Find.FactionManager.OfPlayer;
            float best = float.MaxValue;
            foreach (Pawn p in map.mapPawns.AllPawns)
            {
                if (p.faction != player || p.Dead || !p.Spawned) continue;
                float d = (p.Position - raider.Position).LengthHorizontal;
                if (d < best) best = d;
            }
            return best;
        }
    }
}
