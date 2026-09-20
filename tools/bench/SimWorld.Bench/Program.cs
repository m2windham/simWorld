using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimWorld.Bench.Suites;

namespace SimWorld.Bench
{
    /// <summary>
    /// Stopwatch-based performance harness for the SimWorld simulation core (docs/perf/baseline.md). Plain
    /// console app, no BenchmarkDotNet — see tools/bench/SimWorld.Bench/README notes in the csproj comment.
    /// Run with --help for usage.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Any(a => a is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            BenchOptions opt;
            try
            {
                opt = ParseArgs(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                Console.Error.WriteLine("Run with --help for usage.");
                return 1;
            }

            Bootstrap.LoadContentOnce();

            Console.WriteLine("# SimWorld.Bench");
            Console.WriteLine();
            Console.WriteLine(
                "suite=" + opt.Suite + " pawns=" + opt.Pawns.ToString(CultureInfo.InvariantCulture) +
                " days=" + opt.Days.ToString(CultureInfo.InvariantCulture) + " seed=" + opt.Seed.ToString(CultureInfo.InvariantCulture) +
                " warmup=" + opt.Warmup.ToString(CultureInfo.InvariantCulture) + " runs=" + opt.Runs.ToString(CultureInfo.InvariantCulture) +
                " guard=" + opt.GuardSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s");

            switch (opt.Suite)
            {
                case "tick":
                    RunSingleTick(opt);
                    break;
                case "scaling":
                    ScalingSuite.Run(opt, opt.ScalingNs);
                    break;
                case "attribution":
                    AttributionSuite.Run(opt, opt.Pawns);
                    break;
                case "hediffs":
                    HediffSuite.Run(opt);
                    break;
                case "alloc":
                    AllocationSuite.Run(opt);
                    break;
                case "worldgen":
                    WorldGenSuite.Run(opt);
                    break;
                case "saveload":
                    SaveLoadSuite.Run(opt);
                    break;
                case "pathing":
                    PathingSuite.Run(opt, opt.PathingNs);
                    break;
                case "interrupts":
                    ConstantThinkTreeSuite.Run(opt);
                    break;
                case "phasing":
                    HashPhasingSuite.Run(opt);
                    break;
                case "targets":
                    AttackTargetScanSuite.Run(opt);
                    break;
                case "mapview":
                    MapViewSuite.Run(opt);
                    break;
                case "probe":
                    ProbeSuite.Run(opt);
                    break;
                case "tension":
                    TensionSuite.Run(opt);
                    break;
                case "all":
                    RunAll(opt);
                    break;
                default:
                    Console.Error.WriteLine("error: unknown --suite '" + opt.Suite + "'.");
                    return 1;
            }

            return 0;
        }

        /// <summary>The suite that runs for the plain `--pawns N --days D` invocation: one pawn-tick-scaling
        /// configuration, reported without the full sweep.</summary>
        private static void RunSingleTick(BenchOptions opt)
        {
            ScalingSuite.Run(opt, new[] { opt.Pawns });
        }

        private static void RunAll(BenchOptions opt)
        {
            var scaling = ScalingSuite.Run(opt, opt.ScalingNs);

            int attributionN = LargestComfortable(scaling, opt.GuardMs);
            AttributionSuite.Run(opt, attributionN);

            HediffSuite.Run(opt);
            AllocationSuite.Run(opt);
            WorldGenSuite.Run(opt);
            SaveLoadSuite.Run(opt);
            PathingSuite.Run(opt, opt.PathingNs);
            ConstantThinkTreeSuite.Run(opt);
            HashPhasingSuite.Run(opt);
            AttackTargetScanSuite.Run(opt);
        }

        /// <summary>
        /// Picks the largest scaling-sweep N whose median stayed comfortably under the guard (at most a quarter
        /// of it) for the cost-attribution measurement, per baseline.md measurement 2 ("the largest N that
        /// finishes comfortably"). Falls back to the smallest tested N if nothing qualifies.
        /// </summary>
        private static int LargestComfortable(List<ScalingRow> rows, double guardMs)
        {
            double comfortableCapMs = guardMs * 0.25;
            int best = rows.Count > 0 ? rows[0].Pawns : 1000;
            foreach (ScalingRow r in rows)
            {
                if (!r.Timing.GuardTripped && r.Timing.MedianMs <= comfortableCapMs) best = r.Pawns;
            }
            return best;
        }

        private static BenchOptions ParseArgs(string[] args)
        {
            var opt = new BenchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--suite":
                        opt.Suite = Next(args, ref i);
                        break;
                    case "--pawns":
                        opt.Pawns = ParseInt(Next(args, ref i), "--pawns");
                        break;
                    case "--days":
                        opt.Days = ParseInt(Next(args, ref i), "--days");
                        break;
                    case "--seed":
                        opt.Seed = ParseInt(Next(args, ref i), "--seed");
                        break;
                    case "--warmup":
                        opt.Warmup = ParseInt(Next(args, ref i), "--warmup");
                        break;
                    case "--runs":
                        opt.Runs = ParseInt(Next(args, ref i), "--runs");
                        break;
                    case "--guard-seconds":
                        opt.GuardSeconds = ParseDouble(Next(args, ref i), "--guard-seconds");
                        break;
                    case "--ns":
                        opt.ScalingNs = ParseIntList(Next(args, ref i), "--ns");
                        break;
                    case "--without":
                        opt.Without = Next(args, ref i)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .ToArray();
                        break;
                    case "--solo":
                        opt.Solo = ParseBool(Next(args, ref i), "--solo");
                        break;
                    case "--band":
                        opt.Band = ParseInt(Next(args, ref i), "--band");
                        break;
                    case "--subdivisions":
                        opt.Subdivisions = ParseIntList(Next(args, ref i), "--subdivisions");
                        break;
                    case "--pathing-ns":
                        opt.PathingNs = ParseIntList(Next(args, ref i), "--pathing-ns");
                        break;
                    case "--constant-tree-ticks":
                        opt.ConstantTreeTicks = ParseInt(Next(args, ref i), "--constant-tree-ticks");
                        break;
                    case "--phasing-cycles":
                        opt.PhasingCycles = ParseInt(Next(args, ref i), "--phasing-cycles");
                        break;
                    case "--target-ns":
                        opt.TargetNs = ParseIntList(Next(args, ref i), "--target-ns");
                        break;
                    case "--target-tick-ns":
                        opt.TargetTickNs = ParseIntList(Next(args, ref i), "--target-tick-ns");
                        break;
                    default:
                        throw new ArgumentException("unrecognized argument '" + a + "'.");
                }
            }
            if (opt.Pawns <= 0) throw new ArgumentException("--pawns must be positive.");
            if (opt.Band <= 0) throw new ArgumentException("--band must be positive.");
            if (opt.Days <= 0) throw new ArgumentException("--days must be positive.");
            if (opt.Runs <= 0) throw new ArgumentException("--runs must be positive.");
            if (opt.Warmup < 0) throw new ArgumentException("--warmup cannot be negative.");
            if (opt.ConstantTreeTicks <= 0) throw new ArgumentException("--constant-tree-ticks must be positive.");
            if (opt.PhasingCycles <= 0) throw new ArgumentException("--phasing-cycles must be positive.");
            return opt;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("'" + args[i] + "' needs a value.");
            i++;
            return args[i];
        }

        private static int ParseInt(string s, string flag)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            {
                throw new ArgumentException("'" + flag + "' expects an integer, got '" + s + "'.");
            }
            return v;
        }

        private static bool ParseBool(string s, string flag)
        {
            if (!bool.TryParse(s, out bool v))
            {
                throw new ArgumentException("'" + flag + "' expects true or false, got '" + s + "'.");
            }
            return v;
        }

        private static double ParseDouble(string s, string flag)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                throw new ArgumentException("'" + flag + "' expects a number, got '" + s + "'.");
            }
            return v;
        }

        private static int[] ParseIntList(string s, string flag)
        {
            string[] parts = s.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = ParseInt(parts[i].Trim(), flag);
            }
            return result;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"SimWorld.Bench — stopwatch harness for the simulation core.

