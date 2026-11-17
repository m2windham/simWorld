using System;
using System.Collections.Generic;
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
    ///
    /// Answers are cached the way RimWorld's <c>ReachabilityCache</c> caches them, until the region graph next
    /// changes: here as a connected-component label on each region, filled lazily by one BFS per component.
    /// With regions bounded to 12x12 an open map is hundreds of regions, and a work scan asks this once per
    /// candidate thing, so a fresh BFS per question was a third of the simulation (tools/bench --suite mining).
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
                return to != null && Connected(from, to);
            }

            // A multi-cell Thing is reached from the ring touching its footprint — the same cells
            // PathFinder.BuildGoals aims a Touch path at, so "reachable" and "a path exists" cannot disagree.
            if (PathFinder.TryGetFootprint(target, mode, out CellRect footprint))
            {
                CellRect ring = footprint.ExpandedBy(1);
                for (int z = ring.minZ; z <= ring.maxZ; z++)
                {
                    for (int x = ring.minX; x <= ring.maxX; x++)
                    {
                        var c = new IntVec3(x, 0, z);
                        if (footprint.Contains(c) || !GenGrid.InBounds(c, map)) continue;
                        Region? r = map.regionGrid.RegionAt(c);
                        if (r != null && Connected(from, r)) return true;
                    }
                }
                return false;
            }

            // Touch / ClosestTouch / InteractionCell: reachable if any walkable neighbour of the target
            // shares the pawn's region, or the target cell itself is walkable and shares it.
            for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
            {
                Region? nb = map.regionGrid.RegionAt(targetCell + GenAdj.AdjacentCells[d]);
                if (nb != null && Connected(from, nb)) return true;
            }
            Region? onTarget = map.regionGrid.RegionAt(targetCell);
            return onTarget != null && Connected(from, onTarget);
        }

        /// <summary>Convenience matching RimWorld's call shape; resolves the map from the pawn.</summary>
        private int componentsVersion = -1;
        private int nextComponent;
        private readonly Queue<Region> componentQueue = new Queue<Region>();

        /// <summary>Same answer as <see cref="RegionTraverser.WithinRegions"/> over every passable region type, which
        /// every region is: two regions are mutually reachable exactly when their components match.</summary>
        private bool Connected(Region from, Region to)
        {
            if (ReferenceEquals(from, to)) return true;
            int version = map.regionAndRoomUpdater.Version;
            if (version != componentsVersion)
            {
                componentsVersion = version;
                nextComponent = 0;
            }
            return ComponentOf(from, version) == ComponentOf(to, version);
        }

        private int ComponentOf(Region root, int version)
        {
            if (root.componentVersion == version) return root.component;
            int id = nextComponent++;
            root.componentVersion = version;
            root.component = id;
            componentQueue.Clear();
            componentQueue.Enqueue(root);
            while (componentQueue.Count > 0)
            {
                Region cur = componentQueue.Dequeue();
                IReadOnlyList<RegionLink> links = cur.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    Region? other = links[i].GetOtherRegion(cur);
                    if (other == null || other.componentVersion == version) continue;
                    other.componentVersion = version;
                    other.component = id;
                    componentQueue.Enqueue(other);
                }
            }
            return id;
        }

        public static bool CanReach(Pawn pawn, LocalTargetInfo target, PathEndMode mode = PathEndMode.Touch) =>
            pawn.Map?.reachability.CanReachTarget(pawn, target, mode) ?? false;
    }
}
