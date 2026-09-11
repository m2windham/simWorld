using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Things;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// What finding an enemy costs, as a function of how many people live here
    /// (<c>docs/perf/attack-targets-cache.md</c>).
    ///
    /// <para/><b>Why a second suite rather than more rows in <see cref="ConstantThinkTreeSuite"/>.</b> That
    /// one answers "what does the constant think tree cost at the Full-tier budget" — one population, the
    /// budget, and the whole tree. This one answers a different question: <i>what shape is the curve</i>. The
    /// claim under test is that a scan without <see cref="AttackTargetsCache"/> is O(population) per call and
    /// therefore O(population squared) per interval across a settlement, so a single N cannot show it — only
    /// a sweep can, and only if the sweep holds everything else fixed while N moves.
    ///
    /// <para/><b>Every table is an A/B in one process.</b> <see cref="AttackTargetFinder.BypassCache"/> is the
    /// escape hatch that restores the pre-cache behaviour, so "before" and "after" are taken on the same box,
    /// in the same run, on the same generated scenario, minutes apart — the discipline
    /// <c>docs/perf/baseline.md</c> §9 set for the pathing A/B, and the only way a ratio between two builds
    /// means anything.
    ///
    /// <para/><b>The pawns are spawned on a real map, at a fixed density.</b> Both halves matter.
    /// <see cref="ScalingSuite"/> generates pawns and never spawns them, which is right for tracker cost and
    /// a lie here — <c>JobGiver_AIFightEnemies</c> returns on its first line for a pawn with no
    /// <see cref="Pawn.Map"/>. And the map side grows as sqrt(N) so cells-per-pawn stays at
    /// <see cref="CellsPerPawn"/>: a scan's cost depends on how many candidates fail the distance test, so
    /// holding N fixed while the map shrinks would measure crowding rather than population.
    /// </summary>
    internal static class AttackTargetScanSuite
    {
        /// <summary>Cells per pawn, held constant across the sweep. 45 is what
        /// <see cref="ConstantThinkTreeSuite"/>'s 150x150 map gives at the Full-tier budget of 500, so the
        /// N=500 row of this sweep is directly comparable with <c>docs/perf/constant-think-tree.md</c>.</summary>
        private const int CellsPerPawn = 45;

        /// <summary>Hostiles on the map for the "under raid" case — a raid, not a second settlement.</summary>
        private const int RaidSize = 20;

        public static void Run(BenchOptions opt)
        {
            Report.Heading("12. Finding an attack target (ai.attack-targets)");
            Report.Note(
                "Citizens spawned on a map sized to hold " + CellsPerPawn.ToString(CultureInfo.InvariantCulture) +
                " cells per pawn, so density is constant and only N moves. Peacetime: one faction, no hostiles. " +
                "Under raid: a second, hostile faction of " + RaidSize.ToString(CultureInfo.InvariantCulture) +
                " pawns on the same map. 'before' sets AttackTargetFinder.BypassCache, which is exactly the " +
                "behaviour that shipped before AttackTargetsCache existed. Seed " +
                opt.Seed.ToString(CultureInfo.InvariantCulture) + ".");

            JitWarmup(opt);
            MeasureScan(opt);
            MeasureEvaluation(opt);
            MeasureTickLoop(opt);
        }

        /// <summary>
        /// Runs the scan and the tree once, untimed and reported nowhere, before the first table is measured.
        /// Without it the very first row of the suite carries the cost of promoting these methods out of
        /// tier-0 JIT and reads several times slower than the identical row measured later — which looks like
        /// a result and is not one.
        /// </summary>
        private static void JitWarmup(BenchOptions opt)
        {
            Scenario scenario = BuildScenario(opt.Seed, 60, hostiles: 4);
            ThinkNode root = ConstantThinkTreeDefOf.HumanlikeConstant.thinkRoot;
            for (int i = 0; i < 40_000; i++)
            {
                Pawn p = scenario.Citizens[i % scenario.Citizens.Count];
                AttackTargetFinder.BestAttackTarget(p, CombatAITuning.TargetAcquireRadius);
                AttackTargetFinder.BestAttackTargetUncached(p, CombatAITuning.TargetAcquireRadius);
                root.TryIssueJobPackage(p);
            }
        }

        // ---- 1: the scan itself ----

        private static void MeasureScan(BenchOptions opt)
        {
            Report.SubHeading("AttackTargetFinder.BestAttackTarget, in isolation");
            Report.Note(
                "ns/call/N divided out is the per-candidate cost of the whole population: flat across N means " +
                "the call is linear in population, collapsing means it has stopped reading population at all.");

            var rows = new List<string[]>();
            foreach (int n in opt.TargetNs)
            {
                rows.Add(ScanRow(opt, n, hostiles: 0, "peacetime"));
                rows.Add(ScanRow(opt, n, hostiles: RaidSize, "under raid"));
            }
            Report.Table(
                new[] { "N", "case", "before ns/call", "after ns/call", "speedup", "before ns/call/N", "after ns/call/N" },
                rows);
        }

        private static string[] ScanRow(BenchOptions opt, int n, int hostiles, string label)
        {
            double before = ScanNs(opt, n, hostiles, bypassCache: true);
            double after = ScanNs(opt, n, hostiles, bypassCache: false);
            return new[]
            {
                Report.Int(n),
                label,
                Report.Num(before, 1),
                Report.Num(after, 1),
                Report.Num(before / after, 1) + "x",
                Report.Num(before / n, 3),
                Report.Num(after / n, 3),
            };
        }

        private static double ScanNs(BenchOptions opt, int n, int hostiles, bool bypassCache)
        {
            int calls = CallsFor(n, bypassCache);
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                Scenario scenario = BuildScenario(opt.Seed, n, hostiles);
                List<Pawn> citizens = scenario.Citizens;
                var radii = new float[citizens.Count];
                for (int i = 0; i < citizens.Count; i++) radii[i] = CombatPostureUtility.TargetAcquireRadiusFor(citizens[i]);
                AttackTargetFinder.BypassCache = bypassCache;
                try
                {
                    return Timing.TimeMs(() =>
                    {
                        for (int i = 0; i < calls; i++)
                        {
                            int k = i % citizens.Count;
                            AttackTargetFinder.BestAttackTarget(citizens[k], radii[k]);
                        }
                    });
                }
                finally
                {
                    AttackTargetFinder.BypassCache = false;
                }
            });
            return timing.MedianMs * 1_000_000.0 / calls;
        }

        // ---- 2: the whole constant-tree evaluation, for comparability ----

        private static void MeasureEvaluation(BenchOptions opt)
        {
            Report.SubHeading("One constant-think-tree evaluation");
            Report.Note(
                "The same sweep through the tree that actually calls the scan, so these rows line up with " +
                "docs/perf/constant-think-tree.md. ms/tick is the arithmetic consequence at the shipped " +
                "cadence: every pawn evaluates once per ConstantThinkTreeTuning.IntervalTicks.");

            var rows = new List<string[]>();
            foreach (int n in opt.TargetNs)
            {
                rows.Add(EvaluationRow(opt, n, hostiles: 0, "peacetime"));
                rows.Add(EvaluationRow(opt, n, hostiles: RaidSize, "under raid"));
            }
            Report.Table(
                new[] { "N", "case", "before ns", "after ns", "before ms/tick", "after ms/tick" },
                rows);
        }

        private static string[] EvaluationRow(BenchOptions opt, int n, int hostiles, string label)
        {
            double before = EvaluationNs(opt, n, hostiles, bypassCache: true);
            double after = EvaluationNs(opt, n, hostiles, bypassCache: false);
            double perTick = n / 1_000_000.0 / ConstantThinkTreeTuning.IntervalTicks;
            return new[]
            {
                Report.Int(n),
                label,
                Report.Num(before, 1),
                Report.Num(after, 1),
                Report.Num(before * perTick, 4),
                Report.Num(after * perTick, 4),
            };
        }

        private static double EvaluationNs(BenchOptions opt, int n, int hostiles, bool bypassCache)
        {
            int calls = CallsFor(n, bypassCache);
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                Scenario scenario = BuildScenario(opt.Seed, n, hostiles);
                ThinkNode root = ConstantThinkTreeDefOf.HumanlikeConstant.thinkRoot;
                List<Pawn> citizens = scenario.Citizens;
                AttackTargetFinder.BypassCache = bypassCache;
                try
                {
                    return Timing.TimeMs(() =>
                    {
                        for (int i = 0; i < calls; i++) root.TryIssueJobPackage(citizens[i % citizens.Count]);
                    });
                }
                finally
                {
                    AttackTargetFinder.BypassCache = false;
                }
            });
            return timing.MedianMs * 1_000_000.0 / calls;
        }

        // ---- 3: the whole tick loop, A/B, at each N ----

        private static void MeasureTickLoop(BenchOptions opt)
        {
            Report.SubHeading("The whole tick loop");
            Report.Note(
                opt.ConstantTreeTicks.ToString("N0", CultureInfo.InvariantCulture) + " ticks per trial, peacetime, " +
                "everything else running. 'no constant tree' sets ConstantThinkTreeTuning.IntervalTicksOverride " +
                "to 0; the other two run the shipped 1-in-" +
                ConstantThinkTreeTuning.IntervalTicks.ToString(CultureInfo.InvariantCulture) + " cadence with the " +
                "index bypassed and with it on. This is the end-to-end check on the arithmetic above, and the " +
                "noisiest measurement here — read the isolated tables for the signal and this one only for its " +
                "sign.");

            var rows = new List<string[]>();
            foreach (int n in opt.TargetTickNs)
            {
                double off = TickLoopMs(opt, n, 0, bypassCache: false);
                double before = TickLoopMs(opt, n, ConstantThinkTreeTuning.IntervalTicks, bypassCache: true);
                double after = TickLoopMs(opt, n, ConstantThinkTreeTuning.IntervalTicks, bypassCache: false);
                rows.Add(new[]
                {
                    Report.Int(n),
                    Report.Num(off / opt.ConstantTreeTicks, 4),
                    Report.Num(before / opt.ConstantTreeTicks, 4),
                    Report.Num(after / opt.ConstantTreeTicks, 4),
                    Percent(before, off),
                    Percent(after, off),
                });
            }
            Report.Table(
                new[] { "N", "ms/tick no constant tree", "ms/tick before", "ms/tick after", "before vs. off", "after vs. off" },
                rows);
        }

        private static string Percent(double value, double baseline)
        {
            if (baseline <= 0) return "-";
            double percent = (value / baseline - 1.0) * 100.0;
            return (percent >= 0 ? "+" : "") + Report.Num(percent, 1) + "%";
        }

        private static double TickLoopMs(BenchOptions opt, int n, int interval, bool bypassCache)
        {
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                BuildScenario(opt.Seed, n, hostiles: 0);
                TickManager tm = Find.TickManager;
                ConstantThinkTreeTuning.IntervalTicksOverride = interval;
                AttackTargetFinder.BypassCache = bypassCache;
                try
                {
                    return Timing.TimeMs(() =>
                    {
                        for (int t = 0; t < opt.ConstantTreeTicks; t++) tm.DoSingleTick();
                    });
                }
                finally
                {
                    // Never leave either escape hatch open — a leaked one would silently change what every
                    // later suite in this process measures.
                    ConstantThinkTreeTuning.IntervalTicksOverride = ConstantThinkTreeTuning.IntervalTicks;
                    AttackTargetFinder.BypassCache = false;
                }
            });
            Console.WriteLine(
                "  N=" + n.ToString(CultureInfo.InvariantCulture).PadLeft(5) +
                "  interval=" + interval.ToString(CultureInfo.InvariantCulture).PadLeft(3) +
                "  cache=" + (bypassCache ? "off" : "on ") +
                "  median=" + Report.Ms(timing.MedianMs).PadLeft(12) +
                (timing.GuardTripped ? "  [GUARD TRIPPED]" : ""));
            return timing.MedianMs;
        }

        // ---- scenario ----

        /// <summary>Enough calls that one trial is long enough to time, without letting the largest N run for
        /// minutes. The uncached scan is linear in N, so holding N x calls roughly constant keeps every
        /// "before" trial about the same length; the cached scan is not, so it gets a flat, large call count
        /// instead — a handful of nanoseconds per call needs a lot of calls before a stopwatch can see
        /// it.</summary>
        private static int CallsFor(int n, bool bypassCache) =>
            bypassCache ? Math.Max(20_000, 50_000_000 / Math.Max(n, 1)) : 2_000_000;

        private sealed class Scenario
        {
            public Scenario(CoreMap map, List<Pawn> citizens)
            {
                Map = map;
                Citizens = citizens;
            }

            public CoreMap Map { get; }
            public List<Pawn> Citizens { get; }
        }

        private static Scenario BuildScenario(int seed, int citizenCount, int hostiles)
        {
            Bootstrap.ResetSim(seed);
            Find.FactionManager = new FactionManager();

            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Find.FactionManager.Add(ours);
            Faction? theirs = null;
            if (hostiles > 0)
            {
                theirs = new Faction(DefDatabase<FactionDef>.GetNamed("TribalCivilization"), "Theirs", "F_Theirs");
                Find.FactionManager.Add(theirs);
                ours.SetRelationDirect(theirs, FactionRelationKind.Hostile, -100);
            }

            int total = citizenCount + hostiles;
            int side = Math.Max(16, (int)Math.Ceiling(Math.Sqrt((double)CellsPerPawn * total)));
            var map = new CoreMap(side, side, SimWorld.Map.TerrainDefOf.Soil);
            TickManager tm = Find.TickManager;
            var citizens = new List<Pawn>(citizenCount);

            int stride = Math.Max(1, (int)Math.Floor(side / Math.Sqrt(total + 1)));
            int perRow = Math.Max(1, side / stride);
            for (int i = 0; i < total; i++)
            {
                bool hostile = i >= citizenCount;
                var request = new PawnGenerationRequest(
                    PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, faction: hostile ? theirs : ours);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                pawn.faction = hostile ? theirs : ours;

                int x = 1 + i % perRow * stride;
                int z = 1 + i / perRow * stride;
                GenSpawn.Spawn(pawn, new IntVec3(Math.Min(x, side - 2), 0, Math.Min(z, side - 2)), map);
                tm.RegisterAllTickabilityFor(pawn);
                if (!hostile) citizens.Add(pawn);
            }

            return new Scenario(map, citizens);
        }
    }
}
