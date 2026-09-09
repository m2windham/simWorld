using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// A named, non-exclusive boolean-per-cell region of the map (RimWorld: <c>Verse.Area</c>). Distinct
    /// from <see cref="Zone"/> on purpose: any number of Areas can overlap the same cell and a cell can sit
    /// in both an Area and a Zone at once (a stockpile inside the home area, say) — RimWorld's own split,
    /// kept here rather than folded into Zone. This pass ships only the one Area every RimWorld map has from
    /// the start, <see cref="AreaManager.Home"/> (RimWorld: <c>Verse.Area_Home</c>); nothing yet reads it
    /// (no hauling/wandering restriction exists to consult it) — see this module's report.
    /// </summary>
    public sealed class Area
    {
        private readonly Map.Map map;
        private readonly bool[] grid;

        public string label;

        public Area(Map.Map map, string label)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.label = label;
            grid = new bool[map.cellIndices.NumGridCells];
        }

        public bool this[IntVec3 c]
        {
            get => GenGrid.InBounds(c, map) && grid[map.cellIndices.CellToIndex(c)];
            set
            {
                if (GenGrid.InBounds(c, map)) grid[map.cellIndices.CellToIndex(c)] = value;
            }
        }

        public int TrueCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i]) n++;
                }
                return n;
            }
        }

        /// <summary>Saved as the list of true cell indices — an area is typically sparse relative to the
        /// whole map, so this beats <c>Map.EncodeRunLength</c>'s dense per-cell token stream for the common case.</summary>
        public void ExposeData()
        {
            List<int>? trueCells = Scribe.mode == LoadSaveMode.Saving ? TrueIndices() : null;
            Scribe_Collections.Look(ref trueCells, "trueCells", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Array.Clear(grid, 0, grid.Length);
                if (trueCells != null)
                {
                    for (int i = 0; i < trueCells.Count; i++)
                    {
                        int idx = trueCells[i];
                        if (idx >= 0 && idx < grid.Length) grid[idx] = true;
                    }
                }
            }
        }

        private List<int> TrueIndices()
        {
            var list = new List<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i]) list.Add(i);
            }
            return list;
        }
    }

    /// <summary>Owns every <see cref="Area"/> on a map (RimWorld: <c>Verse.AreaManager</c>), trimmed to the
    /// one Area this pass ships.</summary>
    public sealed class AreaManager
    {
        public Area Home { get; }

        public AreaManager(Map.Map map)
        {
            Home = new Area(map, "Home area");
        }

        public void ExposeData() => Home.ExposeData();
    }
}
