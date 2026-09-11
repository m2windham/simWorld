using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    /// What <see cref="Pawn.HashOffsetTicks"/>'s phasing costs on the worst tick, before and after it was
    /// changed from <c>thingIDNumber * 3</c> to a hash of the id (<c>docs/perf/hash-phasing.md</c>).
    ///
    /// <para/><b>Why peak and not mean.</b> Phasing moves work between ticks; it never removes any. The mean
    /// cost per tick is identical either way and measuring it would show nothing. What phasing buys is the
    /// height of the spike: with ids handed out consecutively and an offset of <c>id * 3</c>,
    /// <c>gcd(3, interval) == 3</c> for every interval the sim actually uses (30, 60, 150, 600), so pawns
    /// reached only a third of the phases and stood three deep on each. The tick that matters for a frame
    /// budget is the worst one, not the average one.
    ///
    /// <para/><b>Two measurements, because one of them is noise-free.</b>
    /// <list type="number">
    /// <item><b>Phase occupancy.</b> How many pawns fire on the busiest tick of a window, counted rather than
    /// timed. Deterministic, machine-independent, and the thing the fix actually changes.</item>
    /// <item><b>Per-tick wall clock.</b> The same population really ticked, each tick timed separately and
    /// folded into a <see cref="FoldTicks"/>-tick phase profile, so the periodic signal survives the per-tick
    /// noise. Peak, mean and their ratio.</item>
    /// </list>
    ///
    /// <para/><b>Why the fold is 30 and not 600.</b> 30 is the gcd of every hash interval in play, so every
    /// pawn's firing phase is coherent under it — a pawn on the 150-tick needs cadence fires at ticks
    /// congruent to its phase mod 150, which is congruent mod 30 too. Everything else the tick loop does
    /// periodically (map sync at 250, weather at 500, the long-interval pass at 2,000) shares no factor with
    /// 30, so it smears itself evenly across the buckets instead of planting a spike in one of them. Folding
    /// at 600 was tried first and the profile was dominated by exactly that: a peak 25x the mean in both arms,
    /// which is the tick loop's other work, not the phasing.
    /// </summary>
    internal static class HashPhasingSuite
    {
        /// <summary>Same map as <see cref="ConstantThinkTreeSuite"/>: a town with room to walk in, so pathing
        /// and target scans cost what they cost in play.</summary>
        private const int MapSide = 150;

        /// <summary>The lcm of every hash interval the sim uses (30, 60, 150, 600): one measured span of this
        /// length contains a whole number of periods of each, so no interval is over- or under-sampled.</summary>
        private const int CycleTicks = 600;

        /// <summary>The gcd of those same intervals, and so the width of the phase profile: see the class
        /// remarks on why the fold is 30.</summary>
        private const int FoldTicks = 30;

        /// <summary>The intervals whose phase occupancy is counted, with what rides on each.</summary>
        private static readonly (int Interval, string What)[] Intervals =
        {
            (30, "constant think tree / job interrupts"),
            (60, "bleeding"),
            (150, "needs, mental-break checks"),
            (600, "healing"),
        };

        public static void Run(BenchOptions opt)
        {
            Report.Heading("12. Hash-interval phasing (Pawn.HashOffsetTicks)");
            Report.Note(
                "N = TieringTuning.FullTierBudget (" + TieringTuning.FullTierBudget.ToString("N0", CultureInfo.InvariantCulture) +
                "), spawned on a " + MapSide + "x" + MapSide + " map, peacetime. 'before' is the old " +
                "thingIDNumber * 3 offset (HashOffsetTuning.UseLegacyMultiplyOffset); 'after' is the shipped " +
                "hash of the id. Both arms do exactly the same total work — only its distribution over ticks " +
                "differs, so read the peak column, not the mean.");

            int n = TieringTuning.FullTierBudget;
            MeasurePhaseOccupancy(n);
            MeasureTickProfile(opt, n);
        }

        // ---- 1: phase occupancy, counted ----

        private static void MeasurePhaseOccupancy(int n)
        {
            Report.SubHeading("Pawns on the busiest phase (counted, not timed)");
            Report.Note(
                "Offsets are a pure function of thingIDNumber, so these are exact and the same on any machine. " +
                "'phases used' is how many of the interval's ticks any pawn ever fires on; an even spread would " +
                "put N / interval pawns on each.");

            var rows = new List<string[]>();
            foreach ((int interval, string what) in Intervals)
            {
                int[] before = PhaseHistogram(n, interval, legacy: true);
                int[] after = PhaseHistogram(n, interval, legacy: false);
                double even = n / (double)interval;
                rows.Add(new[]
                {
                    interval.ToString(CultureInfo.InvariantCulture) + " (" + what + ")",
                    Report.Num(even, 2),
                    Report.Int(Used(before)) + " / " + Report.Int(interval),
                    Report.Int(Max(before)),
                    Report.Int(Used(after)) + " / " + Report.Int(interval),
                    Report.Int(Max(after)),
                    Report.Num(Max(after) == 0 ? double.NaN : Max(before) / (double)Max(after), 2) + "x",
                });
            }

            Report.Table(
                new[] { "interval", "even share", "phases used (before)", "peak (before)", "phases used (after)", "peak (after)", "peak reduction" },
                rows);
        }

        /// <summary>Pawns per phase for consecutive thing ids, which is how <c>Thing.AllocateThingId</c>
        /// hands them out.</summary>
        private static int[] PhaseHistogram(int n, int interval, bool legacy)
        {
            var histogram = new int[interval];
            for (int id = 0; id < n; id++)
            {
                int offset = legacy ? HashOffsetTuning.LegacyOffsetTicks(id) : Rand.HashInt(id) & int.MaxValue;
                histogram[GenMath.PositiveMod(offset, interval)]++;
            }
            return histogram;
        }

        private static int Used(int[] histogram)
        {
            int used = 0;
            foreach (int count in histogram)
            {
                if (count > 0) used++;
            }
            return used;
        }

        private static int Max(int[] histogram)
        {
            int max = 0;
            foreach (int count in histogram)
            {
                if (count > max) max = count;
            }
            return max;
        }

        // ---- 2: per-tick wall clock, folded into a phase profile ----

        private static void MeasureTickProfile(BenchOptions opt, int n)
        {
            Report.SubHeading("Per-tick cost, folded into the " + FoldTicks + "-tick phase profile");

            int cycles = Math.Max(1, opt.PhasingCycles);
            Report.Note(
                "Cycles per trial: " + cycles.ToString("N0", CultureInfo.InvariantCulture) + " (" +
                (cycles * CycleTicks).ToString("N0", CultureInfo.InvariantCulture) + " ticks, " +
                (cycles * CycleTicks / FoldTicks).ToString("N0", CultureInfo.InvariantCulture) + " samples per " +
                "bucket). Each tick is timed on its own; tick t goes into bucket t % " + FoldTicks + " and the " +
                "bucket's samples are averaged, so the phasing signal survives per-tick timer noise while the " +
                "tick loop's other periodic work smears out. 'peak' is the costliest bucket, 'median' the " +
                "middle one, 'mean' the average over all of them.");

            var rows = new List<string[]>();
            PhaseProfile before = Profile(opt, n, cycles, legacy: true);
            PhaseProfile after = Profile(opt, n, cycles, legacy: false);

            rows.Add(ProfileRow("before (thingIDNumber * 3)", before));
            rows.Add(ProfileRow("after (hashed id)", after));

            Report.Table(new[] { "offset", "mean ms/tick", "median bucket", "peak ms/tick", "peak / mean" }, rows);

            Report.Note(
                "Peak tick: " + Report.Num(before.Peak, 4) + " ms -> " + Report.Num(after.Peak, 4) + " ms (" +
                (after.Peak > 0 ? Report.Num(before.Peak / after.Peak, 2) : "n/a") + "x). Mean tick: " +
                Report.Num(before.Mean, 4) + " ms -> " + Report.Num(after.Mean, 4) + " ms — the same work, as it " +
                "should be.");
        }

        private static string[] ProfileRow(string label, PhaseProfile profile) => new[]
        {
            label,
            Report.Num(profile.Mean, 4),
            Report.Num(profile.Median, 4),
            Report.Num(profile.Peak, 4),
            Report.Num(profile.Mean > 0 ? profile.Peak / profile.Mean : double.NaN, 2),
        };

        private readonly struct PhaseProfile
        {
            public PhaseProfile(double mean, double median, double peak)
            {
                Mean = mean;
                Median = median;
                Peak = peak;
            }

            public double Mean { get; }
            public double Median { get; }
            public double Peak { get; }
        }

        /// <summary>Median over the measured trials of each statistic, so one unlucky trial cannot set the
        /// peak on its own.</summary>
        private static PhaseProfile Profile(BenchOptions opt, int n, int cycles, bool legacy)
        {
            var means = new List<double>();
            var medians = new List<double>();
            var peaks = new List<double>();

            for (int trial = 0; trial < opt.Warmup + opt.Runs; trial++)
            {
                PhaseProfile profile = ProfileOnce(opt, n, cycles, legacy);
                if (trial < opt.Warmup) continue;
                means.Add(profile.Mean);
                medians.Add(profile.Median);
                peaks.Add(profile.Peak);
            }

            Console.WriteLine(
                "  " + (legacy ? "before" : "after ") +
                "  mean=" + Report.Num(Median(means), 4) +
                "  median-bucket=" + Report.Num(Median(medians), 4) +
                "  peak=" + Report.Num(Median(peaks), 4) + " ms/tick");

            return new PhaseProfile(Median(means), Median(medians), Median(peaks));
        }

        private static PhaseProfile ProfileOnce(BenchOptions opt, int n, int cycles, bool legacy)
        {
            HashOffsetTuning.UseLegacyMultiplyOffset = legacy;
            try
            {
                BuildScenario(opt.Seed, n);
                TickManager tm = Find.TickManager;
                var buckets = new double[FoldTicks];
                int samplesPerBucket = cycles * CycleTicks / FoldTicks;
                var sw = new Stopwatch();

                for (int t = 0; t < cycles * CycleTicks; t++)
                {
                    int bucket = t % FoldTicks;
                    sw.Restart();
                    tm.DoSingleTick();
                    sw.Stop();
                    buckets[bucket] += sw.Elapsed.TotalMilliseconds;
                }

                for (int i = 0; i < buckets.Length; i++) buckets[i] /= samplesPerBucket;
                return Summarize(buckets);
            }
            finally
            {
                // Never leave the escape hatch open: every other suite in this process must see the shipped
                // offset, and a leaked override would silently change what they measure.
                HashOffsetTuning.UseLegacyMultiplyOffset = false;
            }
        }

        private static PhaseProfile Summarize(double[] buckets)
        {
            double total = 0;
            foreach (double ms in buckets) total += ms;

            var sorted = (double[])buckets.Clone();
            Array.Sort(sorted);
            return new PhaseProfile(total / buckets.Length, sorted[sorted.Length / 2], sorted[sorted.Length - 1]);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        // ---- scenario ----

        /// <summary>A Full-tier budget of colonists spread over a fresh map and registered for ticking; the
        /// same shape <see cref="ConstantThinkTreeSuite"/> builds, minus the hostiles.</summary>
        private static void BuildScenario(int seed, int citizenCount)
        {
            Bootstrap.ResetSim(seed);
            Find.FactionManager = new FactionManager();

            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Find.FactionManager.Add(ours);

            var map = new CoreMap(MapSide, MapSide, SimWorld.Map.TerrainDefOf.Soil);
            TickManager tm = Find.TickManager;

            int stride = Math.Max(1, (int)Math.Floor(MapSide / Math.Sqrt(citizenCount + 1)));
            for (int i = 0; i < citizenCount; i++)
            {
                var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f, faction: ours);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                pawn.faction = ours;

                int x = 1 + i % (MapSide / stride) * stride;
                int z = 1 + i / (MapSide / stride) * stride;
                GenSpawn.Spawn(pawn, new IntVec3(Math.Min(x, MapSide - 2), 0, Math.Min(z, MapSide - 2)), map);
                tm.RegisterAllTickabilityFor(pawn);
            }
        }
    }
}
