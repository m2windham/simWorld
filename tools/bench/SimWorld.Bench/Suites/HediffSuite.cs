using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>Measurement 3: the day-tick cost of a healthy pawn vs. one carrying injuries vs. one carrying
    /// injuries plus an immunizable disease.</summary>
    internal static class HediffSuite
    {
        private static readonly string[] InjuryParts = { "left leg", "left arm", "torso" };

        public static void Run(BenchOptions opt)
        {
            Report.Heading("3. Hediff load (N=" + opt.Pawns.ToString(CultureInfo.InvariantCulture) + ", " +
                opt.Days.ToString(CultureInfo.InvariantCulture) + " day)");

            var variants = new (string Name, Action<Pawn> Load)[]
            {
                ("no hediffs", _ => { }),
                ("3 injuries", AddInjuries),
                ("3 injuries + Flu", p => { AddInjuries(p); AddFlu(p); }),
            };

            var results = new List<(string Name, TimingResult Timing)>();
            foreach ((string name, Action<Pawn> load) in variants)
            {
                TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () => RunOnce(opt.Pawns, opt.Days, opt.Seed, load));
                results.Add((name, timing));
                Console.WriteLine("  " + name.PadRight(20) + Report.Ms(timing.MedianMs));
            }

            double baseline = results[0].Timing.MedianMs;
            Report.SubHeading("Results");
            var rows = new List<string[]>();
            foreach ((string name, TimingResult t) in results)
            {
                double multiplier = baseline > 0 ? t.MedianMs / baseline : double.NaN;
                rows.Add(new[] { name, Report.Num(t.MedianMs, 1) + " ms", Report.Num(multiplier, 2) + "x" });
            }
            Report.Table(new[] { "variant", "median ms/day", "multiplier vs. no hediffs" }, rows);
        }

        private static void AddInjuries(Pawn p)
        {
            HediffDef cut = DefDatabase<HediffDef>.GetNamed("Cut");
            BodyDef? body = p.RaceProps.body;
            foreach (string label in InjuryParts)
            {
                BodyPartRecord? part = body?.GetPartByLabel(label);
                p.health.AddHediff(cut, part);
            }
        }

        private static void AddFlu(Pawn p)
        {
            HediffDef flu = DefDatabase<HediffDef>.GetNamed("Flu");
            p.health.AddHediff(flu);
        }

        private static double RunOnce(int n, int days, int seed, Action<Pawn> load)
        {
            Bootstrap.ResetSim(seed);
            List<Pawn> pawns = PawnFactory.GenerateColonists(n);
            for (int i = 0; i < pawns.Count; i++) load(pawns[i]);

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
