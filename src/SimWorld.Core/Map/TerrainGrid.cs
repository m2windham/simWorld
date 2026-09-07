using System;

namespace SimWorld.Map
{
    /// <summary>
    /// The ground of every cell (RimWorld: <c>Verse.TerrainGrid</c>). Each cell has a visible top layer and,
    /// once something is built or dug into that top layer, an under layer waiting to be revealed again.
    /// </summary>
    public sealed class TerrainGrid
    {
        private readonly Map map;
        private readonly TerrainDef[] topGrid;
        private readonly TerrainDef?[] underGrid;

        public TerrainGrid(Map map, TerrainDef fill)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            if (fill == null) throw new ArgumentNullException(nameof(fill));
            int n = map.cellIndices.NumGridCells;
            topGrid = new TerrainDef[n];
            underGrid = new TerrainDef?[n];
            for (int i = 0; i < n; i++)
            {
                topGrid[i] = fill;
            }
        }

        public TerrainDef TerrainAt(IntVec3 c) => topGrid[map.cellIndices.CellToIndex(c)];

        public TerrainDef? UnderTerrainAt(IntVec3 c) => underGrid[map.cellIndices.CellToIndex(c)];

        /// <summary>Sets the visible terrain; the previous top becomes the under layer.</summary>
        public void SetTerrain(IntVec3 c, TerrainDef newTerrain)
        {
            if (newTerrain == null) throw new ArgumentNullException(nameof(newTerrain));
            int i = map.cellIndices.CellToIndex(c);
            TerrainDef old = topGrid[i];
            if (old.layerable)
            {
                underGrid[i] = old;
            }
            topGrid[i] = newTerrain;
            map.pathGrid.RecalculatePerceivedPathCostAt(c);
        }

        /// <summary>Removes the top layer, revealing the under layer (or leaving the same terrain when there is none).</summary>
        public void RemoveTopLayer(IntVec3 c)
        {
            int i = map.cellIndices.CellToIndex(c);
            TerrainDef? under = underGrid[i];
            if (under == null) return;
            topGrid[i] = under;
            underGrid[i] = null;
            map.pathGrid.RecalculatePerceivedPathCostAt(c);
        }

        /// <summary>Sets the top layer with none of <see cref="SetTerrain"/>'s side effects; Map uses this while rebuilding a loaded grid.</summary>
        internal void SetTerrainDirect(IntVec3 c, TerrainDef def) => topGrid[map.cellIndices.CellToIndex(c)] = def;
    }
}
