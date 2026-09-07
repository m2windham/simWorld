using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>One measured N from the pawn tick scaling sweep (baseline.md measurement 1).</summary>
    internal readonly struct ScalingRow
    {
        public int Pawns { get; }
        public int Days { get; }
        public TimingResult Timing { get; }

        public ScalingRow(int pawns, int days, TimingResult timing)
        {
            Pawns = pawns;
            Days = days;
            Timing = timing;
        }

        /// <summary>Real seconds to simulate one in-game day at this N, run flat out (no speed throttling).</summary>
        public double SecondsPerGameDay => Timing.MedianMs / 1000.0 / Days;

        public double MicrosecondsPerPawnDay => Timing.MedianMs * 1000.0 / (Pawns * (double)Days);
    }

    /// <summary>Measurement 1: ticks N generated colonists through one in-game day and reports where real-time
    /// parity (1x and 15x) breaks.</summary>
    internal static class ScalingSuite
    {
        /// <summary>1 in-game day (60,000 ticks) at 1x speed takes exactly this many real seconds.</summary>
        public const double OneXSecondsPerDay = GenDate.TicksPerDay / (double)GenTicks.TicksPerRealSecond;

        /// <summary>...and at the fastest game speed (15x), this many.</summary>
        public const double FifteenXSecondsPerDay = OneXSecondsPerDay / 15.0;

        public static List<ScalingRow> Run(BenchOptions opt, int[] ns)
        {
            Report.Heading("1. Pawn tick scaling");
            Report.Note(
                "1 in-game day = " + GenDate.TicksPerDay.ToString("N0", CultureInfo.InvariantCulture) +
                " ticks. At 1x that takes " + OneXSecondsPerDay.ToString("N0", CultureInfo.InvariantCulture) +
                " real seconds; at 15x, " + FifteenXSecondsPerDay.ToString("N1", CultureInfo.InvariantCulture) +
                " real seconds. A run is cut off if any single trial exceeds the " +
                opt.GuardSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s guard.");

            var rows = new List<ScalingRow>();
            foreach (int n in ns)
            {
                TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () => RunOnce(n, opt.Days, opt.Seed));
                var row = new ScalingRow(n, opt.Days, timing);
                rows.Add(row);
                Console.WriteLine(
                    "  N=" + n.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                    "  median=" + Report.Ms(timing.MedianMs).PadLeft(12) +
                    "  " + row.MicrosecondsPerPawnDay.ToString("N2", CultureInfo.InvariantCulture).PadLeft(10) + " us/pawn-day" +
                    "  " + row.SecondsPerGameDay.ToString("N2", CultureInfo.InvariantCulture).PadLeft(10) + " s/game-day" +
                    (timing.GuardTripped ? "  [GUARD TRIPPED]" : ""));
                if (timing.GuardTripped)
                {
                    Console.WriteLine("  -- stopping the N sweep: a trial at N=" + n.ToString(CultureInfo.InvariantCulture) + " exceeded the guard.");
                    break;
                }
            }

            Report.SubHeading("Results");
            Report.Table(
                new[] { "N", "median ms/day", "us/pawn-day", "s/game-day flat out", "in-game days/real s flat out" },
                RowsToTable(rows));

            ReportParity(rows, opt.GuardMs);
            return rows;
        }

        private static IEnumerable<string[]> RowsToTable(List<ScalingRow> rows)
        {
            foreach (ScalingRow r in rows)
            {
                yield return new[]
                {
                    r.Pawns.ToString(CultureInfo.InvariantCulture),
                    Report.Num(r.Timing.MedianMs, 1) + (r.Timing.GuardTripped ? " (guard)" : ""),
                    Report.Num(r.MicrosecondsPerPawnDay, 2),
                    Report.Num(r.SecondsPerGameDay, 2),
                    Report.Num(1.0 / r.SecondsPerGameDay, 3),
                };
            }
        }

        private static void ReportParity(List<ScalingRow> rows, double guardMs)
        {
            ScalingRow? lastUnder1x = null, lastUnder15x = null;
            ScalingRow? firstOver1x = null, firstOver15x = null;
            foreach (ScalingRow r in rows)
            {
                if (r.Timing.GuardTripped) continue; // not a clean measurement of steady per-pawn cost
                if (r.SecondsPerGameDay <= OneXSecondsPerDay) lastUnder1x = r; else firstOver1x ??= r;
                if (r.SecondsPerGameDay <= FifteenXSecondsPerDay) lastUnder15x = r; else firstOver15x ??= r;
            }

            Report.SubHeading("Where real-time parity breaks");
            DescribeParity("1x (" + Report.Num(OneXSecondsPerDay, 0) + "s/day budget)", OneXSecondsPerDay, lastUnder1x, firstOver1x, rows);
            DescribeParity("15x (" + Report.Num(FifteenXSecondsPerDay, 1) + "s/day budget)", FifteenXSecondsPerDay, lastUnder15x, firstOver15x, rows);
        }

        private static void DescribeParity(string label, double targetSeconds, ScalingRow? lastUnder, ScalingRow? firstOver, List<ScalingRow> rows)
        {
            if (firstOver.HasValue)
            {
                Console.WriteLine(
                    "- " + label + ": holds through N=" + (lastUnder?.Pawns.ToString(CultureInfo.InvariantCulture) ?? "none tested") +
                    ", breaks by N=" + firstOver.Value.Pawns.ToString(CultureInfo.InvariantCulture) +
                    " (" + Report.Num(firstOver.Value.SecondsPerGameDay, 1) + "s/day).");
                return;
            }
            // Never crossed within the cleanly-measured range: extrapolate from the largest clean sample,
            // clearly labeled as an estimate (assumes the observed per-pawn-day cost holds linearly, which
            // ignores any cache/GC superlinearity that only shows up at larger N).
            ScalingRow? largest = null;
            foreach (ScalingRow r in rows) if (!r.Timing.GuardTripped) largest = r;
            if (largest.HasValue && largest.Value.MicrosecondsPerPawnDay > 0)
            {
                double estimatedN = targetSeconds * 1_000_000.0 / largest.Value.MicrosecondsPerPawnDay;
                Console.WriteLine(
                    "- " + label + ": not reached within the tested range (up to N=" +
                    largest.Value.Pawns.ToString(CultureInfo.InvariantCulture) + "). ESTIMATE (linear extrapolation from the N=" +
                    largest.Value.Pawns.ToString(CultureInfo.InvariantCulture) + " per-pawn-day cost, assumes no further superlinear " +
                    "slowdown): breaks around N~" + Report.Num(estimatedN, 0) + ".");
            }
            else
            {
                Console.WriteLine("- " + label + ": no clean measurement available to extrapolate from.");
            }
        }

        private static double RunOnce(int n, int days, int seed)
        {
            Bootstrap.ResetSim(seed);
            List<Pawn> pawns = PawnFactory.GenerateColonists(n);
            TickManager tm = Find.TickManager;
            for (int i = 0; i < pawns.Count; i++) tm.RegisterAllTickabilityFor(pawns[i]);

            int ticks = GenDate.TicksPerDay * days;
            return Timing.TimeMs(() =>
            {
                for (int t = 0; t < ticks; t++) tm.DoSingleTick();
            });
        }
    }
}
