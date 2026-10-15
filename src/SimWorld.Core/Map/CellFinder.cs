using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Map
{
    /// <summary>
    /// Finding a cell on a map (RimWorld: <c>Verse.CellFinder</c>), trimmed to the two searches an arrival
    /// needs: a random cell on the map's edge that passes a test, and a random cell near a root that a pawn
    /// could <i>walk</i> to from it.
    ///
    /// <para/><b>Why this exists.</b> A raid used to take its entry cell from <c>rand.Range</c> along a random
    /// edge and its squad's cells from thirty random offsets, and neither was asked whether a pawn could
    /// stand there, let alone walk anywhere from it. On a 200x200 interior whose east edge is rock the entry
    /// was (199, 0, 49), a cell nothing can stand on; every offset near it failed the standable test thirty
    /// times, so <c>RandomClosewalkCellNear</c> handed back the root, and three raiders were spawned onto an
    /// impassable cell. They had no region, so no job could be given to them, and they stood there for four
    /// in-game days with the whole think tree evaluated for each of them on every tick. RimWorld cannot do
    /// this: its entry cell must pass <c>RCellFinder.TryFindRandomPawnEntryCell</c> (standable, unroofed,
    /// reaches the colony) and its closewalk cell must be in the root's own region.
    ///
    /// <para/><b>Not ported.</b> RimWorld's <c>roadChance</c> (a map's road edge tiles are tried first, with a
    /// probability) — this port's maps have no roads, so there is nothing to prefer. Its closewalk search walks
    /// regions out from the root weighted by cell count and bounded by each region's <c>extentsClose</c>; this
    /// port's <see cref="Region"/> carries no extents (see its own remarks), so the same set of cells is found
    /// the other way round: every standable cell in the radius, kept if its region joins the root's.
    /// </summary>
    public static class CellFinder
    {
        /// <summary>RimWorld's own count: random edge cells tried before every edge cell is tried in a shuffled
        /// order (<c>CellFinder.TryFindRandomEdgeCellWith</c>).</summary>
        public const int RandomEdgeTries = 100;

        /// <summary>RimWorld's <c>CellFinder.RandomEdgeCell(Map)</c>: a coin picks a pair of opposite edges, a
        /// second coin picks one of the two, and the other axis is uniform. Draws in that order, so a
        /// seeded run reads the same stream RimWorld's would.</summary>
        public static IntVec3 RandomEdgeCell(Map map, RandomStream rand)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            if (rand.Value < 0.5f)
            {
                int x = rand.Value < 0.5f ? 0 : map.Size.x - 1;
                return new IntVec3(x, 0, rand.Range(0, map.Size.z));
            }
            int z = rand.Value < 0.5f ? 0 : map.Size.z - 1;
            return new IntVec3(rand.Range(0, map.Size.x), 0, z);
        }

        /// <summary>
        /// A random edge cell for which <paramref name="validator"/> is true (RimWorld:
        /// <c>CellFinder.TryFindRandomEdgeCellWith</c>): <see cref="RandomEdgeTries"/> random edge cells first,
        /// which on an ordinary map finds one at once, and then — so that a map with a single usable cell still
        /// finds it — every edge cell in a shuffled order. False only when no edge cell passes at all.
        /// </summary>
        public static bool TryFindRandomEdgeCellWith(Predicate<IntVec3> validator, Map map, RandomStream rand, out IntVec3 result)
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));

            for (int i = 0; i < RandomEdgeTries; i++)
            {
                result = RandomEdgeCell(map, rand);
                if (validator(result)) return true;
            }

            var edge = new List<IntVec3>();
            foreach (IntVec3 c in new CellRect(0, 0, map.Size.x, map.Size.z).EdgeCells) edge.Add(c);
            for (int i = edge.Count - 1; i > 0; i--)
            {
                int j = rand.Range(0, i + 1);
                (edge[i], edge[j]) = (edge[j], edge[i]);
            }
            for (int i = 0; i < edge.Count; i++)
            {
                if (!validator(edge[i])) continue;
                result = edge[i];
                return true;
            }

            result = IntVec3.Invalid;
            return false;
        }

        /// <summary>
        /// A standable cell within <paramref name="radius"/> of <paramref name="root"/> that is in the same
        /// connected area as <paramref name="root"/> (RimWorld: <c>CellFinder.TryRandomClosewalkCellNear</c>), so
        /// that a pawn placed there can walk to wherever a pawn placed on the root can. False when the root has
        /// no region (it is not walkable) or nothing in the radius qualifies.
        /// </summary>
        public static bool TryRandomClosewalkCellNear(
            IntVec3 root, Map map, int radius, RandomStream rand, out IntVec3 result, Predicate<IntVec3>? extraValidator = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region? rootRegion = map.regionGrid.RegionAt(root);
            if (rootRegion == null)
            {
                result = IntVec3.Invalid;
                return false;
            }

            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            int inRadius = GenRadial.NumCellsInRadius(radius);

            // One connectivity answer per region, not per cell: most of the cells in a radius share a handful
            // of regions, and a region that does not join the root's is the expensive question to ask.
            var joinsRoot = new Dictionary<Region, bool> { { rootRegion, true } };
            var candidates = new List<IntVec3>(inRadius);
            for (int i = 0; i < inRadius; i++)
            {
                IntVec3 c = root + pattern[i];
                if (!GenGrid.InBounds(c, map) || !GenGrid.Standable(c, map)) continue;
                if (extraValidator != null && !extraValidator(c)) continue;
                Region? region = map.regionGrid.RegionAt(c);
                if (region == null) continue;
                if (!joinsRoot.TryGetValue(region, out bool joins))
                {
                    // From the candidate's side: a pocket that does not join the root is small, and so is the
                    // search that proves it.
                    joins = RegionTraverser.WithinRegions(region, rootRegion);
                    joinsRoot[region] = joins;
                }
                if (joins) candidates.Add(c);
            }

            if (candidates.Count == 0)
            {
                result = IntVec3.Invalid;
                return false;
            }
            result = candidates[rand.Range(0, candidates.Count)];
            return true;
        }

        /// <summary>
        /// <see cref="TryRandomClosewalkCellNear"/>, falling back to <paramref name="root"/> itself when nothing
        /// qualifies (RimWorld: <c>CellFinder.RandomClosewalkCellNear</c>). The fallback is only as safe as the
        /// root: an arrival's root is a cell that has already been shown standable and reachable, which is the
        /// whole of what makes RimWorld's fallback harmless and what this port's lacked.
        /// </summary>
        public static IntVec3 RandomClosewalkCellNear(
            IntVec3 root, Map map, int radius, RandomStream rand, Predicate<IntVec3>? extraValidator = null)
        {
            return TryRandomClosewalkCellNear(root, map, radius, rand, out IntVec3 result, extraValidator) ? result : root;
        }
    }
}
