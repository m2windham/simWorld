using System;
using System.Collections.Generic;

using SimWorld.Map;

namespace SimWorld.Building
{
    /// <summary>
    /// Which roof cells fall when a holder leaves the map (RimWorld:
    /// <c>Verse.RoofCollapseCellsFinder</c>). Two questions, in RimWorld's own order:
    /// <list type="number">
    /// <item><b>Too far from any support.</b> Every roofed cell within
    /// <see cref="RoofCollapseUtility.RoofSupportRadialCellsCount"/> of the vacated footprint that
    /// <see cref="RoofCollapseUtility.WithinRangeOfRoofHolder"/> now says no to.</item>
    /// <item><b>Flying roof.</b> A roofed area that, however near a wall it looks, is no longer connected
    /// along the roof to anything holding it up at all — checked around the vacated footprint and again
    /// around whatever the first question condemned, because a cell can be held up <i>through</i> roof that
    /// has just been taken away. This port had no answer to this question at all; it is the reason RimWorld
    /// drops a whole severed roof in one event rather than one ring of it.</item>
    /// </list>
    ///
    /// <para/><b>The collapse buffer is not ported, and the difference is one tick.</b> RimWorld marks cells
    /// into <c>map.roofCollapseBuffer</c> and lets <c>RoofCollapseBufferResolver</c> drop them on the next map
    /// tick, so one letter names everything crushed at once. There is no letter system here to batch for, so
    /// the marked set is local and is dropped at the end of the call — the same cells, the same event, one
    /// tick earlier. Nothing in RimWorld's delay is a chance to escape: the resolver runs on the very next
    /// tick, before anything under the roof can take a step.
    /// </summary>
    public static class RoofCollapseCellsFinder
    {
        /// <summary>
        /// A roof holder left <paramref name="rect"/> (RimWorld:
        /// <c>RoofCollapseCellsFinder.ProcessRoofHolderDespawned</c>). RimWorld takes the despawned Thing's
        /// <c>Position</c> for the radial half and its <c>OccupiedRect</c> for the flying-roof half; this port
        /// is handed only the rect, so the radial half runs from every cell of it — identical for the 1×1
        /// edifices that hold roof in shipped content, and a superset for anything larger.
        /// </summary>
        public static void ProcessRoofHolderDespawned(CellRect rect, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            // A list beside the set, not the set alone: what falls is damaged in the order it was marked,
            // and every one of those hits draws from the seeded stream. Determinism is a feature.
            var marked = new MarkedCells();
            var visited = new HashSet<IntVec3>();

            CheckCollapseFlyingRoofs(rect.Cells, map, marked, visited);

            var tooFar = new List<IntVec3>();
            foreach (IntVec3 origin in rect.Cells)
            {
                for (int i = 0; i < RoofCollapseUtility.RoofSupportRadialCellsCount; i++)
                {
                    IntVec3 c = origin + GenRadial.RadialPattern[i];
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.roofGrid.Roofed(c)) continue;
                    if (marked.Contains(c)) continue;
                    if (RoofCollapseUtility.WithinRangeOfRoofHolder(c, map)) continue;
                    if (marked.Add(c)) tooFar.Add(c);
                }
            }

            CheckCollapseFlyingRoofs(tooFar, map, marked, visited);

            if (marked.Count == 0) return;
            RoofCollapserImmediate.DropRoofInCells(marked.Cells, map);
        }

        /// <summary>
        /// A roof was just manually stripped from <paramref name="c"/> (RimWorld:
        /// <c>JobDriver_RemoveRoof.DoEffect</c>'s own call into <c>CheckCollapseFlyingRoofs</c>, immediately
        /// after its own <c>roofGrid.SetRoof(cell, null)</c>). Runs only the flying-roof half of a collapse —
        /// a neighbouring patch of roof that reached a support only <i>through</i> the cell just stripped now
        /// hangs with nothing under it at all — not the "too far from any support" radial half
        /// <see cref="ProcessRoofHolderDespawned"/> also runs, which only applies when a Fillage-Full edifice
        /// (a wall, a door, unmined rock) leaves the map; stripping a bare roof cell removes no edifice, so
        /// nothing newly falls outside its own radius.
        /// </summary>
        public static void ProcessRoofRemoved(IntVec3 c, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var marked = new MarkedCells();
            var visited = new HashSet<IntVec3>();
            CheckCollapseFlyingRoofs(new[] { c }, map, marked, visited);

            if (marked.Count == 0) return;
            RoofCollapserImmediate.DropRoofInCells(marked.Cells, map);
        }