USAGE
  dotnet run -c Release --project tools/bench/SimWorld.Bench -- [options]

OPTIONS
  --suite <name>          tick (default) | scaling | attribution | hediffs | alloc | worldgen | saveload | pathing | interrupts | phasing | targets | mapview | probe | tension | all
  --pawns <N>             pawn count (default 1000). Used by: tick, attribution, hediffs, alloc, saveload.
  --days <N>              in-game days to tick (default 1). Used by: tick, scaling, attribution, hediffs, alloc, probe, tension.
                          For --suite tension the span is read in years: 60 days to the year, which is also
                          its sample interval, so --days 12000 is a 200-year run.
  --seed <N>              RandomStream seed (default 12345).
  --warmup <N>            warmup trials discarded before measuring (default 1).
  --runs <N>              measured trials; the median is reported (default 3).
  --guard-seconds <secs>  abort a configuration if a single trial exceeds this (default 120).
  --ns <csv>              N sweep for --suite scaling (default 100,250,500,1000,2500,5000,10000).
  --subdivisions <csv>    subdivision sweep for --suite worldgen (default 3,4,5,6).
  --pathing-ns <csv>      N sweep for --suite pathing (default 100,400,1000).
  --constant-tree-ticks <N>  ticks per trial for --suite interrupts (default 6000).
  --phasing-cycles <N>    600-tick cycles per trial for --suite phasing (default 10).
  --constant-tree-ticks <N>  ticks per trial for --suite interrupts and --suite targets (default 6000).
  --target-ns <csv>       N sweep for --suite targets' scan measurements (default 100,250,500,1000,2000).
  --target-tick-ns <csv>  N sweep for --suite targets' tick-loop A/B (default 250,500,1000).
  --without <csv>         names to ablate for --suite probe's second arm, e.g. ManhunterPack. The probe
                          then runs each arm twice, with and without, and reports the difference.
  --solo <true|false>     for --suite tension: start the player alone and let rivals emerge (default true,
                          the same world shape --suite probe uses), or generate a world already full of
                          rival civilizations (false).
  --band <N>              for --suite tension: founding band size (default 25, the same band --suite probe
                          founds with). A knob rather than a constant because that band does not survive a
                          long unwatched run — see that suite's own doc. Spec §5b.3 fixes the legal range at
                          20-40; outside it the founder throws.
  --help, -h              show this text.

