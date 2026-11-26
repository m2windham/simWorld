using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The settlement's buildings and rooms with their roles (host UX request after playtest 4):
    /// <see cref="CaptureBuildings()"/> and its overloads. See <see cref="SettlementBuildings"/>.
    ///
    /// <para/><b>Where the roles come from.</b> The hall and the houses are the settlement's own plan
    /// (<see cref="SettlementCompound"/>, <see cref="Dwelling"/>), so they are named by the plan rather than
    /// guessed from their walls. Any other closed, roofed room the settlement or the player has walled in is named
    /// by what stands in it, which is RimWorld's idea (<c>RoomRoleDef</c> and its workers score a room by its
    /// contents: barracks, bedroom, workshop, storeroom; recalled, not read from a decompile), trimmed to the
    /// roles this port's buildings can fill. Stores and benches standing on their own are named by what they are.
    /// </summary>
    public sealed partial class MapViewSnapshot
    {
        /// <summary>The buildings of whichever settlement the god has open.</summary>
        public static SettlementBuildings CaptureBuildings()
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return AbsentBuildings(reason!);
            return CaptureBuildings(tile.Value);
        }

        /// <summary>The buildings of the settlement on <paramref name="tile"/>.</summary>
        public static SettlementBuildings CaptureBuildings(int tile)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out _);
            if (map == null) return AbsentBuildings(reason!);
            return CaptureBuildings(map);
        }

        /// <summary>The buildings on a map held directly, for the core's own tests and tools, as
        /// <see cref="Capture(SimWorld.Map.Map)"/> is.</summary>
        public static SettlementBuildings CaptureBuildings(SimWorld.Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            World.Settlement? settlement = AI.HuntingInitiative.SettlementFor(map);
            var views = new List<BuildingView>();
            var planned = new List<CellRect>();

            SettlementCompound? compound = map.settlementCompound;
            if (compound != null)
            {
                CellRect ring = compound.Ring;
                planned.Add(ring);
                bool complete = true;
                foreach (IntVec3 c in ring.EdgeCells)
                {
                    Thing? edifice = GenGrid.InBounds(c, map) ? map.edificeGrid[c] : null;
                    if (edifice == null || edifice.def.mineable) complete = false;
                }
                views.Add(View(map, BuildingRole.Hall, "communal hall", "communal hall", null, null, null, ring,
                    new List<IntVec3>(compound.Interior.ClipInsideMap(map).Cells), complete));
            }

            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                planned.Add(d.footprint);
                views.Add(View(map, BuildingRole.House, "house", d.def.label ?? d.def.defName, d.def.defName, null,
                    d.id, d.footprint, new List<IntVec3>(d.footprint.ClipInsideMap(map).Cells), d.IsComplete(map)));
            }

            IReadOnlyList<Room> rooms = map.roomTracker.Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                Room room = rooms[i];
                if (room.TouchesOutside || room.cells.Count == 0 || InAny(room.cells, planned)) continue;
                if (!BuiltBy(room, settlement)) continue;
                (BuildingRole role, string label) = RoleOf(map, room);
                views.Add(View(map, role, label, label, null, null, null, BoundsOf(room.cells), new List<IntVec3>(room.cells), true));
            }

            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                Thing t = buildings[i];
                if (!t.Spawned || !Owned(t, settlement)) continue;
                BuildingRole? role = StandaloneRole(t);
                if (role == null) continue;
                CellRect rect = t.OccupiedRect();
                views.Add(View(map, role.Value, RoleLabelOf(role.Value), t.def.label ?? t.def.defName, t.def.defName,
                    t.thingIDNumber, null, rect, new List<IntVec3>(rect.Cells), true));
            }

            return new SettlementBuildings(true, null, Find.TickManager.TicksGame, views);
        }

        private static BuildingView View(
            SimWorld.Map.Map map, BuildingRole role, string roleLabel, string label, string? defName, int? thingId,
            int? dwellingId, CellRect bounds, List<IntVec3> cells, bool complete)
        {
            bool roofed = cells.Count > 0;
            for (int i = 0; i < cells.Count && roofed; i++) roofed = map.roofGrid.RoofAt(cells[i]) != null;
            return new BuildingView(role, roleLabel, label, defName, thingId, dwellingId,
                new IntVec3(bounds.minX, 0, bounds.minZ), new IntVec2(bounds.width, bounds.height), cells, roofed, complete);
        }

        /// <summary>A room inside the hall's ring or a house's plot is that building's, already listed.</summary>
        private static bool InAny(List<IntVec3> cells, List<CellRect> rects)
        {
            for (int r = 0; r < rects.Count; r++)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    if (rects[r].Contains(cells[i])) return true;
                }
            }
            return false;
        }

        /// <summary>The settlement walled it in: some wall or door round it is its own, as the roof builder asks
        /// before it roofs a room (<see cref="AutoBuildRoofAreaSetter"/>). A cave in the rock, or a ruin's walls,
        /// is not the settlement's building.</summary>
        private static bool BuiltBy(Room room, World.Settlement? settlement)
        {
            foreach (Thing edifice in room.boundaryEdifices)
            {
                if (!edifice.def.mineable && Owned(edifice, settlement)) return true;
            }
            return false;
        }

        private static bool Owned(Thing t, World.Settlement? settlement) =>
            settlement != null ? settlement.IsOwnStructure(t) : t.Faction != null;

        private static (BuildingRole Role, string Label) RoleOf(SimWorld.Map.Map map, Room room)
        {
            // What stands in it and against it, as RimWorld's Room.ContainedAndAdjacentThings reads it: a bench
            // or a store that fills its cell is not one of the room's cells, only next to them.
            int beds = 0;
            bool bench = false, store = false;
            var seen = new HashSet<Thing>();
            for (int i = 0; i < room.cells.Count; i++)
            {
                for (int d = -1; d < GenAdj.AdjacentCells.Length; d++)
                {
                    IntVec3 c = d < 0 ? room.cells[i] : room.cells[i] + GenAdj.AdjacentCells[d];
                    if (!GenGrid.InBounds(c, map)) continue;
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                    for (int j = 0; j < here.Count; j++)
                    {
                        Thing t = here[j];
                        if (!seen.Add(t)) continue;
                        if (ReferenceEquals(t.def, ConstructionThingDefOf.Bed)) beds++;
                        else if (StorageForms.IsStorage(t.def)) store = true;
                        else if (StandaloneRole(t) is BuildingRole.Bench or BuildingRole.Research) bench = true;
                    }
                }
            }
            if (beds > 1) return (BuildingRole.Dormitory, "dormitory");
            if (beds == 1) return (BuildingRole.Bedroom, "bedroom");
            if (bench) return (BuildingRole.Workshop, "workshop");
            if (store) return (BuildingRole.Storeroom, "storeroom");
            return (BuildingRole.Room, "room");
        }

        /// <summary>The role of a building that has one standing on its own, or null for a wall, a door, a bed and
        /// everything else that is part of something rather than a place.</summary>
        private static BuildingRole? StandaloneRole(Thing t)
        {
            if (t.def.category != ThingCategory.Building) return null;
            if (StorageForms.IsStorage(t.def)) return BuildingRole.Store;
            if (ReferenceEquals(t.def, ResearchWorkDefOf.ResearchBench)) return BuildingRole.Research;
            // Bills, or a recipe that names it as its bench without one (the butcher table: corpses find it by
            // recipe, AI.WorkGiver_ButcherCorpse).
            if (t.def.AllRecipes.Count > 0 || (t is ThingWithComps twc && twc.GetComp<CompBillGiver>() != null)) return BuildingRole.Bench;
            return null;
        }

        private static string RoleLabelOf(BuildingRole role) => role switch
        {
            BuildingRole.Store => "store",
            BuildingRole.Research => "research bench",
            _ => "workbench",
        };

        private static CellRect BoundsOf(List<IntVec3> cells)
        {
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                minX = Math.Min(minX, cells[i].x);
                minZ = Math.Min(minZ, cells[i].z);
                maxX = Math.Max(maxX, cells[i].x);
                maxZ = Math.Max(maxZ, cells[i].z);
            }
            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }

        private static SettlementBuildings AbsentBuildings(string reason) =>
            new SettlementBuildings(false, reason, Find.TickManager.TicksGame, Array.Empty<BuildingView>());
    }
}
