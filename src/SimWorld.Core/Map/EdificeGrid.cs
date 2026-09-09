using System;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>The one edifice (RimWorld: <c>ThingDef.IsEdifice</c>) occupying each cell, if any (RimWorld: <c>Verse.EdificeGrid</c>).</summary>
    public sealed class EdificeGrid
    {
        private readonly Map map;
        private readonly Thing?[] grid;

        public EdificeGrid(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            grid = new Thing?[map.cellIndices.NumGridCells];
        }

        public Thing? this[IntVec3 c] => grid[map.cellIndices.CellToIndex(c)];

        public void Register(Thing edifice)
        {
            if (edifice == null) throw new ArgumentNullException(nameof(edifice));
            foreach (IntVec3 c in edifice.OccupiedRect().Cells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                grid[map.cellIndices.CellToIndex(c)] = edifice;
                // A door spawning/despawning changes whether this cell should be its own Portal region even
                // when walkability itself doesn't flip (a Door replacing plain floor is still Standable
                // either way) — PathGrid's own walkability-flip hook would miss exactly that case, so the
                // region graph needs its own notification here regardless of pathCost.
                map.regionAndRoomUpdater.Notify_DirtyCell(c);
            }
            // An edifice spawning can create or split a room (system 16: Building) — the room tracker
            // recomputes lazily off this, not every tick.
            map.roomTracker.Notify_Dirty();
        }

        /// <summary>
        /// <paramref name="mode"/> gates roof-collapse support checking (system 16: Building — roof collapse):
        /// <see cref="DestroyMode.WillReplace"/> means the edifice is about to be re-spawned as something
        /// else at the same cell in the same moment (RimWorld's own value for exactly this — see
        /// <see cref="Building.Frame.CompleteConstruction"/>) and must not be treated as support genuinely
        /// lost. Every other mode does, including the default a caller with no mode of its own uses.
        /// </summary>
        public void DeRegister(Thing edifice, DestroyMode mode = DestroyMode.Vanish)
        {
            if (edifice == null) throw new ArgumentNullException(nameof(edifice));
            CellRect occupied = edifice.OccupiedRect();
            foreach (IntVec3 c in occupied.Cells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                int i = map.cellIndices.CellToIndex(c);
                if (ReferenceEquals(grid[i], edifice)) grid[i] = null;
                map.regionAndRoomUpdater.Notify_DirtyCell(c);
            }
            map.roomTracker.Notify_Dirty();

            if (mode != DestroyMode.WillReplace && edifice.def.Fillage == FillCategory.Full)
            {
                Building.RoofCollapseUtility.Notify_RoofHolderDespawned(occupied, map);
            }
        }
    }
}
