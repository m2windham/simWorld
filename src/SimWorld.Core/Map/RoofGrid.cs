using System;

namespace SimWorld.Map
{
    /// <summary>Overhead roof per cell, or none (RimWorld: <c>Verse.RoofGrid</c>).</summary>
    public sealed class RoofGrid
    {
        private readonly Map map;
        private readonly RoofDef?[] grid;

        public RoofGrid(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            grid = new RoofDef?[map.cellIndices.NumGridCells];
        }

        public bool Roofed(IntVec3 c) => grid[map.cellIndices.CellToIndex(c)] != null;

        public RoofDef? RoofAt(IntVec3 c) => grid[map.cellIndices.CellToIndex(c)];

        public void SetRoof(IntVec3 c, RoofDef? roof) => grid[map.cellIndices.CellToIndex(c)] = roof;
    }
}
