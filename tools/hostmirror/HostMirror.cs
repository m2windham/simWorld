using System;
using System.Globalization;
using System.Linq;
using SimWorld.Content;
using SimWorld.Bench.Suites;
using SimWorld.Defs;

namespace SimWorld.Bench
{
    /// <summary>
    /// The layout bench, run on the runtime the Unity host runs: Unity's own Mono, with the core compiled from
    /// source against Unity's class libraries, as the Editor compiles the package. <c>run.sh</c> builds and runs it.
    /// <para/>
    /// It exists because the bench (CoreCLR) and the host (Mono) do not play the same game from the same seed and
    /// the same commit. Mono evaluates <c>float</c> arithmetic with <c>double</c> intermediates where CoreCLR
    /// rounds each step to <c>float</c>, so an expression on a rounding edge comes out differently: the first one
    /// on the host's seed is <c>GenStep_Trees</c>' <c>(int)(40000 * 0.9f / 20f)</c>, 1800 trees on CoreCLR and
    /// 1799 on Mono, and the two games part before the first tick. This harness reproduces the host's game exactly
    /// (the same digest, tick for tick, as a SimWorld.Core.dll taken from a built player), so a layout seen in the
    /// host can be examined here.
    /// </summary>
    internal static class HostMirror
    {
        public static int Main(string[] args)
        {
            var database = new DefDatabase();
            DefLoadResult result = CoreContent.Load(database, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true });
            if (!result.Success)
            {
                Console.Error.WriteLine("Core content failed to load:" + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Select(e => "  - " + e)));
                return 1;
            }
            DefDatabase.Global = database;
            Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

            // The bench's own option names, so a line from one can be pasted into the other.
            var opt = new BenchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--layout-seed": opt.LayoutSeed = args[++i]; break;
                    case "--layout-ticks": opt.LayoutTicks = Int(args[++i]); break;
                    case "--layout-every": opt.LayoutEvery = Int(args[++i]); break;
                    case "--layout-digest-every": opt.LayoutDigestEvery = Int(args[++i]); break;
                    case "--layout-view-every": opt.LayoutViewEvery = Int(args[++i]); break;
                    default:
                        Console.Error.WriteLine("unknown option " + args[i]);
                        return 2;
                }
            }
            LayoutSuite.Run(opt);
            return 0;
        }

        private static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
