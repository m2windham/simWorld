using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SimWorld.Bench
{
    /// <summary>One configuration's timing outcome: the median of the measured runs, every sample taken, and
    /// whether a run was cut off by <see cref="Timing.Run"/>'s wall-clock guard.</summary>
    internal readonly struct TimingResult
    {
        public double MedianMs { get; }
        public IReadOnlyList<double> SamplesMs { get; }
        public bool GuardTripped { get; }

        public TimingResult(double medianMs, IReadOnlyList<double> samplesMs, bool guardTripped)
        {
            MedianMs = medianMs;
            SamplesMs = samplesMs;
            GuardTripped = guardTripped;
        }
    }

    /// <summary>Runs one warmup (discarded) plus N measured trials of an action, reporting the median.</summary>
    internal static class Timing
    {
        /// <summary>
        /// Runs <paramref name="warmupRuns"/> discarded trials then up to <paramref name="measuredRuns"/> measured
        /// ones, each timed by <paramref name="runOnceMs"/> (which must do its own setup and return elapsed
        /// milliseconds for the part under measurement). If any single trial — warmup included — exceeds
        /// <paramref name="guardMs"/>, no further trials run and <see cref="TimingResult.GuardTripped"/> is set;
        /// the median is then taken over whatever measured samples were collected before the trip (possibly zero,
        /// in which case <see cref="TimingResult.MedianMs"/> is the lone over-budget sample so callers still have
        /// a number to report).
        /// </summary>
        public static TimingResult Run(int warmupRuns, int measuredRuns, double guardMs, Func<double> runOnceMs)
        {
            var samples = new List<double>(measuredRuns);
            bool tripped = false;
            int total = warmupRuns + measuredRuns;
            for (int i = 0; i < total; i++)
            {
                double ms = runOnceMs();
                bool isMeasured = i >= warmupRuns;
                if (isMeasured) samples.Add(ms);
                if (ms > guardMs)
                {
                    tripped = true;
                    if (!isMeasured) samples.Add(ms); // the guard tripped during warmup: keep it, it's all we have
                    break;
                }
            }
            samples.Sort();
            double median = samples.Count == 0 ? double.NaN : samples[samples.Count / 2];
            return new TimingResult(median, samples, tripped);
        }

        public static double TimeMs(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
    }
}
