using System;

namespace SimWorld.Map
{
    /// <summary>
    /// Converts between a cell and its flat grid index (RimWorld: <c>Verse.CellIndices</c>). Every per-cell
    /// grid (terrain, path cost, things) is a flat array keyed by this index rather than a 2D array.
    /// </summary>
    public sealed class CellIndices
    {
        public int mapSizeX { get; }
        public int mapSizeZ { get; }

        public CellIndices(int mapSizeX, int mapSizeZ)
        {
            if (mapSizeX <= 0) throw new ArgumentOutOfRangeException(nameof(mapSizeX));
            if (mapSizeZ <= 0) throw new ArgumentOutOfRangeException(nameof(mapSizeZ));
            this.mapSizeX = mapSizeX;
            this.mapSizeZ = mapSizeZ;
        }

        public int NumGridCells => mapSizeX * mapSizeZ;

        public int CellToIndex(IntVec3 c) => c.z * mapSizeX + c.x;

        public int CellToIndex(int x, int z) => z * mapSizeX + x;

        public IntVec3 IndexToCell(int index) => new IntVec3(index % mapSizeX, 0, index / mapSizeX);
    }
}
