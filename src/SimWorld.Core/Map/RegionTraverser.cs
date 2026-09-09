using System;
using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// Breadth-first search over the region graph (RimWorld: <c>Verse.RegionTraverser</c>) — a handful of
    /// coarse hops across <see cref="Region"/>s joined by <see cref="RegionLink"/>s, versus visiting every
    /// cell on a shortest path the way <see cref="AI.PathFinder"/> must. A rolling per-search stamp
    /// (<see cref="Region.reachedIndex"/>) plays the same role <see cref="AI.PathFinder"/>'s own
    /// <c>generation</c> field plays per cell: no fresh "visited" set to allocate or clear on every query.
    /// </summary>
    public static class RegionTraverser
    {
        private static int reachedIndexCounter;
        [ThreadStatic] private static Queue<Region>? queue;

        /// <summary>Is <paramref name="to"/> reachable from <paramref name="from"/> by crossing only regions
        /// of a type in <paramref name="traversableRegionTypes"/>? (RimWorld's real
        /// <c>RegionTraverser.WithinRegions</c> distance-limits its search too; nothing in this port needs
        /// that yet, so this version searches the whole connected component.)</summary>
        public static bool WithinRegions(Region from, Region to, RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (ReferenceEquals(from, to)) return true;

            int stamp = ++reachedIndexCounter;
            Queue<Region> q = queue ??= new Queue<Region>();
            q.Clear();

            from.reachedIndex = stamp;
            q.Enqueue(from);

            while (q.Count > 0)
            {
                Region cur = q.Dequeue();
                IReadOnlyList<RegionLink> links = cur.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    Region? other = links[i].GetOtherRegion(cur);
                    if (other == null || other.reachedIndex == stamp) continue;
                    if ((other.type & traversableRegionTypes) == 0) continue;
                    if (ReferenceEquals(other, to)) return true;
                    other.reachedIndex = stamp;
                    q.Enqueue(other);
                }
            }
            return false;
        }
    }
}
