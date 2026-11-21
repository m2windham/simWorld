using System.Collections.Generic;

namespace SimWorld.Building
{
    /// <summary>
    /// A thermal unit (RimWorld: <c>Verse.RoomGroup</c>). Since doors became thermal barriers every group here
    /// holds exactly one <see cref="Room"/>; the class stays because the temperature, its equalisation data
    /// and every reader (<c>room.Group.Temperature</c>) hang off it. Heat crosses a <see cref="Door"/> only
    /// through <see cref="RoomTracker"/>'s door exchange.
    /// </summary>
    public sealed class RoomGroup
    {
        private readonly Map.Map map;

        internal readonly List<Room> rooms = new List<Room>();

        /// <summary>Valid only while <see cref="UsesOutdoorTemperature"/> is false; equalises toward
        /// outdoor air and its neighbours every tick (see <see cref="RoomTracker"/>), pushed by any <see cref="CompHeatPusherPowered"/> inside.</summary>
        internal float temperature;

        /// <summary>Share of cells under thin (constructed or thin rock) roof, and under thick rock roof.</summary>
        internal float thinRoofShare;

        internal float thickRoofShare;

        /// <summary>The far side of each wall cell bounding this group, one entry per wall cell.</summary>
        internal readonly List<SimWorld.Map.IntVec3> wallBeyondCells = new List<SimWorld.Map.IntVec3>();

        /// <summary>This tick's roof and wall change, computed for every group before any is applied.</summary>
        internal float pendingDelta;

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
