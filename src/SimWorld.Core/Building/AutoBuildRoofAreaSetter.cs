using System;
using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Grows <see cref="AreaManager.BuildRoof"/> by itself around a room the settlement has enclosed (RimWorld:
    /// <c>Verse.AutoBuildRoofAreaSetter</c>). Read from
    /// <c>josh-m/rw-decompile/Verse/AutoBuildRoofAreaSetter.cs</c>. Before this class the settlement could wall
    /// a room in and nothing would ever roof it: <see cref="Room.TouchesOutside"/> stayed true for every
    /// enclosed space forever, because nothing built a roof and nothing ever asked to.
    ///
    /// <para/><b>Hooked where RimWorld hooks it, adapted to this port's own room pipeline.</b> RimWorld calls
    /// <c>Room.Notify_RoomShapeOrContainedBedsChanged</c> — which queues <c>TryGenerateAreaFor(room)</c> — from
    /// <c>RegionAndRoomUpdater.CreateOrUpdateRooms</c>, once per room whose regions it just rebuilt or created;
    /// a separate queue lets several such notifications in one frame resolve once, on the next map tick
    /// (<c>AutoBuildRoofAreaSetterTick_First</c>). This port's <see cref="RoomTracker"/> has no incremental
    /// per-room update at all — see that class's own remarks — it re-floods every <see cref="Room"/> on the
    /// map from scratch whenever anything dirties it, so there is no stream of partial notifications to batch
    /// in the first place: by the time <see cref="RoomTracker.RegenerateAllRooms"/> returns, every Room already
    /// reflects the map's current, settled state. <see cref="Notify_RoomsRebuilt"/> is called once, synchronously,
    /// right there, in place of RimWorld's queue-then-resolve-next-tick.
    ///
    /// <para/><b>Two of RimWorld's own gates are not ported, and each is recorded.</b>
    /// <list type="bullet">
    /// <item><b><c>Room.RegionCount &gt; 26</c></b> is dropped; <see cref="MaxCellCount"/> (RimWorld's other
    /// bound on the same line, 320, kept exactly) is doing the real work of both. See that constant's own doc.</item>
    /// <item><b><c>Room.RegionType == RegionType.Portal</c></b> — RimWorld's doorway-only "room" — never
    /// applies here: this port's <see cref="RoomTracker"/> floods only cells with no Fillage-Full edifice on
    /// them at all, so a <see cref="Door"/>'s own cell is never a member of any <see cref="Room"/> in the first
    /// place (it is only ever a <see cref="Room.boundaryEdifices"/> entry) — there is no portal-only Room here
    /// to skip.</item>
    /// </list>
    ///
    /// <para/><b>"The player's own faction", translated — the same call <see cref="AutoHomeAreaMaker"/> makes,
    /// answered the same way for the same reason, and split across two checks because one substitute alone
    /// leaves a gap the other closes.</b> RimWorld's own gate walks a room's border and (1) aborts the whole
    /// room if any border edifice belongs to a hostile faction, then (2) requires at least one border edifice
    /// to belong to <c>Faction.OfPlayer</c> before roofing anything. This port's <see cref="Thing"/> carries no
    /// <c>Faction</c> field at all (see <see cref="AutoHomeAreaMaker"/>'s own doc for the established reading:
    /// nothing here ever lets a hostile pawn place a building, so "abort for a hostile border" never arises),
    /// so the real question is only ever (2): translated as two independent checks, both required, because
    /// each answers a different half of "did the <i>settlement</i> build this, and does it happen anywhere the
    /// settlement calls its own":
    /// <list type="bullet">
    /// <item><b>Not bare rock.</b> <see cref="ThingDef.mineable"/> — <c>Buildings_CollapsedRocks.xml</c>'s own
    /// comment already names it "this codebase's line between natural rock and something somebody built", and
    /// <see cref="AI.WorkGiver_Miner"/> already reads it for exactly that distinction. A room touched anywhere
    /// by a border edifice that is <i>not</i> mineable — a wall or a door, the only other things
    /// <see cref="RoofUtility.GetRoofHolderOrImpassable"/> ever returns in shipped content — clears this half;
    /// a room bordered solely by bare rock does not, so a wholly natural cave (every border edifice mineable)
    /// is correctly left alone — map-gen's own <see cref="MapGen.GenStep_Roofs"/> already roofed whatever of it
    /// was enclosed by rock density.</item>
    /// <item><b>Not an unclaimed ruin, and not some far corner of the map the settlement does not call
    /// home.</b> An ancient wall <c>MapGen.GenStep_Ruins.SpawnWeatheredWall</c> scatters is an ordinary, non-
    /// mineable <c>Wall</c> — it clears the check above on its own, which RimWorld's Faction check alone would
    /// not (an unclaimed ruin's Faction is <c>null</c>, same as bare rock, so RimWorld leaves it alone too). So
    /// this also requires at least one of the room's own cells to already sit in <see cref="AreaManager.Home"/>
    /// — the same ground every other autonomous settlement mechanic in this codebase is already scoped to
    /// (<see cref="SettlementConstructionInitiative"/>, <see cref="Filth.CleaningBounds"/>,
    /// <see cref="AI.WorkGiver_FightFires"/>), and, because <see cref="AutoHomeAreaMaker.Notify_BuildingSpawned"/>
    /// marks home area from the exact same event (<see cref="Frame.CompleteConstruction"/>) this checks reads
    /// "not mineable" against, a room the settlement itself walled in will already have this covered for free:
    /// its own walls mark the ground around them the moment they finish. A room enclosed only by an ancient
    /// ruin's walls, or one the player placed somewhere the settlement has never otherwise built near, has
    /// neither — <b>this half is this port's own addition, recorded rather than silently added</b>, since
    /// RimWorld has no home-area concept for this class to begin with.</item>
    /// </list>
    /// </summary>
    public static class AutoBuildRoofAreaSetter
    {
        /// <summary>Cells a room may hold before it stops being a candidate for auto-roofing (RimWorld:
        /// the <c>320</c> half of <c>Room.RegionCount &gt; 26 || Room.CellCount &gt; 320</c>, sourced from
        /// decompile). See the class doc for why the <c>RegionCount</c> half is not ported.</summary>
        public const int MaxCellCount = 320;

        /// <summary>
        /// A full room rebuild just finished (RimWorld's combined <c>TryGenerateAreaFor</c> +
        /// <c>ResolveQueuedGenerateRoofs</c> + <c>TryGenerateAreaNow</c>, run synchronously for every room
        /// rather than queued — see the class doc). Adds every eligible, currently-unroofed cell of each
        /// eligible room to <see cref="AreaManager.BuildRoof"/>; never removes a cell from it and never touches
        /// <see cref="AreaManager.NoRoof"/>.
        /// </summary>
        public static void Notify_RoomsRebuilt(IReadOnlyList<Room> rooms, Map.Map map)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (map == null) throw new ArgumentNullException(nameof(map));

            for (int i = 0; i < rooms.Count; i++) TryGenerateAreaNow(rooms[i], map);
        }

        /// <summary>RimWorld: <c>AutoBuildRoofAreaSetter.TryGenerateAreaNow</c>.</summary>
        private static void TryGenerateAreaNow(Room room, Map.Map map)
        {
            if (room.Cells.Count == 0 || room.TouchesMapEdge) return;
            if (room.Cells.Count > MaxCellCount) return;

            IReadOnlyList<IntVec3> roomCells = room.Cells;
            bool anyCellInHomeArea = false;
            for (int i = 0; i < roomCells.Count; i++)
            {
                if (!map.areaManager.Home[roomCells[i]]) continue;
                anyCellInHomeArea = true;
                break;
            }
            if (!anyCellInHomeArea) return; // this port's own addition — see the class doc's second bullet.

            bool touchedBySettlementConstruction = false;
            foreach (IntVec3 border in room.BorderCells)
            {
                if (!GenGrid.InBounds(border, map)) continue;
                Thing? holder = RoofUtility.GetRoofHolderOrImpassable(border, map);
                if (holder == null) continue;
                if (holder.def.mineable) continue; // bare rock: see the class doc's "translated" section.
                if (holder.def.building != null && !holder.def.building.allowAutoroof) continue;
                touchedBySettlementConstruction = true;
            }
            if (!touchedBySettlementConstruction) return;

            // Every cell the room's own floor reaches, plus the whole footprint of anything bigger than 1x1
            // bordering it (RimWorld: innerCells) — a room can be enclosed by one corner of a wide building.
            var innerCells = new HashSet<IntVec3>();
            for (int i = 0; i < roomCells.Count; i++)
            {
                IntVec3 c = roomCells[i];
                innerCells.Add(c);
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 n = c + GenAdj.AdjacentCells[d];
                    if (!GenGrid.InBounds(n, map)) continue;
                    Thing? holder = RoofUtility.GetRoofHolderOrImpassable(n, map);
                    if (holder == null) continue;
                    if (holder.def.size.x <= 1 && holder.def.size.z <= 1) continue;
                    CellRect rect = holder.OccupiedRect().ClipInsideMap(map);
                    foreach (IntVec3 cell in rect.Cells) innerCells.Add(cell);
                }
            }

            // Every inner cell, plus (for each) any of its 8 neighbours that is itself a roof holder or
            // impassable — the one ring outward that carries the roof onto the walls holding it up (RimWorld:
            // cellsToRoof, built from GenAdj.AdjacentCellsAndInside).
            var cellsToRoof = new HashSet<IntVec3>();
            foreach (IntVec3 c in innerCells)
            {
                cellsToRoof.Add(c);
                for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 n = c + GenAdj.AdjacentCells[d];
                    if (!GenGrid.InBounds(n, map)) continue;
                    if (RoofUtility.GetRoofHolderOrImpassable(n, map) == null) continue;
                    cellsToRoof.Add(n);
                }
            }

            foreach (IntVec3 c in cellsToRoof)
            {
                if (map.roofGrid.RoofAt(c) != null) continue;
                if (map.areaManager.NoRoof[c]) continue;
                // assumeNonNoRoofCellsAreRoofed: true — none of this batch is roofed yet; see that parameter's
                // own doc on RoofCollapseUtility.WithinRangeOfRoofHolder for why this is the eligibility
                // question ("would a roof reach a support"), not the real one WorkGiver_BuildRoof/
                // JobDriver_BuildRoof ask once building is actually under way.
                if (!RoofCollapseUtility.WithinRangeOfRoofHolder(c, map, ignoring: null, assumeNonNoRoofCellsAreRoofed: true))
                {
                    continue;
                }
                map.areaManager.BuildRoof[c] = true;
            }
        }
    }
}
