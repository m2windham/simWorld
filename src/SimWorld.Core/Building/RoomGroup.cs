using System.Collections.Generic;

namespace SimWorld.Building
{
    /// <summary>
    /// One or more <see cref="Room"/>s pooled into a single thermal unit (RimWorld: <c>Verse.RoomGroup</c>).
    /// Real RimWorld's Room stops at a closed door but RoomGroup joins rooms connected only by one back into
    /// sharing a temperature, since a door is not a wall. This port's <see cref="Door"/> has no closed state
    /// at all (see its own remarks) — every Door is always open — so the practical effect here is that a
    /// doorway carries zero insulation of its own rather than merely "some": it still separates two Rooms
    /// (their geometry differs) but never keeps their temperatures apart.
    /// </summary>
    public sealed class RoomGroup
    {
        private readonly Map.Map map;

        internal readonly List<Room> rooms = new List<Room>();

        /// <summary>Valid only while <see cref="UsesOutdoorTemperature"/> is false; equalises toward the
        /// map's outdoor temperature every tick, pushed by any <see cref="CompHeatPusherPowered"/> inside.</summary>
        internal float temperature;

        public IReadOnlyList<Room> Rooms => rooms;

        public int CellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < rooms.Count; i++) n += rooms[i].Cells.Count;
                return n;
            }
        }

        /// <summary>True if any member Room touches the outdoors — one exterior door is enough to make the
        /// whole group track outdoor temperature directly, with no equalisation lag.</summary>
        public bool UsesOutdoorTemperature
        {
            get
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i].TouchesOutside) return true;
                }
                return false;
            }
        }

        public float Temperature => UsesOutdoorTemperature ? map.outdoorTemperature : temperature;

        internal RoomGroup(Map.Map map)
        {
            this.map = map;
            temperature = map.outdoorTemperature;
        }

        internal void AddRoom(Room room)
        {
            rooms.Add(room);
            room.Group = this;
        }
    }
}
