using System.Collections.Generic;

namespace SimWorld.Map.View
{
    /// <summary>What a building or room is for, as a role the inspector can name.</summary>
    public enum BuildingRole
    {
        /// <summary>The settlement's walled communal hall: the ring of wall round the shared bed block every
        /// citizen without a house sleeps in (<c>Building.SettlementCompound</c>).</summary>
        Hall,

        /// <summary>A household's house (<see cref="DwellingView"/>, by <see cref="BuildingView.DwellingId"/>).</summary>
        House,

        /// <summary>Another enclosed, roofed room holding more than one bed.</summary>
        Dormitory,

        /// <summary>Another enclosed, roofed room holding one bed.</summary>
        Bedroom,

        /// <summary>Another enclosed, roofed room holding a workbench and no bed.</summary>
        Workshop,

        /// <summary>Another enclosed, roofed room holding a store and no bed or bench.</summary>
        Storeroom,

        /// <summary>Another enclosed, roofed room with none of those in it.</summary>
        Room,

        /// <summary>A store standing on its own: the storage hut and every later form of it, the granary
        /// included.</summary>
        Store,

        /// <summary>A workbench standing on its own: anything that takes bills.</summary>
        Bench,

        /// <summary>The research bench.</summary>
        Research,
    }

    /// <summary>
    /// The settlement's buildings and rooms with what each is for (host UX request after playtest 4: clicking the
    /// communal hall's roof had nothing to resolve to, since a hall is a ring of ordinary walls and nothing in a
    /// chunk says which walls make it or what it is). <see cref="BuildingAt"/> resolves a clicked cell, roofed or
    /// not, to the most specific building that holds it.
    ///
    /// <para/>A slow-clock capture like <see cref="SettlementLevers"/>: read it when the inspector opens or the
    /// player clicks, not every frame.
    /// </summary>
    public sealed class SettlementBuildings
    {
        internal SettlementBuildings(bool hasMap, string? absenceReason, int ticksGame, IReadOnlyList<BuildingView> buildings)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            TicksGame = ticksGame;
            Buildings = buildings;
        }

        /// <summary>False when no interior is open; <see cref="AbsenceReason"/> says why.</summary>
        public bool HasMap { get; }

        public string? AbsenceReason { get; }

        public int TicksGame { get; }

        /// <summary>The hall first, then the houses in the order planned, then other rooms, then stores and benches
        /// standing on their own in the map's own order.</summary>
        public IReadOnlyList<BuildingView> Buildings { get; }

        /// <summary>The smallest building or room holding <paramref name="cell"/>, or null: a bench inside a
        /// workshop resolves to the bench, a bed in the hall to the hall, a cell of the hall's roof to the hall.</summary>
        public BuildingView? BuildingAt(IntVec3 cell)
        {
            BuildingView? best = null;
            for (int i = 0; i < Buildings.Count; i++)
            {
                BuildingView b = Buildings[i];
                if (!b.Contains(cell)) continue;
                if (best == null || b.BoundsSize.x * b.BoundsSize.z < best.BoundsSize.x * best.BoundsSize.z) best = b;
            }
            return best;
        }
    }

    /// <summary>One building or room of the settlement: what it is for, what it is called and where it is.</summary>
    public sealed class BuildingView
    {
        internal BuildingView(
            BuildingRole role, string roleLabel, string label, string? defName, int? thingId, int? dwellingId,
            IntVec3 boundsMin, IntVec2 boundsSize, IReadOnlyList<IntVec3> cells, bool roofed, bool isComplete)
        {
            Role = role;
            RoleLabel = roleLabel;
            Label = label;
            DefName = defName;
            ThingId = thingId;
            DwellingId = dwellingId;
            BoundsMin = boundsMin;
            BoundsSize = boundsSize;
            Cells = cells;
            Roofed = roofed;
            IsComplete = isComplete;
        }

        public BuildingRole Role { get; }

        /// <summary>The role in words ("communal hall", "house", "workshop", "store").</summary>
        public string RoleLabel { get; }

        /// <summary>What this one is ("communal hall", "longhouse", "granary", "masons' bench").</summary>
        public string Label { get; }

        /// <summary>The <c>ThingDef</c> of a building standing on its own, the <c>DwellingDef</c> of a house, or
        /// null for the hall and a room.</summary>
        public string? DefName { get; }

        /// <summary>The building's <see cref="ThingView.ThingId"/>, for a store or bench standing on its own.</summary>
        public int? ThingId { get; }

        /// <summary>The house's <see cref="DwellingView.DwellingId"/>, for <see cref="BuildingRole.House"/>.</summary>
        public int? DwellingId { get; }

        /// <summary>Low corner of everything it covers: the hall's ring of wall, a house's plot, a room's floor, a
        /// building's footprint.</summary>
        public IntVec3 BoundsMin { get; }

        public IntVec2 BoundsSize { get; }

        /// <summary>The floor under the roof: the hall's bed block, a house's plot, a room's own cells, a
        /// building's footprint.</summary>
        public IReadOnlyList<IntVec3> Cells { get; }

        /// <summary>Every one of <see cref="Cells"/> has a roof over it.</summary>
        public bool Roofed { get; }

        /// <summary>The hall: every cell of its ring stands built. A house: <see cref="DwellingView.IsComplete"/>.
        /// A room or a building standing on its own: true.</summary>
        public bool IsComplete { get; }

        /// <summary>Whether <paramref name="cell"/> is inside it: within the bounds, and for a room, one of its own
        /// cells (a room need not be a rectangle).</summary>
        public bool Contains(IntVec3 cell)
        {
            if (cell.x < BoundsMin.x || cell.z < BoundsMin.z) return false;
            if (cell.x >= BoundsMin.x + BoundsSize.x || cell.z >= BoundsMin.z + BoundsSize.z) return false;
            if (!IsRoom) return true;
            for (int i = 0; i < Cells.Count; i++)
            {
                if (Cells[i] == cell) return true;
            }
            return false;
        }

        private bool IsRoom =>
            Role == BuildingRole.Dormitory || Role == BuildingRole.Bedroom || Role == BuildingRole.Workshop
            || Role == BuildingRole.Storeroom || Role == BuildingRole.Room;
    }
}
