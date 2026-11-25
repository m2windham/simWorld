using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// The nearest-candidate work scan shared by every think-tree node that walks an ordered list of
    /// <see cref="WorkGiverDef"/>s (RimWorld: the inner loop of <c>RimWorld.JobGiver_Work</c>). Pulled out of
    /// <see cref="JobGiver_Work"/> so <see cref="JobGiver_Edicts"/> (system 12: the god layer's edict tier) can
    /// reuse the exact same "skip ineligible givers, then nearest reachable thing with a job on it" search over
    /// its own, edict-restricted giver list instead of duplicating it.
    /// </summary>
    public static class WorkGiverScanUtility
    {
        /// <summary>Tries each giver in order; the first to produce a job wins. Skips a giver whose
        /// <see cref="WorkGiver.ShouldSkip"/> or <see cref="WorkGiver.MissingRequiredCapacity"/> says no, and
        /// anything not a <see cref="WorkGiver_Scanner"/> at all (the no-op <see cref="WorkGiver_Pending"/>
        /// placeholder content still points most <see cref="WorkGiverDef"/>s at).</summary>
        public static Job? TryGiveJobInGivers(Pawn pawn, IReadOnlyList<WorkGiverDef> givers)
        {
            for (int i = 0; i < givers.Count; i++)
            {
                if (!(givers[i].Worker is WorkGiver_Scanner scanner)) continue;
                if (scanner.ShouldSkip(pawn) || scanner.MissingRequiredCapacity(pawn)) continue;
                Job? job = TryFindJobOnScanner(pawn, scanner);
                if (job != null) return job;
            }
            return null;
        }

        /// <summary>
        /// Nearest spawned, on-map candidate the scanner has a job on; null if none qualify. A cell-scanning
        /// giver (<see cref="WorkGiverDef.scanCells"/>) is tried first over <see cref="WorkGiver_Scanner.PotentialWorkCellsGlobal"/>;
        /// a thing-scanning one (<see cref="WorkGiverDef.scanThings"/>, the default) walks
        /// <see cref="WorkGiver_Scanner.PotentialWorkThingsGlobal"/> exactly as before this pass added the
        /// cell half — a giver can set both, in which case the cell result wins ties by being tried first.
        /// </summary>
        public static Job? TryFindJobOnScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;

            if (scanner.def.scanCells)
            {
                Job? cellJob = TryFindJobOnCellScanner(pawn, scanner);
                if (cellJob != null) return cellJob;
            }
            if (!scanner.def.scanThings) return null;

            // Nearest first. RimWorld's GenClosest.ClosestThing_Global_Reachable walks the candidates in list order
            // and tightens its distance bound only on a candidate it accepts, so every rejection let farther
            // candidates through to HasJobOnThing. That is cheap over a designation list; this port's miner has
            // no designations and offers every rock on the map, and testing them in list order was most of the
            // simulation once a band took to mining (tools/bench --suite mining). Sorting by (distance, list
            // position) and stopping at the first accepted candidate picks the same thing the list-order walk
            // picks — the nearest accepted, the earliest-listed on a tie — without asking about the rest.
            ScanBuffers buffers = RentBuffers();
            List<Thing> candidates = buffers.candidates;
            List<long> order = buffers.order;
            try
            {
                foreach (Thing t in scanner.PotentialWorkThingsGlobal(pawn))
                {
                    if (!t.Spawned || t.Map != map) continue;
                    long distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                    order.Add((distSq << 32) | (uint)candidates.Count);
                    candidates.Add(t);
                }
                // A heap rather than a sort: the same ascending order, but paid for only as far as the walk goes.
                // The keys are unique (the list position is in the low bits), so the candidates come out in
                // exactly the order a full sort gives. Most scans stop at the first or second candidate, and a
                // wood-short settlement offers every grown tree on the map: sorting all of them for every job
                // search was a tenth of a Bronze-age town's tick (tools/bench --suite tickcost).
                Heapify(order);
                for (int n = order.Count; n > 0; n--)
                {
                    Thing t = candidates[(int)(PopMin(order, n) & 0xFFFFFFFF)];
                    if (scanner.HasJobOnThing(pawn, t)) return scanner.JobOnThing(pawn, t);
                }
                return null;
            }
            finally
            {
                candidates.Clear();
                order.Clear();
                freeBuffers!.Push(buffers);
            }
        }

        /// <summary>Makes <paramref name="heap"/> a binary min-heap in place, in linear time.</summary>
        private static void Heapify(List<long> heap)
        {
            for (int i = heap.Count / 2 - 1; i >= 0; i--) SiftDown(heap, i, heap.Count);
        }

        /// <summary>Removes and returns the least of the first <paramref name="count"/> entries of a min-heap,
        /// leaving the rest a heap of <paramref name="count"/> - 1.</summary>
        private static long PopMin(List<long> heap, int count)
        {
            long min = heap[0];
            heap[0] = heap[count - 1];
            SiftDown(heap, 0, count - 1);
            return min;
        }

        private static void SiftDown(List<long> heap, int i, int count)
        {
            long item = heap[i];
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= count) break;
                if (child + 1 < count && heap[child + 1] < heap[child]) child++;
                if (heap[child] >= item) break;
                heap[i] = heap[child];
                i = child;
            }
            heap[i] = item;
        }

        /// <summary>The scan's two working lists, reused instead of allocated on every job search. A stack rather
        /// than one pair because a scan can re-enter itself — <see cref="WorkGiver_PatientGoToBed"/> asks the
        /// whole work list from inside a giver — and per thread because tests run simulations side by side.</summary>
        private sealed class ScanBuffers
        {
            public readonly List<Thing> candidates = new List<Thing>();
            public readonly List<long> order = new List<long>();
        }

        [ThreadStatic] private static Stack<ScanBuffers>? freeBuffers;

        private static ScanBuffers RentBuffers()
        {
            freeBuffers ??= new Stack<ScanBuffers>();
            return freeBuffers.Count > 0 ? freeBuffers.Pop() : new ScanBuffers();
        }

        private static Job? TryFindJobOnCellScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDistSq = int.MaxValue;
            bool found = false;
            foreach (IntVec3 c in scanner.PotentialWorkCellsGlobal(pawn))
            {
                int distSq = (c - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!scanner.HasJobOnCell(pawn, c)) continue;
                best = c;
                bestDistSq = distSq;
                found = true;
            }
            return found ? scanner.JobOnCell(pawn, best) : null;
        }
    }
}
