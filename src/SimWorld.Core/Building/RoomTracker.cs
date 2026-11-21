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
    /// simplified: a dirty flag triggers a full re-flood of the whole map rather than an incremental update.
    /// That is cheap because it happens only on an edifice spawning or despawning, never every tick.
    /// <para/>
    /// A region graph now exists (<c>Map.RegionGrid</c>/<c>Map.RegionAndRoomUpdater</c>) and updates
    /// incrementally, so this could be rebuilt on top of it — but rooms and regions answer different
    /// questions (thermal enclosure vs. reachability partitioning), and this tracker was deliberately left
    /// alone rather than turned into a second consumer of the graph in the same pass that introduced it.
    /// An earlier version of this comment said rooms made "the same trade-off <c>AI.Reachability</c> already
    /// makes"; that stopped being true the moment reachability moved onto the region graph.
    /// </summary>
    public sealed class RoomTracker
    {
        /// <summary>RimWorld (<c>RoomGroupTempTracker</c>, B18-era decompile): per-tick pull toward outdoor air
        /// through a constructed or thin rock roof, per unit of the room's thin-roofed share.</summary>
        public const float ThinRoofEqualizationRate = 5e-5f;

        /// <summary>RimWorld: per-tick pull toward <see cref="DeepEqualizationTemperature"/> under thick rock
        /// roof, only while the room is colder than that.</summary>
        public const float DeepEqualizationRate = 5e-5f;

        /// <summary>RimWorld: the temperature thick rock settles a cold room toward.</summary>
        public const float DeepEqualizationTemperature = 15f;

        /// <summary>RimWorld: per-tick exchange through one wall cell, divided by the room's cell count.</summary>
        public const float WallEqualizationRate = 0.00017f;

        /// <summary>RimWorld: an outdoor difference beyond this many degrees counts five times over.</summary>
        public const float OutdoorDifferenceKnee = 100f;

        public const float OutdoorDifferenceBeyondKneeFactor = 5f;

        /// <summary>RimWorld (<c>Building_Door</c>): a shut door exchanges heat with its neighbours every 375
        /// ticks, an open one every 34, at rate 1 (each neighbouring room moves gap / its cell count toward
        /// the neighbours' mean).</summary>
        public const int ClosedDoorEqualizeIntervalTicks = 375;

        public const int OpenDoorEqualizeIntervalTicks = 34;

        private readonly Map.Map map;
        private readonly List<Room> rooms = new List<Room>();
        private readonly List<RoomGroup> groups = new List<RoomGroup>();
        private readonly Dictionary<IntVec3, Room> cellToRoom = new Dictionary<IntVec3, Room>();
        private readonly List<CompHeatPusherPowered> heatPushers = new List<CompHeatPusherPowered>();
        private readonly List<Door> doors = new List<Door>();
        private readonly List<RoomGroup> tmpDoorGroups = new List<RoomGroup>();

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

        /// <summary>
        /// One tick of the room temperature model (RimWorld: <c>RoomGroupTempTracker.EqualizeTemperature</c>
        /// for roofs and walls, and <c>Building_Door</c>'s exchange through the door).
        /// <para/><b>Translation.</b> RimWorld applies the roof and wall terms every 120 ticks at 120 times the
        /// per-tick rate, and a door's exchange every 375 (shut) or 34 (open) ticks at full strength; this port
        /// spreads the same rates evenly over every tick, which needs no interval phase to save. Not ported:
        /// the share of open sky (a room here uses outdoor air outright once any cell is unroofed, so RimWorld's
        /// no-roof term is always zero), RimWorld's sampling of a fifth of the wall cells per pass (all are
        /// averaged here every tick), and vents and coolers.
        /// </summary>
        public void RoomTrackerTick()
        {
            if (dirty)
            {
                RegenerateAllRooms();
                dirty = false;
                ReapplyPendingTemperatures();
            }

            float outdoor = map.outdoorTemperature;
            for (int i = 0; i < groups.Count; i++)
            {
                RoomGroup group = groups[i];
                if (group.UsesOutdoorTemperature)
                {
                    group.temperature = outdoor;
                    continue;
                }
                group.pendingDelta = RoofAndWallDelta(group, outdoor);
            }
            for (int i = 0; i < groups.Count; i++)
            {
                RoomGroup group = groups[i];
                if (group.UsesOutdoorTemperature) continue;
                group.temperature = GenMath.Clamp(group.temperature + group.pendingDelta, -273.15f, 2000f);
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = 0; i < doors.Count; i++)
            {
                Door door = doors[i];
                if (door.Destroyed || !door.Spawned) continue;
                if (AnyPawnAt(door.Position)) door.Notify_Occupied(now);
                int interval = door.IsOpenAt(now) ? OpenDoorEqualizeIntervalTicks : ClosedDoorEqualizeIntervalTicks;
                EqualizeThroughDoor(door, 1f / interval);
            }

            for (int i = 0; i < heatPushers.Count; i++)
            {
                heatPushers[i].PushHeat(this);
            }
        }

        /// <summary>RimWorld's outdoor difference, counted five times over beyond 100 degrees.</summary>
        private static float AdjustedOutdoorDifference(float diff)
        {
            float abs = Math.Abs(diff);
            if (abs <= OutdoorDifferenceKnee) return diff;
            float adjusted = OutdoorDifferenceKnee + (abs - OutdoorDifferenceKnee) * OutdoorDifferenceBeyondKneeFactor;
            return diff < 0f ? -adjusted : adjusted;
        }

        private float RoofAndWallDelta(RoomGroup group, float outdoor)
        {
            float t = group.temperature;
            float delta = AdjustedOutdoorDifference(outdoor - t) * group.thinRoofShare * ThinRoofEqualizationRate;
            if (t < DeepEqualizationTemperature)
            {
                delta += (DeepEqualizationTemperature - t) * group.thickRoofShare * DeepEqualizationRate;
            }

            List<IntVec3> beyond = group.wallBeyondCells;
            if (beyond.Count > 0)
            {
                // mean difference x wall-cell count = the plain sum of differences.
                float sum = 0f;
                for (int i = 0; i < beyond.Count; i++)
                {
                    sum += AirBeyondWall(group, beyond[i], outdoor) - t;
                }
                delta += sum * WallEqualizationRate / Math.Max(1, group.CellCount);
            }
            return delta;
        }

        /// <summary>The air on the far side of a wall: that room's air, outdoor air off the map's edge, and the
        /// midpoint of this room and outdoors when the far side is more wall or this same room (RimWorld's
        /// fallback for a cell with no air of its own).</summary>
        private float AirBeyondWall(RoomGroup group, IntVec3 beyond, float outdoor)
        {
            if (!GenGrid.InBounds(beyond, map)) return outdoor;
            Room? room = RoomAt(beyond);
            if (room == null || room.Group == group) return (group.temperature + outdoor) / 2f;
            return room.Group.Temperature;
        }

        private bool AnyPawnAt(IntVec3 cell)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawns.Pawn) return true;
            }
            return false;
        }

        /// <summary>
        /// Pulls the rooms around a door toward the plain mean of its cardinal neighbours' air (RimWorld:
        /// <c>GenTemperature.EqualizeTemperaturesThroughBuilding</c>, as described from the decompile: each
        /// neighbour cell counts once, so a room touching the door on two sides counts twice; outdoor air
        /// counts toward the mean but never moves; each room moves its gap times <paramref name="rate"/> over
        /// its cell count, all scaled back together so that none overshoots the mean).
        /// </summary>
        private void EqualizeThroughDoor(Door door, float rate)
        {
            tmpDoorGroups.Clear();
            float sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 c = door.Position + GenAdj.CardinalDirections[i];
                if (!GenGrid.InBounds(c, map)) continue;
                Room? room = RoomAt(c);
                if (room == null) continue;
                tmpDoorGroups.Add(room.Group);
                sum += room.Group.Temperature;
            }
            if (tmpDoorGroups.Count < 2)
            {
                tmpDoorGroups.Clear();
                return;
            }
            float mean = sum / tmpDoorGroups.Count;

            float scale = 1f;
            for (int i = 0; i < tmpDoorGroups.Count; i++)
            {
                RoomGroup g = tmpDoorGroups[i];
                if (g.UsesOutdoorTemperature) continue;
                float gap = mean - g.temperature;
                float step = gap * rate / Math.Max(1, g.CellCount);
                if (step != 0f && Math.Abs(step) > Math.Abs(gap))
                {
                    float f = Math.Abs(gap / step);
                    if (f < scale) scale = f;
                }
            }
            for (int i = 0; i < tmpDoorGroups.Count; i++)
            {
                RoomGroup g = tmpDoorGroups[i];
                if (g.UsesOutdoorTemperature) continue;
                g.temperature += (mean - g.temperature) * scale * rate / Math.Max(1, g.CellCount);
            }
            tmpDoorGroups.Clear();
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

        /// <summary>
        /// One <see cref="RoomGroup"/> per <see cref="Room"/>: a door separates temperatures, as a closed door
        /// does in RimWorld, and heat crosses it only through <see cref="EqualizeThroughDoor"/>. (This port used
        /// to merge rooms through every door, so any house with a door to the outside simply was the outdoors.)
        /// Also records each group's roof shares and the far side of each of its wall cells, and the doors.
        /// </summary>
        private void BuildRoomGroups()
        {
            doors.Clear();
            var seenDoors = new HashSet<Door>();
            foreach (Room room in rooms)
            {
                var group = new RoomGroup(map);
                group.AddRoom(room);
                groups.Add(group);
                RegenerateEqualizationData(group, room);
                foreach (Thing edifice in room.boundaryEdifices)
                {
                    if (edifice is Door door && seenDoors.Add(door)) doors.Add(door);
                }
            }
            doors.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
        }

        /// <summary>Roof shares and wall cells for the roof and wall terms (RimWorld:
        /// <c>RoomGroupTempTracker.RegenerateEqualizationData</c>). Each wall cell is counted once, by the first
        /// room cell to reach it, and sampled on its far side; a doorway is not a wall.</summary>
        private void RegenerateEqualizationData(RoomGroup group, Room room)
        {
            int thick = 0, thin = 0;
            var walls = new HashSet<IntVec3>();
            group.wallBeyondCells.Clear();
            foreach (IntVec3 cell in room.cells)
            {
                RoofDef? roof = map.roofGrid.RoofAt(cell);
                if (roof != null)
                {
                    if (roof.isThickRoof) thick++;
                    else thin++;
                }
                for (int d = 0; d < 4; d++)
                {
                    IntVec3 dir = GenAdj.CardinalDirections[d];
                    IntVec3 wall = cell + dir;
                    if (!GenGrid.InBounds(wall, map) || !IsFullEdifice(wall)) continue;
                    if (map.edificeGrid[wall] is Door) continue;
                    if (!walls.Add(wall)) continue;
                    group.wallBeyondCells.Add(wall + dir);
                }
            }
            int n = Math.Max(1, room.cells.Count);
            group.thickRoofShare = (float)thick / n;
            group.thinRoofShare = (float)thin / n;
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

            if (Scribe.mode == LoadSaveMode.Saving && pendingSeedX != null)
            {
                // Loaded, and saved again before a tick has rebuilt the rooms: the loaded temperatures are still
                // waiting in the pending seeds, and the groups list is empty, so write the seeds back as they came.
                seedX = pendingSeedX;
                seedZ = pendingSeedZ;
                seedTemp = pendingSeedTemp;
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
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
