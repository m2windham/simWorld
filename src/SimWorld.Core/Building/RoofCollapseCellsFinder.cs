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
    /// <para/><b>The collapse buffer is ported; its one-tick delay is not.</b> RimWorld marks cells into
    /// <c>map.roofCollapseBuffer</c> and lets <c>RoofCollapseBufferResolver</c> drop them on the next map
    /// tick, so one letter names everything crushed at once. There is no letter system here to batch for, so
    /// the <see cref="RoofCollapseBuffer"/> is held for the length of one event and dropped before the call
    /// returns — the same cells, the same event, one tick earlier. Nothing in RimWorld's delay is a chance to
    /// escape: the resolver runs on the very next tick, before anything under the roof can take a step.
    ///
    /// <para/><b>What the buffer is for, and what its absence cost.</b> RimWorld's finder never drops a roof.
    /// It marks, and the drop is one loop somewhere else — so a falling roof that destroys another holder
    /// only adds cells to a list that is still being walked. This port dropped from inside the finder, with a
    /// set local to each call, so a holder destroyed <i>by</i> a collapse started a second collapse inside
    /// the first: the cells still waiting for their roof to be taken away were condemned again, everything
    /// under them was crushed again, and the depth of that nesting was bounded by nothing but the stack.
    /// <see cref="resolving"/> is what makes a nested call mark into the collapse already under way and
    /// return. The commonest way in was not a second holder at all but the first one — see
    /// <c>Things.Thing.DeSpawn</c> for why a holder leaves the thing grid before its roof is asked about.
    /// </summary>
    public static class RoofCollapseCellsFinder
    {
        /// <summary>The collapse being dropped on this thread right now, and the map it is on; null between
        /// events. Thread-static like the rest of this module's scratch state, because tests run whole games
        /// on several threads at once.</summary>
        [ThreadStatic] private static RoofCollapseBuffer? resolving;

        [ThreadStatic] private static Map.Map? resolvingMap;

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

            // A holder went while a collapse on this map is still being dropped — something that falling
            // roof destroyed. Its cells join the work already under way: RoofCollapserImmediate is walking
            // that list by index and will reach them.
            if (resolving != null && ReferenceEquals(resolvingMap, map))
            {
                MarkCellsLeftUnsupported(rect, map, resolving);
                return;
            }

            var buffer = new RoofCollapseBuffer();
            MarkCellsLeftUnsupported(rect, map, buffer);
            if (buffer.CellsMarkedToCollapse.Count == 0) return;

            RoofCollapseBuffer? outer = resolving;
            Map.Map? outerMap = resolvingMap;
            resolving = buffer;
            resolvingMap = map;
            try
            {
                RoofCollapserImmediate.DropRoofInCells(buffer.CellsMarkedToCollapse, map);
            }
            finally
            {
                resolving = outer;
                resolvingMap = outerMap;
            }
        }

        /// <summary>
        /// Both of RimWorld's questions about one vacated footprint, answered into <paramref name="marked"/>.
        /// A cell already condemned is skipped, which is what stops a holder lost mid-collapse from
        /// condemning — and so crushing — the same cells twice.
        /// </summary>
        private static void MarkCellsLeftUnsupported(CellRect rect, Map.Map map, RoofCollapseBuffer marked)
        {
            // Fresh for every footprint: a region an earlier walk proved connected may have been connected
            // through the very holder that has just gone.
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
                    if (marked.IsMarkedToCollapse(c)) continue;
                    if (RoofCollapseUtility.WithinRangeOfRoofHolder(c, map)) continue;
                    if (marked.MarkToCollapse(c)) tooFar.Add(c);
                }
            }

            CheckCollapseFlyingRoofs(tooFar, map, marked, visited);
        }

        /// <summary>
        /// Marks every roofed area around <paramref name="nearCells"/> that no longer connects along the roof
        /// to any holder (RimWorld: <c>CheckCollapseFlyingRoofs</c> →
        /// <c>CheckCollapseFlyingRoofAtAndAdjInternal</c>). <paramref name="visited"/> is shared across the
        /// whole despawn event so a region proven connected is not re-walked for each of its cells, which is
        /// what keeps this affordable on a map that is mostly roof.
        /// </summary>
        private static void CheckCollapseFlyingRoofs(IEnumerable<IntVec3> nearCells, Map.Map map,
            RoofCollapseBuffer marked, HashSet<IntVec3> visited)
        {
            foreach (IntVec3 root in nearCells)
            {
                // Cardinal neighbours and the cell itself — RimWorld's GenAdj.CardinalDirectionsAndInside.
                for (int i = 0; i <= GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 c = i == GenAdj.CardinalDirections.Length ? root : root + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.roofGrid.Roofed(c)) continue;
                    if (marked.IsMarkedToCollapse(c) || visited.Contains(c)) continue;
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
        private static void FloodRoofedRegion(IntVec3 c, Map.Map map, RoofCollapseBuffer marked)
        {
            var queue = new Queue<IntVec3>();
            if (!marked.MarkToCollapse(c)) return;
            queue.Enqueue(c);

            while (queue.Count > 0)
            {
                IntVec3 x = queue.Dequeue();
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 neighbour = x + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(neighbour, map)) continue;
                    if (!map.roofGrid.Roofed(neighbour)) continue;
                    if (marked.MarkToCollapse(neighbour)) queue.Enqueue(neighbour);
                }
            }
        }
    }
}
