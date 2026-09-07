using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>Measurement 6: Scribe save-to-string and load-from-string time and size for a pawn population.</summary>
    internal static class SaveLoadSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("6. Save/load (N=" + opt.Pawns.ToString(CultureInfo.InvariantCulture) + ")");

            Bootstrap.ResetSim(opt.Seed);
            List<Pawn> pawns = PawnFactory.GenerateColonists(opt.Pawns);
            var holder = new SaveHolder { pawns = pawns };

            string? xml = null;
            TimingResult saveTiming = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () =>
                Timing.TimeMs(() => xml = Scribe.SaveToString(holder, "bench")));

            long bytes = Encoding.UTF8.GetByteCount(xml!);

            TimingResult loadTiming = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () => Timing.TimeMs(() =>
            {
                Pawn.ResetThingIdCounter();
                _ = Scribe.Load<SaveHolder>(xml!, "bench", out IReadOnlyList<string> errors);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(errors.Count + " Scribe load error(s): " + string.Join("; ", errors));
                }
            }));

            Report.SubHeading("Results");
            Report.Table(
                new[] { "metric", "value" },
                new[]
                {
                    new[] { "save", Report.Ms(saveTiming.MedianMs) },
                    new[] { "load", Report.Ms(loadTiming.MedianMs) },
                    new[] { "serialized size", Report.Int(bytes) + " bytes (" + Report.Num(bytes / 1024.0 / 1024.0, 2) + " MB)" },
                    new[] { "bytes/pawn", Report.Num(bytes / (double)opt.Pawns, 0) },
                });
        }
    }
}
