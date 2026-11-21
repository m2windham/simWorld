using System;
using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Marks the build-roof area over a room its builders have just closed in (RimWorld:
    /// <c>Verse.AutoBuildRoofAreaSetter</c>, read from <c>josh-m/RW-Decompile/Verse/AutoBuildRoofAreaSetter.cs</c>;
    /// its callers <c>Verse/Room.cs</c> <c>Notify_RoomShapeOrContainedBedsChanged</c>,
    /// <c>Verse/RoomGroupTempTracker.cs</c> <c>RoomChanged</c> and <c>Verse/Map.cs</c> <c>MapPreTick</c>).
    /// <see cref="WorkGiver_BuildRoof"/> then sends builders to roof what this marks.
    ///
    /// <para/><b>Why this exists here.</b> Nothing a settlement built was ever roofed: roofs came only from map
    /// generation and from roof collapse. <see cref="RoomTracker"/> gives a room with an open cell outdoor air,
    /// so the compound's walled dormitory was the outdoors and doors kept nothing in. RimWorld does not ask its
    /// player to roof a room either; this class is how a walled room gets its roof there, and it is the same
    /// here.
    ///
    /// <para/><b>Translated: "the player's".</b> RimWorld roofs a room only when a holder on its border is the
    /// player's (<c>Faction.OfPlayer</c>) and none belongs to another faction. This map has no player faction
    /// to compare with; a building takes its builder's faction (<see cref="Frame.CompleteConstruction"/>), so
    /// the rule here is: some holder on the border has a faction, and every holder that has one has the same
    /// one. A ruin's walls, and rock, have none, so a ruin or a cave is never roofed, as in RimWorld.
    ///
    /// <para/><b>Not ported.</b> The player's no-roof area (nobody paints one here, so every cell is treated as
    /// outside it); <c>BuildingProperties.allowAutoroof</c> (no shipped building opts out); the 26-region cap
    /// beside the 320-cell one (a <see cref="Room"/> here is cells, not regions); the portal-room check (a door is
    /// never a <see cref="Room"/> here); <c>TryGenerateAreaOnImpassable</c> (no caller in the decompile); the
    /// temporary-roof mote.
    /// </summary>
    public sealed class AutoBuildRoofAreaSetter
    {
        /// <summary>A room bigger than this is not auto-roofed (RimWorld: the <c>room.CellCount &gt; 320</c>
        /// test).</summary>
        public const int MaxRoomCells = 320;

        private readonly Map.Map map;
        private readonly List<Room> queuedGenerateRooms = new List<Room>();
        private readonly HashSet<IntVec3> innerCells = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> cellsToRoof = new HashSet<IntVec3>();

        public AutoBuildRoofAreaSetter(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>Queues a room whose shape has just changed (RimWorld: called from
        /// <c>Room.Notify_RoomShapeOrContainedBedsChanged</c>; here from <see cref="RoomTracker"/>, which
        /// rebuilds every room at once).</summary>
        public void TryGenerateAreaFor(Room room) => queuedGenerateRooms.Add(room);

        /// <summary>RimWorld: run first in <c>Map.MapPreTick</c>.</summary>
        public void AutoBuildRoofAreaSetterTick_First() => ResolveQueuedGenerateRoofs();

        public void ResolveQueuedGenerateRoofs()
        {
            for (int i = 0; i < queuedGenerateRooms.Count; i++) TryGenerateAreaNow(queuedGenerateRooms[i]);
            queuedGenerateRooms.Clear();
        }

        private void TryGenerateAreaNow(Room room)
        {
            // RimWorld's Dereferenced test is not needed: the queue is resolved in the same rebuild that made
            // these rooms, so none of them has been replaced yet.
            if (room.touchesMapEdge) return;
            if (room.cells.Count > MaxRoomCells) return;

            // Who built the border. One faction's holder is needed, and any other faction's holder stops it.
            Faction? owner = null;
            for (int i = 0; i < room.cells.Count; i++)
            {
                IntVec3 cell = room.cells[i];
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 border = cell + GenAdj.AdjacentCells[d];
                    if (ReferenceEquals(map.roomTracker.RoomAt(border), room)) continue;
                    Thing? holder = RoofHolderOrImpassable(border);
                    Faction? faction = holder?.Faction;
                    if (faction == null) continue;
                    if (owner == null) owner = faction;
                    else if (!ReferenceEquals(owner, faction)) return;
                }
            }
            if (owner == null) return;

            // The room's own cells, and the whole footprint of any multi-cell holder touching them.
            innerCells.Clear();
            for (int i = 0; i < room.cells.Count; i++)
            {
                IntVec3 cell = room.cells[i];
                innerCells.Add(cell);
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 c = cell + GenAdj.AdjacentCells[d];
                    if (!GenGrid.InBounds(c, map)) continue;
                    Thing? holder = RoofHolderOrImpassable(c);
                    if (holder == null || (holder.def.size.x <= 1 && holder.def.size.z <= 1)) continue;
                    foreach (IntVec3 occupied in holder.OccupiedRect().ClipInsideMap(map).Cells) innerCells.Add(occupied);
                }
            }

            // Those cells, and every holder round them, so the walls are roofed with the room.
            cellsToRoof.Clear();
            foreach (IntVec3 inner in innerCells)
            {
                cellsToRoof.Add(inner);
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 c = inner + GenAdj.AdjacentCells[d];
                    if (GenGrid.InBounds(c, map) && RoofHolderOrImpassable(c) != null) cellsToRoof.Add(c);
                }
            }

            foreach (IntVec3 c in cellsToRoof)
            {
                if (map.roofGrid.RoofAt(c) != null) continue;
                if (!RoofCollapseUtility.WithinRangeOfRoofHolder(c, map, assumeNonNoRoofCellsAreRoofed: true)) continue;
                map.areaManager.BuildRoof[c] = true;
            }
            innerCells.Clear();
            cellsToRoof.Clear();
        }

        /// <summary>The first thing on <paramref name="c"/> that holds roof or blocks movement (RimWorld:
        /// <c>GridsUtility.GetRoofHolderOrImpassable</c>). "Holds roof" is this port's
        /// <see cref="RoofCollapseUtility.HoldsRoof"/>: a Fillage-Full edifice.</summary>
        private Thing? RoofHolderOrImpassable(IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return null;
            if (RoofCollapseUtility.HoldsRoof(c, map)) return map.edificeGrid[c];
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.passability == Traversability.Impassable) return things[i];
            }
            return null;
        }
    }
}