SUITES
  tick          Ticks --pawns colonists through --days in-game day(s) once; the required smoke-test shape
                (baseline.md measurement 1, single configuration).
  scaling       The full N sweep from measurement 1: pawn tick scaling and where 1x/15x parity breaks.
  attribution   Measurement 2: per-tracker cost split (health/needs/mindState/skills/age) at --pawns.
  hediffs       Measurement 3: day-tick cost with no hediffs / 3 injuries / 3 injuries + Flu, at --pawns.
  alloc         Measurement 4: GC.GetTotalAllocatedBytes and Gen0/1/2 counts for one day at --pawns.
  worldgen      Measurement 5: WorldGenerator.GenerateWorld time and tile count across --subdivisions.
  saveload      Measurement 6: Scribe save/load time and serialized size for --pawns pawns.
  pathing       Measurement 9: N pawns to a shared destination, before (no region-graph sharing) vs after,
                across --pathing-ns.
  interrupts    Measurement 11: what the constant think tree costs at TieringTuning.FullTierBudget — one
                evaluation in isolation, and the whole tick loop off / at the shipped interval / every tick.
  phasing       Measurement 12: what Pawn.HashOffsetTicks' phasing costs on the WORST tick at
                TieringTuning.FullTierBudget — pawns per phase counted, and per-tick wall clock folded over
                the 600-tick cycle, for the old id * 3 offset against the hashed one.
  targets       Measurement 12: what finding an attack target costs as population moves — the scan alone,
                one constant-tree evaluation, and the tick loop A/B, swept across --target-ns.
  mapview       What the Map/View settlement-interior read model costs on a generated TribalStart interior:
                a full Capture() against the incremental CaptureChanges() a host runs every frame
                (docs/perf/map-view.md).
  probe         OUTCOMES, not time: runs the same seeded world twice, watched and unwatched, and reports a
                fixed metric vector per in-game day (population and tier split, mood, health, food, larder
                nutrition, deaths by cause, research, era) plus the watched-minus-unwatched difference. The
                baseline a deliberately injected defect is measured against. Also reports its own throughput,
                because what an in-game year costs in wall clock decides the affordable window and is a fact
                about the machine rather than an assumption. Not in `all` — it is slow and run on purpose.
  tension       OUTCOMES, not time: ticks one real seeded game for --days in-game days, unwatched, and samples
                Director.StandingReader (relative standing against rival civilizations) once per in-game year.
                Reports the series and the four questions docs/design/goal-renewal.md §8 step 1 asks of a
                tension before anything is built on it: does it move, does its rate of change decay across the
                run's thirds, does it survive the world settling, is it deterministic. An instrument only —
                nothing in the simulation acts on the reading. Not in `all`: slow, and run on purpose.
  all           Runs every suite in sequence (scaling first; attribution's N is derived from its result).

EXAMPLES
  dotnet run -c Release --project tools/bench/SimWorld.Bench -- --pawns 1000 --days 1
  dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite all
  dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite probe --days 6 --seed 12345
  dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite tension --days 12000 --seed 12345
");
        }
    }
}
