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

        public float Temperature => Group.Temperature;
    }
}
