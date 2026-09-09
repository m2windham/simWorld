using System;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Answers "can this pawn reach that target" without running a full A* (RimWorld: <c>Verse.AI.Reachability</c>)
    /// by BFS over the map's Region/RegionLink graph (<see cref="Map.RegionTraverser.WithinRegions"/>) —
    /// a handful of coarse region hops, not a per-cell search. The graph itself
    /// (<see cref="Map.Map.regionGrid"/>) is kept current by <see cref="Map.Map.regionAndRoomUpdater"/>,
    /// which rebuilds only the region(s) a passability change actually touches, not the whole map: this
    /// class used to keep its own flat flood-fill cache and pay an O(map) recompute on *any* path-grid
    /// change anywhere, however small (see this repository's <c>ai.regions</c> tracker item for that
    /// history) — that cost now lives, correctly scoped, in the region graph instead.
    /// </summary>
    public sealed class Reachability
    {
        private readonly Map.Map map;

        public Reachability(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public bool CanReachTarget(Pawn pawn, LocalTargetInfo target, PathEndMode mode)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (pawn.Map != map || !target.IsValid) return false;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return false;

            map.regionAndRoomUpdater.RebuildIfNeeded();

            Region? from = map.regionGrid.RegionAt(pawn.Position);
            if (from == null) return false;

            IntVec3 targetCell = target.Cell;
            if (mode == PathEndMode.OnCell || mode == PathEndMode.None)
            {
                Region? to = map.regionGrid.RegionAt(targetCell);
                return to != null && RegionTraverser.WithinRegions(from, to);
            }

            // Touch / ClosestTouch / InteractionCell: reachable if any walkable neighbour of the target
            // shares the pawn's region, or the target cell itself is walkable and shares it.
            for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
            {
                Region? nb = map.regionGrid.RegionAt(targetCell + GenAdj.AdjacentCells[d]);
                if (nb != null && RegionTraverser.WithinRegions(from, nb)) return true;
            }
            Region? onTarget = map.regionGrid.RegionAt(targetCell);
            return onTarget != null && RegionTraverser.WithinRegions(from, onTarget);
        }

        /// <summary>Convenience matching RimWorld's call shape; resolves the map from the pawn.</summary>
        public static bool CanReach(Pawn pawn, LocalTargetInfo target, PathEndMode mode = PathEndMode.Touch) =>
            pawn.Map?.reachability.CanReachTarget(pawn, target, mode) ?? false;
    }
}
