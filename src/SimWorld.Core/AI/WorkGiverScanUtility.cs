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
                // Nearest first without sorting what the walk never reaches. Most scans take one of the first few
                // candidates, and a wood-short settlement offers every grown tree on the map: sorting all of them for
                // every job search was a tenth of a Bronze-age town's tick (tools/bench --suite tickcost). So the
                // first few are picked by linear passes, each the least key above the last, and the list is sorted
                // only when the walk gets past them. The keys are unique (the list position is in the low bits),
                // so the walk visits candidates in exactly the order a full sort gives. (A heap was tried first and
                // measured slower: many scans do walk deep, refused at every candidate, and popping a heap through
                // List<long> costs more than one sort.)
                int count = order.Count;
                long last = long.MinValue;
                for (int pick = 0; pick < LinearPicks && pick < count; pick++)
                {
                    long next = long.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        long key = order[i];
                        if (key > last && key < next) next = key;
                    }
                    last = next;
                    Thing t = candidates[(int)(next & 0xFFFFFFFF)];
                    if (scanner.HasJobOnThing(pawn, t)) return scanner.JobOnThing(pawn, t);
                }
                if (count <= LinearPicks) return null;

                order.Sort();
                for (int i = LinearPicks; i < count; i++)
                {
                    Thing t = candidates[(int)(order[i] & 0xFFFFFFFF)];
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

        /// <summary>How many candidates <see cref="TryFindJobOnScanner"/> picks by linear pass before it sorts the
        /// rest. Not a sourced number: three keeps a shallow scan from sorting at all while costing a deep one only
        /// three extra passes beside its sort.</summary>
        private const int LinearPicks = 3;

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
