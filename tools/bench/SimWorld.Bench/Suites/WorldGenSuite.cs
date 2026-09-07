using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.World;
using SimWorld.World.Gen;

namespace SimWorld.Bench.Suites
{
    /// <summary>Measurement 5: world generation time and tile count at a sweep of icosphere subdivisions.</summary>
    internal static class WorldGenSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("5. World generation");

            var rows = new List<string[]>();
            foreach (int subdivision in opt.Subdivisions)
            {
                int tiles = 0;
                TimingResult timing = Timing.Run(opt.Warmup, opt.Runs, opt.GuardMs, () => RunOnce(subdivision, opt.Seed, out tiles));
                Console.WriteLine(
                    "  subdivision=" + subdivision.ToString(CultureInfo.InvariantCulture) +
                    "  tiles=" + tiles.ToString("N0", CultureInfo.InvariantCulture) +
                    "  median=" + Report.Ms(timing.MedianMs) +
                    (timing.GuardTripped ? "  [GUARD TRIPPED]" : ""));
                rows.Add(new[]
                {
                    subdivision.ToString(CultureInfo.InvariantCulture),
                    tiles.ToString("N0", CultureInfo.InvariantCulture),
                    Report.Num(timing.MedianMs, 1) + " ms" + (timing.GuardTripped ? " (guard)" : ""),
                });
                if (timing.GuardTripped)
                {
                    Console.WriteLine("  -- stopping the subdivision sweep: exceeded the guard.");
                    break;
                }
            }

            Report.SubHeading("Results");
            Report.Table(new[] { "subdivision", "tiles", "median ms" }, rows);
        }

        private static double RunOnce(int subdivision, int seed, out int tiles)
        {
            Bootstrap.ResetSim(seed);
            SimWorld.World.World? world = null;
            double ms = Timing.TimeMs(() =>
            {
                world = WorldGenerator.GenerateWorld(
                    "bench-seed-" + seed.ToString(CultureInfo.InvariantCulture),
                    1f,
                    OverallRainfall.Normal,
                    OverallTemperature.Normal,
                    OverallPopulation.Normal,
                    "Bench",
                    subdivision);
            });
            tiles = world!.grid.TilesCount;
            return ms;
        }
    }
}