        /// <summary>
        /// Marks every roofed area around <paramref name="nearCells"/> that no longer connects along the roof
        /// to any holder (RimWorld: <c>CheckCollapseFlyingRoofs</c> →
        /// <c>CheckCollapseFlyingRoofAtAndAdjInternal</c>). <paramref name="visited"/> is shared across the
        /// whole despawn event so a region proven connected is not re-walked for each of its cells, which is
        /// what keeps this affordable on a map that is mostly roof.
        /// </summary>
        private static void CheckCollapseFlyingRoofs(IEnumerable<IntVec3> nearCells, Map.Map map,
            MarkedCells marked, HashSet<IntVec3> visited)
        {
            foreach (IntVec3 root in nearCells)
            {
                // Cardinal neighbours and the cell itself — RimWorld's GenAdj.CardinalDirectionsAndInside.
                for (int i = 0; i <= GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 c = i == GenAdj.CardinalDirections.Length ? root : root + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.roofGrid.Roofed(c)) continue;
                    if (marked.Contains(c) || visited.Contains(c)) continue;
                    if (ConnectsToRoofHolder(c, map, visited)) continue;

                    // Nothing holds this roof up anywhere along it: the whole connected area falls.
                    FloodRoofedRegion(c, map, marked);
                }
            }
        }

        /// <summary>
        /// True if the roofed area containing <paramref name="c"/> reaches, along the roof, a cell that holds
        /// roof up (RimWorld: <c>ConnectsToRoofHolder</c>). Unbounded by distance, unlike
        /// <see cref="RoofCollapseUtility.WithinRangeOfRoofHolder"/> — this is "is anything under this ceiling
        /// at all", not "is anything near enough".
        /// </summary>
        public static bool ConnectsToRoofHolder(IntVec3 c, Map.Map map, HashSet<IntVec3> visited)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (visited == null) throw new ArgumentNullException(nameof(visited));

            var queue = new Queue<IntVec3>();
            var seen = new HashSet<IntVec3> { c };
            queue.Enqueue(c);

            while (queue.Count > 0)
            {
                IntVec3 x = queue.Dequeue();
                if (!visited.Add(x)) return true; // a previous walk already proved this area connected

                if (RoofCollapseUtility.HoldsRoof(x, map)) return true;
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 neighbour = x + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(neighbour, map)) continue;
                    if (RoofCollapseUtility.HoldsRoof(neighbour, map)) return true;
                    if (!map.roofGrid.Roofed(neighbour)) continue;
                    if (seen.Add(neighbour)) queue.Enqueue(neighbour);
                }
            }
            return false;
        }

        /// <summary>Marks the whole roofed area containing <paramref name="c"/> (RimWorld: the
        /// <c>FloodFill(intVec, x =&gt; x.Roofed(map), x =&gt; MarkToCollapse(x))</c> arm).</summary>
        private static void FloodRoofedRegion(IntVec3 c, Map.Map map, MarkedCells marked)
        {
            var queue = new Queue<IntVec3>();
            if (!marked.Add(c)) return;
            queue.Enqueue(c);

            while (queue.Count > 0)
            {
                IntVec3 x = queue.Dequeue();
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 neighbour = x + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(neighbour, map)) continue;
                    if (!map.roofGrid.Roofed(neighbour)) continue;
                    if (marked.Add(neighbour)) queue.Enqueue(neighbour);
                }
            }
        }

        /// <summary>
        /// The cells condemned by one despawn event — RimWorld's <c>map.roofCollapseBuffer</c>, held for the
        /// length of the call rather than across a tick (see this class's own remarks). A set for the
        /// "already condemned?" question and a list for the order they fall in, because the order is what a
        /// seeded damage roll is drawn against.
        /// </summary>
        private sealed class MarkedCells
        {
            private readonly HashSet<IntVec3> set = new HashSet<IntVec3>();

            public List<IntVec3> Cells { get; } = new List<IntVec3>();

            public int Count => Cells.Count;

            public bool Contains(IntVec3 c) => set.Contains(c);

            public bool Add(IntVec3 c)
            {
                if (!set.Add(c)) return false;
                Cells.Add(c);
                return true;
            }
        }
    }
}
