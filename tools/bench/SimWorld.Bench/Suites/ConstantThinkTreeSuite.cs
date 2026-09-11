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
    /// What the constant think tree costs at the Full-tier budget (<c>docs/perf/constant-think-tree.md</c>,
    /// <c>docs/status.json</c>'s <c>ai.interrupts</c>).
    ///
    /// <para/><b>Why this cannot reuse <see cref="ScalingSuite"/>.</b> That suite generates pawns and never
    /// spawns them, which is the right call for measuring tracker cost — but the constant tree's only job
    /// giver bails on its first line for a pawn with no <see cref="Pawn.Map"/>, so measured there it would
    /// cost nothing and the number would be a lie. Everything here happens on a real map, at a real
    /// settlement density, with the pawns really pathing and really working.
    ///
    /// <para/>Two measurements, because they answer different questions:
    /// <list type="number">
    /// <item><b>One evaluation, in isolation.</b> What a single pass of the tree costs a single pawn, on a map
    /// that already holds a full Full-tier budget of pawns — because
    /// <see cref="AttackTargetFinder.BestAttackTarget"/> walks the map's pawn list, so the cost of one
    /// evaluation is a function of the population, not a constant. From this, the per-tick cost at any cadence
    /// is arithmetic.</item>
    /// <item><b>The whole tick loop, A/B.</b> The same population ticked with the tree off, at the shipped
    /// interval, and every tick — the end-to-end number, including everything the arithmetic above cannot see
    /// (the interrupts that actually fire, the jobs they end, the re-paths that follow).</item>
    /// </list>
    /// </summary>
    internal static class ConstantThinkTreeSuite
    {
        /// <summary>Map side in cells. 150x150 = 22,500 cells, about 45 per citizen at the Full-tier budget —
        /// a town with room to walk around in rather than a crowd in a box, since pathing and the target scan
        /// both read the map's shape.</summary>
        private const int MapSide = 150;

        public static void Run(BenchOptions opt)
        {
            Report.Heading("11. The constant think tree (ai.interrupts)");
            Report.Note(
                "N = TieringTuning.FullTierBudget (" + TieringTuning.FullTierBudget.ToString("N0", CultureInfo.InvariantCulture) +
                "), spawned on a " + MapSide + "x" + MapSide + " map. Peacetime: one faction, no hostiles — the state a " +
                "settlement spends its life in, and the one where the constant tree is pure overhead. A run is cut off if " +
                "any single trial exceeds the " + opt.GuardSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s guard.");

            int n = TieringTuning.FullTierBudget;
            MeasureOneEvaluation(opt, n);
            MeasureTickLoop(opt, n);
        }

        // ---- 1: one evaluation, in isolation ----

        private static void MeasureOneEvaluation(BenchOptions opt, int n)
        {
            Report.SubHeading("One constant-think-tree evaluation");

            var rows = new List<string[]>();
            rows.Add(EvaluationRow(opt, n, hostiles: 0, "peacetime (no hostile on the map)"));
            rows.Add(EvaluationRow(opt, n, hostiles: 20, "under raid (20 hostiles)"));

            Report.Table(new[] { "case", "ns/evaluation", "ms/tick at every tick", "ms/tick at the shipped interval" }, rows);
        }

        private static string[] EvaluationRow(BenchOptions opt, int n, int hostiles, string label)
        {
            const int evaluations = 200_000;
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                Scenario scenario = BuildScenario(opt.Seed, n, hostiles);
                ThinkNode root = ConstantThinkTreeDefOf.HumanlikeConstant.thinkRoot;
                List<Pawn> citizens = scenario.Citizens;
                return Timing.TimeMs(() =>
                {
                    for (int i = 0; i < evaluations; i++) root.TryIssueJobPackage(citizens[i % citizens.Count]);
                });
            });

            double nsPerEvaluation = timing.MedianMs * 1_000_000.0 / evaluations;
            double msPerTickEveryTick = nsPerEvaluation * n / 1_000_000.0;
            return new[]
            {
                label,
                Report.Num(nsPerEvaluation, 1),
                Report.Num(msPerTickEveryTick, 4),
                Report.Num(msPerTickEveryTick / ConstantThinkTreeTuning.IntervalTicks, 4),
            };
        }

        // ---- 2: the whole tick loop, A/B ----

        private static void MeasureTickLoop(BenchOptions opt, int n)
        {
            Report.SubHeading("The whole tick loop, A/B");
            Report.Note(
                "Ticks measured per trial: " + opt.ConstantTreeTicks.ToString("N0", CultureInfo.InvariantCulture) + " (" +
                Report.Num(opt.ConstantTreeTicks / (double)GenDate.TicksPerDay, 3) + " in-game days). 'off' sets " +
                "ConstantThinkTreeTuning.IntervalTicksOverride to 0 — exactly the behaviour before ai.interrupts, " +
                "a think tree consulted only when a pawn has nothing to do.");

            double off = TickLoopMs(opt, n, 0);
            double shipped = TickLoopMs(opt, n, ConstantThinkTreeTuning.IntervalTicks);
            double everyTick = TickLoopMs(opt, n, 1);

            Report.Table(
                new[] { "cadence", "median ms", "ms/tick", "vs. off" },
                new[]
                {
                    TickLoopRow("off (pre-ai.interrupts)", off, off, opt.ConstantTreeTicks),
                    TickLoopRow("every " + ConstantThinkTreeTuning.IntervalTicks.ToString(CultureInfo.InvariantCulture) + " ticks (shipped)", shipped, off, opt.ConstantTreeTicks),
                    TickLoopRow("every tick", everyTick, off, opt.ConstantTreeTicks),
                });
        }

        private static string[] TickLoopRow(string label, double ms, double baselineMs, int ticks)
        {
            double percent = baselineMs > 0 ? (ms / baselineMs - 1.0) * 100.0 : 0.0;
            return new[]
            {
                label,
                Report.Num(ms, 1),
                Report.Num(ms / ticks, 4),
                baselineMs > 0 ? (percent >= 0 ? "+" : "") + Report.Num(percent, 1) + "%" : "-",
            };
        }

        private static double TickLoopMs(BenchOptions opt, int n, int interval)
        {
            TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
            {
                Scenario scenario = BuildScenario(opt.Seed, n, hostiles: 0);
                TickManager tm = Find.TickManager;
                ConstantThinkTreeTuning.IntervalTicksOverride = interval;
                try
                {
                    return Timing.TimeMs(() =>
                    {
                        for (int t = 0; t < opt.ConstantTreeTicks; t++) tm.DoSingleTick();
                    });
                }
                finally
                {
                    // Never leave the escape hatch open: every other suite in this process must see the
                    // shipped cadence, and a leaked override would silently change what they measure.
                    ConstantThinkTreeTuning.IntervalTicksOverride = ConstantThinkTreeTuning.IntervalTicks;
                }
            });
            Console.WriteLine(
                "  interval=" + interval.ToString(CultureInfo.InvariantCulture).PadLeft(3) +
                "  median=" + Report.Ms(timing.MedianMs).PadLeft(12) +
                (timing.GuardTripped ? "  [GUARD TRIPPED]" : ""));
            return timing.MedianMs;
        }

        // ---- scenario ----

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

        /// <summary>A settlement's worth of colonists spread evenly over a fresh map, plus optional hostiles
        /// of a second, enemy faction. Registered for ticking; nothing is ticked here.</summary>
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

            var map = new CoreMap(MapSide, MapSide, SimWorld.Map.TerrainDefOf.Soil);
            TickManager tm = Find.TickManager;
            var citizens = new List<Pawn>(citizenCount);

            int stride = Math.Max(1, (int)Math.Floor(MapSide / Math.Sqrt(citizenCount + hostiles + 1)));
            int placed = 0;
            for (int i = 0; i < citizenCount + hostiles; i++)
            {
                bool hostile = i >= citizenCount;
                var request = new PawnGenerationRequest(
                    PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, faction: hostile ? theirs : ours);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                pawn.faction = hostile ? theirs : ours;

                int x = 1 + placed % (MapSide / stride) * stride;
                int z = 1 + placed / (MapSide / stride) * stride;
                GenSpawn.Spawn(pawn, new IntVec3(Math.Min(x, MapSide - 2), 0, Math.Min(z, MapSide - 2)), map);
                tm.RegisterAllTickabilityFor(pawn);
                if (!hostile) citizens.Add(pawn);
                placed++;
            }

            return new Scenario(map, citizens);
        }
    }
}
