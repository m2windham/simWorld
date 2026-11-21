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

        public void SetRoof(IntVec3 c, RoofDef? roof)
        {
            int i = map.cellIndices.CellToIndex(c);
            if (ReferenceEquals(grid[i], roof)) return;
            grid[i] = roof;

            // RimWorld: RoofGrid.SetRoof -> Room.Notify_RoofChanged. A room is outdoors while any cell of it is
            // open (Building.RoomTracker), so a roof built or lost can turn one indoors or out; the tracker
            // re-floods lazily on its next tick. Null only while a load is still decoding the roof string,
            // before the managers exist; the first tick re-floods anyway.
            map.roomTracker?.Notify_Dirty();

            // A roof falling in is the one map change a player most needs to see drawn, and a host cannot
            // infer it from terrain or from what is standing on the cell (Map/View — View.MapViewTracker).
            map.mapView.Notify_RoofChanged();
        }
    }
}
