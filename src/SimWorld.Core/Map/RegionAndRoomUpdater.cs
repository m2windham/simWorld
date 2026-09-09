using System;
using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// Owns the region graph's dirty-cell tracking and rebuilds it lazily (RimWorld: <c>Verse.RegionAndRoomUpdater</c>)
    /// — a full flood the first time anything asks, an incremental rebuild of only the regions a passability
    /// change actually touches after that. Nothing calls this every tick; a query that needs a fresh graph
    /// (<see cref="AI.Reachability"/> today) calls <see cref="RebuildIfNeeded"/> itself, same as
    /// <c>Reachability</c>'s own flood-fill cache used to check its version stamp lazily.
    ///
    /// <b>Scope</b>: this port's Room/RoomGroup concept already exists independently as
    /// <see cref="Building.RoomTracker"/>, flood-filled off the edifice grid rather than off this region
    /// graph (see that class's own remarks for why — in short, it predates this pass and this pass's brief
    /// keeps <c>Building/**</c> off limits, since another lane owns it concurrently). RimWorld's real
    /// <c>RegionAndRoomUpdater</c> rebuilds Rooms from the region graph in the same pass as Regions; this
    /// port's half only ever touches Regions. The class keeps RimWorld's name regardless, since nothing
    /// about the Room half of that name changes what this half does.
    /// </summary>
    public sealed class RegionAndRoomUpdater
    {
        private readonly Map map;
        private readonly RegionGrid regionGrid;

        private readonly HashSet<IntVec3> dirtyCells = new HashSet<IntVec3>();
        private bool initialBuildDone;

        /// <summary>How many regions the most recent rebuild pass created. RimWorld has no such counter —
        /// it exists here purely so a test can assert that one local passability change regenerates a
        /// small, local slice of the graph rather than the whole map, by asserting a bound rather than
        /// pinning an exact literal.</summary>
        public int RegionsCreatedLastRebuild { get; private set; }

        /// <summary>Bumped every time a rebuild actually runs (<see cref="RebuildAll"/> or
        /// <see cref="RebuildDirty"/>) — never on a no-op <see cref="RebuildIfNeeded"/> call. RimWorld has no
        /// such counter either; this one exists so <see cref="AI.RegionPathCorridorCache"/> (system 9's
        /// path-sharing pass) has a cheap, correct signal for "the graph might have changed under me": every
        /// rebuild tears down and re-floods whichever <see cref="Region"/>s it touches rather than patching
        /// them in place, so a cache holding a stale <see cref="Region"/> reference from before a rebuild can
        /// be holding parent pointers through regions that no longer exist or no longer link the way they used
        /// to — this version number is the one-sentence invalidation trigger that cache clears itself on.</summary>
        public int Version { get; private set; }

        public RegionAndRoomUpdater(Map map, RegionGrid regionGrid)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.regionGrid = regionGrid ?? throw new ArgumentNullException(nameof(regionGrid));
        }

        /// <summary>A passability-relevant change touched this cell — mark it and its cardinal neighbours
        /// dirty. The neighbours matter too: a region or a link that only *touches* the changed cell from
        /// next door can also need rebuilding (a wall removed on the far side of a doorway, say, changes
        /// what that portal region should link to, even though the doorway cell itself never changed).
        /// RimWorld's real updater does the same cell-plus-neighbourhood expansion before regenerating.</summary>
        public void Notify_DirtyCell(IntVec3 c)
        {
            dirtyCells.Add(c);
            for (int d = 0; d < GenAdj.CardinalDirections.Length; d++)
            {
                dirtyCells.Add(c + GenAdj.CardinalDirections[d]);
            }
        }

        /// <summary>Brings the region graph up to date: a full flood the very first time this is called for
        /// this map, an incremental rebuild of only the dirty regions every time after that. A no-op call
        /// (nothing dirty, already built once) touches no allocation and no region.</summary>
        public void RebuildIfNeeded()
        {
            if (!initialBuildDone)
            {
                RebuildAll();
                return;
            }
            if (dirtyCells.Count > 0) RebuildDirty();
        }

        private void RebuildAll()
        {
            regionGrid.Clear();
            dirtyCells.Clear();
            RegionsCreatedLastRebuild = 0;

            var maker = new RegionMaker(map, regionGrid);
            int n = map.cellIndices.NumGridCells;
            for (int i = 0; i < n; i++)
            {
                if (regionGrid.RegionAtIndex(i) != null || !map.pathGrid.WalkableFast(i)) continue;
                if (maker.TryGenerateRegionFrom(map.cellIndices.IndexToCell(i))) RegionsCreatedLastRebuild++;
            }
            initialBuildDone = true;
            Version++;
        }

        /// <summary>
        /// Every region that touches a dirty cell is torn down completely and re-flooded from scratch —
        /// regions are never patched cell by cell in place, matching RimWorld's own incremental rebuild,
        /// which regenerates whichever regions the dirty cells fall in rather than editing them piecemeal.
        /// Every region that does <i>not</i> touch a dirty cell is left completely alone: same object, same
        /// id, same cells, same <see cref="Region.Links"/> list apart from links a torn-down neighbour had
        /// to it (<see cref="RegionGrid.RemoveRegion"/> repairs those, and <see cref="RegionMaker"/>
        /// reconnects fresh ones as it re-floods) — that is the incremental-invalidation contract this pass
        /// exists to deliver.
        /// </summary>
        private void RebuildDirty()
        {
            RegionsCreatedLastRebuild = 0;

            var cells = new List<IntVec3>(dirtyCells);
            dirtyCells.Clear();
            // Deterministic processing order regardless of HashSet enumeration order, so region ids and the
            // exact split of a torn-down area into new regions reproduce identically for the same map.
            cells.Sort((a, b) => map.cellIndices.CellToIndex(a).CompareTo(map.cellIndices.CellToIndex(b)));

            var toClear = new List<Region>();
            var seen = new HashSet<Region>();
            foreach (IntVec3 c in cells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                Region? r = regionGrid.RegionAt(c);
                if (r != null && seen.Add(r)) toClear.Add(r);
            }

            var reflood = new List<IntVec3>();
            foreach (Region r in toClear)
            {
                reflood.AddRange(r.Cells);
                regionGrid.RemoveRegion(r);
            }
            foreach (IntVec3 c in cells)
            {
                // Cells that never had a region (freshly walkable, e.g. a mined wall) still need flooding.
                if (GenGrid.InBounds(c, map)) reflood.Add(c);
            }
            reflood.Sort((a, b) => map.cellIndices.CellToIndex(a).CompareTo(map.cellIndices.CellToIndex(b)));

            var maker = new RegionMaker(map, regionGrid);
            var processed = new HashSet<IntVec3>();
            foreach (IntVec3 c in reflood)
            {
                if (!processed.Add(c) || regionGrid.RegionAt(c) != null) continue;
                if (!map.pathGrid.Walkable(c)) continue;
                if (maker.TryGenerateRegionFrom(c)) RegionsCreatedLastRebuild++;
            }
            Version++;
        }
    }
}
