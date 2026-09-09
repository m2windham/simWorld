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

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            foreach (Thing t in scanner.PotentialWorkThingsGlobal(pawn))
            {
                if (!t.Spawned || t.Map != map) continue;
                int distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!scanner.HasJobOnThing(pawn, t)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best != null ? scanner.JobOnThing(pawn, best) : null;
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
