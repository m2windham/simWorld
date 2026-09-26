using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// One flood-filled enclosed area (RimWorld: <c>Verse.Room</c>) — geometry only; its temperature is its
    /// <see cref="RoomGroup"/>'s, since a Room touching a Door still shares one thermal unit with whatever
    /// lies on the other side (see <see cref="RoomGroup"/>'s own remarks).
    /// </summary>
    public sealed class Room
    {
        internal readonly List<IntVec3> cells = new List<IntVec3>();

        /// <summary>Every Fillage-Full edifice (wall or door) bounding this room, one entry per distinct
        /// Thing regardless of how many of its cells border this room.</summary>
        internal readonly HashSet<Thing> boundaryEdifices = new HashSet<Thing>();

        internal bool touchesMapEdge;
        internal bool anyCellUnroofed;

        public IReadOnlyList<IntVec3> Cells => cells;

        public RoomGroup Group { get; internal set; } = null!;

        /// <summary>True when this room is (part of) the outdoors: it reaches the map's edge, or any of its
        /// own cells has no roof (RimWorld: <c>Room.UsesOutdoorTemperature</c>).</summary>
        public bool TouchesOutside => touchesMapEdge || anyCellUnroofed;

        /// <summary>Reaches the map's own edge, roof or no roof (RimWorld: <c>Room.TouchesMapEdge</c>).
        /// Exposed on its own — distinct from <see cref="TouchesOutside"/> — because
        /// <see cref="AutoBuildRoofAreaSetter"/> asks this half alone: a room with no roof yet is exactly
        /// the case it exists to fix, so gating on <see cref="TouchesOutside"/> there would refuse every
        /// room it was ever meant to roof.</summary>
        public bool TouchesMapEdge => touchesMapEdge;

        public float Temperature => Group.Temperature;

        /// <summary>
        /// Every cell just outside this room's own footprint (RimWorld: <c>Room.BorderCells</c>, there a walk
        /// over the region graph asking, for each of a room's cells' 8 neighbours, whether that neighbour's
        /// own region belongs to this room). This port's <see cref="Room"/> carries no region of its own —
        /// see <see cref="RoomTracker"/>'s remarks on why it floods the edifice grid directly instead — so
        /// the same question is asked directly off the cell set <see cref="RoomTracker.RegenerateAllRooms"/>
        /// already built: a neighbour is a border cell when it is not itself one of <see cref="Cells"/>.
        /// <see cref="AutoBuildRoofAreaSetter"/> is the one reader.
        /// <para/>Deduplicated, unlike RimWorld's own version (a cell bordering several of this room's cells
        /// is yielded once there per bordering cell): every caller only asks whether a border cell holds a
        /// roof or blocks movement, a question a duplicate answers no differently, so this port dedupes for
        /// a cheaper walk rather than reproducing the repeat count.
        /// </summary>
        public IEnumerable<IntVec3> BorderCells
        {
            get
            {
                var own = new HashSet<IntVec3>(cells);
                var yielded = new HashSet<IntVec3>();
                for (int i = 0; i < cells.Count; i++)
                {
                    IntVec3 c = cells[i];
                    for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                    {
                        IntVec3 n = c + GenAdj.AdjacentCells[d];
                        if (own.Contains(n)) continue;
                        if (yielded.Add(n)) yield return n;
                    }
                }
            }
        }
    }
}
