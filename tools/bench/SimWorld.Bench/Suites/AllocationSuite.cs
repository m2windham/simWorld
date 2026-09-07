using System;
using System.Collections.Generic;
using System.Globalization;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Bench.Suites
{
    /// <summary>Measurement 4: managed allocation and GC pressure for one in-game day at a fixed N, plus a
    /// targeted micro-measurement of one call site flagged by inspection as an avoidable per-interval
    /// allocation (see docs/perf/baseline.md).</summary>
    internal static class AllocationSuite
    {
        public static void Run(BenchOptions opt)
        {
            Report.Heading("4. Allocation pressure (N=" + opt.Pawns.ToString(CultureInfo.InvariantCulture) + ", " +
                opt.Days.ToString(CultureInfo.InvariantCulture) + " day)");

            Bootstrap.ResetSim(opt.Seed);
            List<Pawn> pawns = PawnFactory.GenerateColonists(opt.Pawns);
            TickManager tm = Find.TickManager;
            for (int i = 0; i < pawns.Count; i++) tm.RegisterAllTickabilityFor(pawns[i]);

            int ticks = GenDate.TicksPerDay * opt.Days;

            ForceFullGc();
            long allocBefore = GC.GetTotalAllocatedBytes(true);
            int gen0Before = GC.CollectionCount(0), gen1Before = GC.CollectionCount(1), gen2Before = GC.CollectionCount(2);

            double ms = Timing.TimeMs(() =>
            {
                for (int t = 0; t < ticks; t++) tm.DoSingleTick();
            });

            long allocAfter = GC.GetTotalAllocatedBytes(true);
            int gen0After = GC.CollectionCount(0), gen1After = GC.CollectionCount(1), gen2After = GC.CollectionCount(2);

            long totalBytes = allocAfter - allocBefore;
            double bytesPerPawnDay = totalBytes / (double)(opt.Pawns * opt.Days);

            Report.SubHeading("Results");
            Report.Table(
                new[] { "metric", "value" },
                new[]
                {
                    new[] { "wall clock", Report.Ms(ms) },
                    new[] { "total managed allocation", Report.Int(totalBytes) + " bytes (" + Report.Num(totalBytes / 1024.0 / 1024.0, 2) + " MB)" },
                    new[] { "allocation per pawn-day", Report.Num(bytesPerPawnDay, 0) + " bytes" },
                    new[] { "Gen0 collections", Report.Int(gen0After - gen0Before) },
                    new[] { "Gen1 collections", Report.Int(gen1After - gen1Before) },
                    new[] { "Gen2 collections", Report.Int(gen2After - gen2Before) },
                });

            RunShouldBeDeadMicroMeasurement();
            RunGetHediffsMicroMeasurement();
        }

        private static void ForceFullGc()
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        /// <summary>
        /// Pawn_HealthTracker.ShouldBeDead (src/SimWorld.Core/Health/Pawn_HealthTracker.cs), called
        /// unconditionally from CheckForStateChange at the end of every HealthTick — i.e. every tick, for every
        /// pawn, hediffs or not — calls PawnCapacityUtility.CalculatePartEfficiency(hediffSet, corePart)
        /// uncached (it bypasses the PawnCapacitiesHandler dictionary entirely). That method
        /// (src/SimWorld.Core/Health/Capacities.cs) itself calls hediffSet.GetHediffsOnPart(part), another
        /// generic iterator that allocates an enumerator per call regardless of whether the part has any
        /// hediffs. This measures that one call's cost directly on a healthy pawn's core body part.
        /// </summary>
        private static void RunShouldBeDeadMicroMeasurement()
        {
            Bootstrap.ResetSim(2);
            List<Pawn> healthy = PawnFactory.GenerateColonists(1);
            Pawn p = healthy[0];
            HediffSet set = p.health.hediffSet;
            BodyPartRecord core = p.RaceProps.body!.corePart!;

            const int calls = 200_000;
            ForceFullGc();
            long before = GC.GetTotalAllocatedBytes(true);
            float sink = 0f;
            for (int i = 0; i < calls; i++)
            {
                sink += PawnCapacityUtility.CalculatePartEfficiency(set, core);
            }
            long after = GC.GetTotalAllocatedBytes(true);
            double bytesPerCall = (after - before) / (double)calls;

            Report.SubHeading("Call-site micro-measurement: CalculatePartEfficiency(corePart), called every tick from ShouldBeDead");
            Console.WriteLine("  " + calls.ToString("N0", CultureInfo.InvariantCulture) + " calls, sink=" + sink.ToString(CultureInfo.InvariantCulture) +
                " (unused, prevents the JIT from eliding the call)");
            Report.Table(
                new[] { "metric", "value" },
                new[]
                {
                    new[] { "bytes allocated", Report.Int(after - before) },
                    new[] { "bytes/call", Report.Num(bytesPerCall, 1) },
                });
            Report.Note(
                "This runs once per tick per pawn unconditionally (60,000x/pawn/day), independent of hediffs, needs, or " +
                "anything else — at " + Report.Num(bytesPerCall, 0) + " bytes/call that is roughly N x 60,000 x " +
                Report.Num(bytesPerCall, 0) + " bytes/day, which for N=1000 is about " +
                Report.Num(1000 * 60000 * bytesPerCall / 1024.0 / 1024.0, 1) + " MB/day on its own.");
        }

        /// <summary>
        /// Pawn_HealthTracker.TryNaturalHealing (src/SimWorld.Core/Health/Pawn_HealthTracker.cs, called from
        /// HealthTick every HealInterval = 600 ticks, i.e. 100x/pawn/day, unconditionally — whether or not the
        /// pawn has any injuries) calls hediffSet.GetHediffs&lt;Hediff_Injury&gt;() twice. That method is a
        /// generic C# iterator (`yield return`), so every call allocates a heap object for the enumerator before
        /// a single element is produced. This measures the bytes/call directly, on a pawn with zero hediffs, to
        /// confirm the allocation happens even when there is nothing to iterate.
        /// </summary>
        private static void RunGetHediffsMicroMeasurement()
        {
            Bootstrap.ResetSim(1);
            List<Pawn> healthy = PawnFactory.GenerateColonists(1);
            Pawn p = healthy[0];
            HediffSet set = p.health.hediffSet;

            const int calls = 200_000;
            ForceFullGc();
            long before = GC.GetTotalAllocatedBytes(true);
            int sink = 0;
            for (int i = 0; i < calls; i++)
            {
                foreach (Hediff_Injury injury in set.GetHediffs<Hediff_Injury>())
                {
                    sink += injury.Part == null ? 1 : 2; // never runs (0 hediffs); keeps the JIT from eliding the call
                }
            }
            long after = GC.GetTotalAllocatedBytes(true);
            double bytesPerCall = (after - before) / (double)calls;

            Report.SubHeading("Call-site micro-measurement: HediffSet.GetHediffs<T>() on a pawn with 0 hediffs");
            Console.WriteLine("  " + calls.ToString("N0", CultureInfo.InvariantCulture) + " calls, sink=" + sink.ToString(CultureInfo.InvariantCulture) +
                " (unused, prevents the loop being optimized away)");
            Report.Table(
                new[] { "metric", "value" },
                new[]
                {
                    new[] { "bytes allocated", Report.Int(after - before) },
                    new[] { "bytes/call", Report.Num(bytesPerCall, 1) },
                });
            Report.Note(
                "At N=" + healthy.Count.ToString(CultureInfo.InvariantCulture) + " x 100 calls/pawn/day (TryNaturalHealing runs twice per " +
                "HealInterval), this call site alone accounts for roughly N x 200 x " + Report.Num(bytesPerCall, 0) +
                " bytes/day — independent of whether any pawn is actually injured.");
        }
    }
}
