using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Answers "can this pawn reach that target" without running a full A* (RimWorld: <c>Verse.AI.Reachability</c>).
    /// <b>Simplification, flagged per this module's brief:</b> a real region/region-link graph (rooms joined
    /// by doorway links, so a region recomputes only around what actually changed) would scale far better on
    /// a large, well-subdivided map. This implementation is a flat flood-fill: every walkable cell gets a
    /// connected-component id in one pass over the whole map, cached until <see cref="PathGrid.Version"/>
    /// changes and then recomputed in full. That keeps a per-query answer O(1) — the thing the brief called
    /// out as non-negotiable — at the cost of an O(map) recompute on *any* path-grid change, anywhere on the
    /// map, however small. A region graph could replace <see cref="Recompute"/> wholesale later without
    /// touching <see cref="CanReachTarget"/>'s public shape.
    /// </summary>
    public sealed class Reachability
    {
        private readonly Map.Map map;
        private int[] regionId;
        private int lastPathGridVersion = int.MinValue;
        private readonly Queue<int> floodQueue = new Queue<int>();

        public Reachability(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            regionId = new int[map.cellIndices.NumGridCells];
        }

        public bool CanReachTarget(Pawn pawn, LocalTargetInfo target, PathEndMode mode)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawn.Map != map || !target.IsValid) return false;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return false;

            EnsureUpToDate();
            int fromId = RegionAt(pawn.Position);
            if (fromId < 0) return false;

            IntVec3 targetCell = target.Cell;
            if (mode == PathEndMode.OnCell || mode == PathEndMode.None)
            {
                return RegionAt(targetCell) == fromId;
            }

            // Touch / ClosestTouch / InteractionCell: reachable if any walkable neighbour of the target
            // shares the pawn's region, or the target cell itself is walkable and shares it.
            for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
            {
                if (RegionAt(targetCell + GenAdj.AdjacentCells[d]) == fromId) return true;
            }
            return RegionAt(targetCell) == fromId;
        }

        /// <summary>Convenience matching RimWorld's call shape; resolves the map from the pawn.</summary>
        public static bool CanReach(Pawn pawn, LocalTargetInfo target, PathEndMode mode = PathEndMode.Touch) =>
            pawn.Map?.reachability.CanReachTarget(pawn, target, mode) ?? false;

        private void EnsureUpToDate()
        {
            if (lastPathGridVersion == map.pathGrid.Version) return;
            Recompute();
            lastPathGridVersion = map.pathGrid.Version;
        }

        private void Recompute()
        {
            if (regionId.Length != map.cellIndices.NumGridCells)
            {
                regionId = new int[map.cellIndices.NumGridCells];
            }
            Array.Fill(regionId, -1);
            floodQueue.Clear();

            int next = 0;
            int n = regionId.Length;
            for (int seed = 0; seed < n; seed++)
            {
                if (regionId[seed] != -1 || !map.pathGrid.WalkableFast(seed)) continue;

                regionId[seed] = next;
                floodQueue.Enqueue(seed);
                while (floodQueue.Count > 0)
                {
                    int cur = floodQueue.Dequeue();
                    IntVec3 cell = map.cellIndices.IndexToCell(cur);
                    for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                    {
                        IntVec3 nb = cell + GenAdj.AdjacentCells[d];
                        if (!GenGrid.InBounds(nb, map)) continue;
                        int ni = map.cellIndices.CellToIndex(nb);
                        if (regionId[ni] != -1 || !map.pathGrid.WalkableFast(ni)) continue;
                        regionId[ni] = next;
                        floodQueue.Enqueue(ni);
                    }
                }
                next++;
            }
        }

        private int RegionAt(IntVec3 c) => GenGrid.InBounds(c, map) ? regionId[map.cellIndices.CellToIndex(c)] : -1;
    }
}
