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
            int index = map.cellIndices.CellToIndex(c);
            bool wasRoofed = grid[index] != null;
            grid[index] = roof;

            // A roof falling in is the one map change a player most needs to see drawn, and a host cannot
            // infer it from terrain or from what is standing on the cell (Map/View — View.MapViewTracker).
            map.mapView.Notify_RoofChanged();

            // Room.anyCellUnroofed/TouchesOutside read this grid, but only RoomTracker.RegenerateAllRooms
            // re-derives them, and only when dirty — an edifice spawning or despawning is what has ever set
            // that flag before now. Building a roof over the last unroofed cell of an enclosed room (or
            // stripping the only roof off one) changes exactly that same flag, with no edifice involved at
            // all, so it has to dirty the tracker itself: without this a room that WorkGiver_BuildRoof/
            // JobDriver_BuildRoof just finished roofing would report TouchesOutside forever, until some
            // unrelated wall happened to spawn or despawn elsewhere on the map (system 95: Roofs).
            //
            // roomTracker may still be null here: Map.ExposeData calls Map.DecodeRoofInto (which replays
            // every saved cell through this same SetRoof) before InitializeAIManagers constructs roomTracker
            // during a load — RoofGrid itself exists earlier, in InitializeGridsExceptPath, for exactly the
            // same reason mapView (used above) does. A freshly loaded map has dirty=true by construction
            // regardless, so there is nothing to notify yet, and nothing lost by skipping it.
            if (wasRoofed != (roof != null)) map.roomTracker?.Notify_Dirty();
        }
    }
}
