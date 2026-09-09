using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Owns every <see cref="Zone"/> on a map and enforces one zone per cell (RimWorld: <c>Verse.ZoneManager</c>).
    /// Constructed once per <see cref="Map.Map"/> (see <c>Map.InitializeAIManagers</c>) and re-seeded from a
    /// save the same way <see cref="RoomTracker"/>'s own extra state is: the manager itself is fresh
    /// infrastructure, only its <see cref="ExposeData"/> call carries anything that must persist.
    /// </summary>
    public sealed class ZoneManager
    {
        private readonly Map.Map map;
        private readonly List<Zone> allZones = new List<Zone>();
        private readonly Zone?[] zoneGrid;

        public ZoneManager(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            zoneGrid = new Zone?[map.cellIndices.NumGridCells];
        }

        public IReadOnlyList<Zone> AllZones => allZones;

        public Zone? ZoneAt(IntVec3 c) =>
            GenGrid.InBounds(c, map) ? zoneGrid[map.cellIndices.CellToIndex(c)] : null;

        /// <summary>Adds a freshly-created, cell-less zone to this map.</summary>
        public void RegisterZone(Zone zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            zone.Map = map;
            allZones.Add(zone);
        }

        /// <summary>Removes a zone and every cell it held.</summary>
        public void DeregisterZone(Zone zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            // Copy first: RemoveCell mutates zone.cells, which Cells exposes directly.
            var cells = new List<IntVec3>(zone.Cells);
            for (int i = 0; i < cells.Count; i++) RemoveCell(zone, cells[i]);
            allZones.Remove(zone);
            zone.Map = null;
        }

        /// <summary>
        /// Claims <paramref name="cell"/> for <paramref name="zone"/>. Fails (returns false, nothing changed)
        /// if another zone already holds the cell — RimWorld's own "one zone per cell" invariant; a caller
        /// wanting to move a cell between zones removes it from the old one first.
        /// </summary>
        public bool AddCell(Zone zone, IntVec3 cell)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (!GenGrid.InBounds(cell, map)) return false;
            int i = map.cellIndices.CellToIndex(cell);
            Zone? existing = zoneGrid[i];
            if (existing != null) return ReferenceEquals(existing, zone);

            zoneGrid[i] = zone;
            zone.AddCellRaw(cell);
            return true;
        }

        public void RemoveCell(Zone zone, IntVec3 cell)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (!GenGrid.InBounds(cell, map)) return;
            int i = map.cellIndices.CellToIndex(cell);
            if (ReferenceEquals(zoneGrid[i], zone)) zoneGrid[i] = null;
            zone.RemoveCellRaw(cell);
        }

        /// <summary>
        /// Saves/loads every zone, deep, with its own cells; called directly from <c>Map.ExposeData</c> on
        /// every Scribe phase (matching <see cref="RoomTracker.ExposeTemperatures"/>'s own shape) rather than
        /// via <c>Scribe_Deep.Look</c> on the manager itself — this manager always exists already (built by
        /// <c>Map.InitializeAIManagers</c>, which needs a live <see cref="Map.Map"/> to size <see cref="zoneGrid"/>
        /// and so cannot run through Scribe's parameterless-constructor reflection).
        /// </summary>
        public void ExposeData()
        {
            List<Zone>? zones = Scribe.mode == LoadSaveMode.Saving ? new List<Zone>(allZones) : null;
            Scribe_Collections.Look(ref zones, "zones", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                allZones.Clear();
                if (zones != null) allZones.AddRange(zones);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Array.Clear(zoneGrid, 0, zoneGrid.Length);
                for (int i = 0; i < allZones.Count; i++)
                {
                    Zone zone = allZones[i];
                    zone.Map = map;
                    for (int c = 0; c < zone.Cells.Count; c++)
                    {
                        IntVec3 cell = zone.Cells[c];
                        if (GenGrid.InBounds(cell, map)) zoneGrid[map.cellIndices.CellToIndex(cell)] = zone;
                    }
                }
            }
        }
    }
}
