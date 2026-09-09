using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// Flood-fills one <see cref="Region"/> from a seed cell (RimWorld: <c>Verse.RegionMaker</c>). Growth is
    /// cardinal-only — diagonal "cutting" never counts as the same region, matching both
    /// <see cref="AI.PathFinder"/>'s corner-cutting rule and <see cref="Building.RoomTracker"/>'s own
    /// cardinal-only room flood fill — and stops dead at a doorway: a door cell never merges with its
    /// neighbours, it always becomes its own single-cell <see cref="RegionType.Portal"/> region, joined to
    /// the region(s) on either side by a <see cref="RegionLink"/> instead. That is what lets a door's
    /// open/closed state change later (this port's <see cref="Building.Door"/> does not model that yet — see
    /// its own remarks) without forcing the two rooms it separates to merge or split.
    ///
    /// <b>Region size</b>: RimWorld's own region growth is bounded by geometry only (impassable cells,
    /// doorways, the map edge) — there is no separate cell-count cap, so a large open area legitimately
    /// becomes one large region. The brief that requested this port also named "region size 12" among the
    /// constants to keep, but nothing in the sources available to this port pins down what that number
    /// actually bounds (candidates considered and rejected for lack of a source: a per-region cell cap,
    /// which would fragment a big open room into hundreds of tiny regions and contradicts the
    /// geometry-only growth above; an internal list/array capacity hint, which has no observable behaviour
    /// to test). Per this repository's rule for an unsourced constant, it is not implemented — flood growth
    /// here is unbounded, and <c>Region_flood_fill_does_not_fragment_a_large_open_area</c> in
    /// <c>RegionTests.cs</c> pins that behaviour down instead of the literal.
    ///
    /// <b>Trim</b>: a portal region is one door <i>cell</i>, not one door <i>Thing</i> — RimWorld's real
    /// portal region spans a whole (possibly multi-cell) door. Nothing in this port's content defines a
    /// door wider than one cell, so the distinction has no observable effect yet; merging same-door cells
    /// into a single portal region can be done later without changing this class's shape.
    /// </summary>
    internal sealed class RegionMaker
    {
        private readonly Map map;
        private readonly RegionGrid regionGrid;
        private readonly Queue<IntVec3> queue = new Queue<IntVec3>();

        public RegionMaker(Map map, RegionGrid regionGrid)
        {
            this.map = map;
            this.regionGrid = regionGrid;
        }

        /// <summary>Builds one region starting at <paramref name="root"/> and registers it with the region
        /// grid; returns <c>false</c> without doing anything if <paramref name="root"/> already has a region
        /// or is not walkable.</summary>
        public bool TryGenerateRegionFrom(IntVec3 root)
        {
            if (regionGrid.RegionAt(root) != null) return false;
            if (!map.pathGrid.Walkable(root)) return false;

            var region = new Region { type = IsDoorAt(root) ? RegionType.Portal : RegionType.Normal };
            bool isPortal = region.type == RegionType.Portal;

            AddCellToRegion(region, root);

            queue.Clear();
            queue.Enqueue(root);

            // A portal region is exactly its root cell: nothing is ever enqueued for it below, so this loop
            // processes it once and stops on its own.
            while (queue.Count > 0)
            {
                IntVec3 cur = queue.Dequeue();

                for (int d = 0; d < GenAdj.CardinalDirections.Length; d++)
                {
                    IntVec3 nb = cur + GenAdj.CardinalDirections[d];
                    if (!GenGrid.InBounds(nb, map) || !map.pathGrid.Walkable(nb)) continue;

                    Region? existing = regionGrid.RegionAt(nb);
                    if (ReferenceEquals(existing, region)) continue;

                    bool nbIsPortal = IsDoorAt(nb);
                    if (isPortal || nbIsPortal)
                    {
                        // A portal boundary never flood-fills across — link instead, once the neighbour's
                        // own region actually exists. If it doesn't yet, the other side connects this same
                        // pair back when its own TryGenerateRegionFrom call reaches us in turn (order of
                        // construction doesn't matter — see this class's remarks on why).
                        if (existing != null) ConnectRegions(region, existing);
                        continue;
                    }

                    if (existing != null)
                    {
                        ConnectRegions(region, existing);
                        continue;
                    }

                    AddCellToRegion(region, nb);
                    queue.Enqueue(nb);
                }
            }

            regionGrid.AddRegion(region);
            return true;
        }

        private void AddCellToRegion(Region region, IntVec3 c)
        {
            region.cells.Add(c);
            regionGrid.SetRegionAt(c, region);
        }

        private static void ConnectRegions(Region a, Region b)
        {
            for (int i = 0; i < a.links.Count; i++)
            {
                if (ReferenceEquals(a.links[i].GetOtherRegion(a), b)) return; // already linked
            }
            var link = new RegionLink(a, b);
            a.links.Add(link);
            b.links.Add(link);
        }

        private bool IsDoorAt(IntVec3 c) => map.edificeGrid[c] is Building.Door;
    }
}
