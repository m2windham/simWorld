using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Flood-fills <see cref="Room"/>s and <see cref="RoomGroup"/>s from the edifice/roof grids, lazily —
    /// only when something that could change a room's shape has happened (RimWorld: <c>Verse.RegionAndRoomUpdater</c>,
    /// simplified: this port has no region graph to update incrementally (see the AI module's <c>ai.regions</c>
    /// item), so a dirty flag triggers a full re-flood of the whole map instead — the same trade-off
    /// <c>AI.Reachability</c> already makes for the same reason, and cheap for the same reason: it happens
    /// only on an edifice spawning/despawning, never every tick.
    /// </summary>
    public sealed class RoomTracker
    {
        /// <summary>Unsourced (RimWorld's real per-room equalisation rate lives inside math this port does not
        /// have); pinned by a test asserting the trend (equalises faster with less insulation, slower for a
        /// bigger room) rather than the literal.</summary>
        public const float BaseEqualizationRatePerTick = 0.05f;

        private readonly Map.Map map;
        private readonly List<Room> rooms = new List<Room>();
        private readonly List<RoomGroup> groups = new List<RoomGroup>();
        private readonly Dictionary<IntVec3, Room> cellToRoom = new Dictionary<IntVec3, Room>();
        private readonly List<CompHeatPusherPowered> heatPushers = new List<CompHeatPusherPowered>();

        private bool dirty = true;

        private List<int>? pendingSeedX;
        private List<int>? pendingSeedZ;
        private List<float>? pendingSeedTemp;

        public RoomTracker(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public IReadOnlyList<Room> Rooms => rooms;
        public IReadOnlyList<RoomGroup> Groups => groups;

        public Room? RoomAt(IntVec3 cell) => cellToRoom.TryGetValue(cell, out Room? r) ? r : null;

        public void Notify_Dirty() => dirty = true;

        internal void RegisterHeatPusher(CompHeatPusherPowered pusher) => heatPushers.Add(pusher);
        internal void DeregisterHeatPusher(CompHeatPusherPowered pusher) => heatPushers.Remove(pusher);

        public void RoomTrackerTick()
        {
            if (dirty)
            {
                RegenerateAllRooms();
                dirty = false;
                ReapplyPendingTemperatures();
            }

            for (int i = 0; i < groups.Count; i++)
            {
                RoomGroup group = groups[i];
                if (group.UsesOutdoorTemperature)
                {
                    group.temperature = map.outdoorTemperature;
                    continue;
                }
                float insulation = AverageBoundaryInsulation(group);
                float rate = GenMath.Clamp01(BaseEqualizationRatePerTick / (1f + insulation) / Math.Max(1, group.CellCount));
                group.temperature = GenMath.Lerp(group.temperature, map.outdoorTemperature, rate);
            }

            for (int i = 0; i < heatPushers.Count; i++)
            {
                heatPushers[i].PushHeat(this);
            }
        }

        private static float AverageBoundaryInsulation(RoomGroup group)
        {
            float total = 0f;
            int count = 0;
            for (int i = 0; i < group.Rooms.Count; i++)
            {
                foreach (Thing edifice in group.Rooms[i].boundaryEdifices)
                {
                    total += edifice.GetStatValue(StatDefOf.Insulation);
                    count++;
                }
            }
            return count == 0 ? 0f : total / count;
        }

        /// <summary>Re-floods the whole map (RimWorld's own room updater does the same on a full rebuild;
        /// this port never does the cheaper incremental region-patch RimWorld's <c>RegionAndRoomUpdater</c>
        /// can, for the reason in this class's remarks).</summary>
        private void RegenerateAllRooms()
        {
            rooms.Clear();
            groups.Clear();
            cellToRoom.Clear();

            var visited = new HashSet<IntVec3>();
            foreach (IntVec3 seed in map.AllCells)
            {
                if (visited.Contains(seed)) continue;
                if (IsFullEdifice(seed)) continue;

                var room = new Room();
                var queue = new Queue<IntVec3>();
                queue.Enqueue(seed);
                visited.Add(seed);

                while (queue.Count > 0)
                {
                    IntVec3 cur = queue.Dequeue();
                    room.cells.Add(cur);
                    cellToRoom[cur] = room;
                    if (!map.roofGrid.Roofed(cur)) room.anyCellUnroofed = true;
                    if (cur.x == 0 || cur.z == 0 || cur.x == map.Size.x - 1 || cur.z == map.Size.z - 1)
                    {
                        room.touchesMapEdge = true;
                    }

                    foreach (IntVec3 n in GenAdj.CellsAdjacentCardinal(cur))
                    {
                        if (!GenGrid.InBounds(n, map))
                        {
                            room.touchesMapEdge = true;
                            continue;
                        }
                        if (IsFullEdifice(n))
                        {
                            Thing edifice = map.edificeGrid[n]!;
                            room.boundaryEdifices.Add(edifice);
                            continue;
                        }
                        if (visited.Contains(n)) continue;
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }

                rooms.Add(room);
            }

            BuildRoomGroups();
        }

        private bool IsFullEdifice(IntVec3 cell)
        {
            Thing? edifice = map.edificeGrid[cell];
            return edifice != null && edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>Joins Rooms that share a boundary <see cref="Door"/> into one <see cref="RoomGroup"/>.</summary>
        private void BuildRoomGroups()
        {
            var doorToRooms = new Dictionary<Door, List<Room>>();
            foreach (Room room in rooms)
            {
                foreach (Thing edifice in room.boundaryEdifices)
                {
                    if (edifice is Door door)
                    {
                        if (!doorToRooms.TryGetValue(door, out List<Room>? list))
                        {
                            list = new List<Room>();
                            doorToRooms[door] = list;
                        }
                        list.Add(room);
                    }
                }
            }

            var assigned = new HashSet<Room>();
            foreach (Room start in rooms)
            {
                if (assigned.Contains(start)) continue;
                var group = new RoomGroup(map);
                var queue = new Queue<Room>();
                queue.Enqueue(start);
                assigned.Add(start);
                while (queue.Count > 0)
                {
                    Room cur = queue.Dequeue();
                    group.AddRoom(cur);
                    foreach (Thing edifice in cur.boundaryEdifices)
                    {
                        if (edifice is Door door && doorToRooms.TryGetValue(door, out List<Room>? linked))
                        {
                            foreach (Room other in linked)
                            {
                                if (assigned.Add(other)) queue.Enqueue(other);
                            }
                        }
                    }
                }
                groups.Add(group);
            }
        }

        // ---- Scribe ----
        // Room/RoomGroup shape is pure geometry, deterministically re-derived by RegenerateAllRooms — like
        // AI.Reachability's own cache, it is never saved. Only the temperature each enclosed group actually
        // reached is real simulation state; it is saved as (a representative cell, that group's temperature)
        // per group and reattached once rooms regenerate after load.

        public void ExposeTemperatures()
        {
            List<int>? seedX;
            List<int>? seedZ;
            List<float>? seedTemp;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                seedX = new List<int>();
                seedZ = new List<int>();
                seedTemp = new List<float>();
                foreach (RoomGroup group in groups)
                {
                    if (group.UsesOutdoorTemperature || group.Rooms.Count == 0 || group.Rooms[0].Cells.Count == 0) continue;
                    IntVec3 c = group.Rooms[0].Cells[0];
                    seedX.Add(c.x);
                    seedZ.Add(c.z);
                    seedTemp.Add(group.temperature);
                }
            }
            else
            {
                seedX = null;
                seedZ = null;
                seedTemp = null;
            }

            Scribe_Collections.Look(ref seedX, "roomTempSeedX", LookMode.Value);
            Scribe_Collections.Look(ref seedZ, "roomTempSeedZ", LookMode.Value);
            Scribe_Collections.Look(ref seedTemp, "roomTempSeedTemp", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                pendingSeedX = seedX;
                pendingSeedZ = seedZ;
                pendingSeedTemp = seedTemp;
                dirty = true;
            }
        }

        private void ReapplyPendingTemperatures()
        {
            if (pendingSeedX == null || pendingSeedZ == null || pendingSeedTemp == null) return;
            int count = Math.Min(pendingSeedX.Count, Math.Min(pendingSeedZ.Count, pendingSeedTemp.Count));
            for (int i = 0; i < count; i++)
            {
                var cell = new IntVec3(pendingSeedX[i], 0, pendingSeedZ[i]);
                Room? room = RoomAt(cell);
                if (room != null && !room.TouchesOutside)
                {
                    room.Group.temperature = pendingSeedTemp[i];
                }
            }
            pendingSeedX = null;
            pendingSeedZ = null;
            pendingSeedTemp = null;
        }
    }
}
