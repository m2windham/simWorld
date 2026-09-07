using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// Measurement 2: cost attribution across the five per-pawn trackers. <see cref="Pawn.Tick"/> itself is
    /// never edited — each tracker's own tick method is timed directly in a loop over a fresh pawn set, one
    /// tracker at a time, over one full in-game day. The simulation clock is still advanced for real via
    /// <see cref="TickManager.DoSingleTick"/> each iteration (with no pawns registered on its tick lists, so
    /// that call is cheap) so hash-interval checks inside the trackers see the same ticksGame progression they
    /// would in a real run.
    /// </summary>
    internal static class AttributionSuite
    {
        public static void Run(BenchOptions opt, int pawns)
        {
            Report.Heading("2. Cost attribution (N=" + pawns.ToString(CultureInfo.InvariantCulture) + ")");
            Report.Note(
                "Each row is " + opt.Days.ToString(CultureInfo.InvariantCulture) +
                " in-game day of that tracker's own Tick method only, called directly over a fresh " +
                pawns.ToString(CultureInfo.InvariantCulture) + "-pawn set (median of " + opt.Runs.ToString(CultureInfo.InvariantCulture) +
                ", 1 warmup discarded). Pawn.Tick()'s own overhead (the dead/suspended checks, base.Tick()) is not attributed to any tracker.");

            var trackers = new (string Name, Action<Pawn> Tick)[]
            {
                ("health.HealthTick", p => p.health.HealthTick()),
                ("needs.NeedsTrackerTick", p => p.needs.NeedsTrackerTick()),
                ("mindState.MindStateTick", p => p.mindState.MindStateTick()),
                ("skills.SkillsTick", p => p.skills.SkillsTick()),
                ("ageTracker.AgeTick", p => p.ageTracker.AgeTick()),
            };

            var results = new List<(string Name, TimingResult Timing)>();
            foreach ((string name, Action<Pawn> tick) in trackers)
            {
                TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () => RunOnce(pawns, opt.Days, opt.Seed, tick));
                results.Add((name, timing));
                Console.WriteLine("  " + name.PadRight(24) + Report.Ms(timing.MedianMs));
            }

            double sum = 0;
            foreach ((string _, TimingResult t) in results) sum += t.MedianMs;

            Report.SubHeading("Split");
            var rows = new List<string[]>();
            foreach ((string name, TimingResult t) in results)
            {
                double pct = sum > 0 ? t.MedianMs / sum * 100.0 : 0;
                rows.Add(new[] { name, Report.Num(t.MedianMs, 1) + " ms", Report.Num(pct, 1) + "%" });
            }
            rows.Add(new[] { "**sum**", Report.Num(sum, 1) + " ms", "100%" });
            Report.Table(new[] { "tracker", "median ms/day", "% of attributed total" }, rows);
        }

        private static double RunOnce(int n, int days, int seed, Action<Pawn> tick)
        {
            Bootstrap.ResetSim(seed);
            List<Pawn> pawns = PawnFactory.GenerateColonists(n);
            TickManager tm = Find.TickManager; // tick lists stay empty; DoSingleTick just advances ticksGame

            int ticks = GenDate.TicksPerDay * days;
            return Timing.TimeMs(() =>
            {
                for (int t = 0; t < ticks; t++)
                {
                    tm.DoSingleTick();
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn p = pawns[i];
                        if (p.Dead) continue;
                        tick(p);
                    }
                }
            });
        }
    }
}
