using System;
using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// Every map cell's <see cref="Region"/>, if it has one (RimWorld: <c>Verse.RegionGrid</c>) — one
    /// <see cref="Region"/> reference per cell, many cells sharing the same reference, mirroring
    /// <see cref="PathGrid"/>'s own flat-array-per-cell shape. A cell with no region is either impassable or
    /// off the map. Mutating methods are <c>internal</c>: only <see cref="RegionMaker"/> and
    /// <see cref="RegionAndRoomUpdater"/> (both in this assembly) build or tear down regions; everything
    /// else only ever reads.
    /// </summary>
    public sealed class RegionGrid
    {
        private readonly Map map;
        private readonly Region?[] grid;
        private readonly List<Region> allRegions = new List<Region>();

        public RegionGrid(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            grid = new Region?[map.cellIndices.NumGridCells];
        }

        public IReadOnlyList<Region> AllRegions => allRegions;

        public Region? RegionAt(IntVec3 c) => GenGrid.InBounds(c, map) ? grid[map.cellIndices.CellToIndex(c)] : null;

        internal Region? RegionAtIndex(int i) => grid[i];

        internal void SetRegionAt(IntVec3 c, Region region) => grid[map.cellIndices.CellToIndex(c)] = region;

        internal void AddRegion(Region region) => allRegions.Add(region);

        /// <summary>Tears a region down completely: clears its cells off the grid, removes it from
        /// <see cref="AllRegions"/>, and unlinks it from every neighbour it had a <see cref="RegionLink"/>
        /// to — the neighbour survives untouched (same object, same cells), only the stale link to this
        /// region is dropped from its list. The region's own cells are left unassigned for whatever new
        /// region(s) <see cref="RegionMaker"/> floods them into next.</summary>
        internal void RemoveRegion(Region region)
        {
            for (int i = 0; i < region.cells.Count; i++)
            {
                int idx = map.cellIndices.CellToIndex(region.cells[i]);
                if (ReferenceEquals(grid[idx], region)) grid[idx] = null;
            }
            for (int i = 0; i < region.links.Count; i++)
            {
                Region? other = region.links[i].GetOtherRegion(region);
                other?.links.Remove(region.links[i]);
            }
            region.links.Clear();
            allRegions.Remove(region);
        }

        /// <summary>Discards every region — used only for a full rebuild (<see cref="RegionAndRoomUpdater"/>'s
        /// first-ever build for this map).</summary>
        internal void Clear()
        {
            Array.Clear(grid, 0, grid.Length);
            allRegions.Clear();
        }
    }
}
