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
    /// the start, <see cref="AreaManager.Home"/> (RimWorld: <c>Verse.Area_Home</c>). The player paints it
    /// (<see cref="Map.View.MapCommands.SetHomeArea"/>). Two things read it:
    /// <see cref="Filth.CleaningBounds"/> for where cleaning goes, and
    /// <see cref="SettlementConstructionInitiative"/> for where the settlement builds. Hauling and wandering
    /// do not read it yet.
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

        /// <summary>Every cell currently set, in cell-index order (RimWorld: <c>Area.ActiveCells</c>). Walked
        /// by a cell-scanning <see cref="Work.WorkGiver_Scanner.PotentialWorkCellsGlobal"/> — see
        /// <see cref="WorkGiver_BuildRoof"/>/<see cref="WorkGiver_RemoveRoof"/>.</summary>
        public IEnumerable<IntVec3> ActiveCells
        {
            get
            {
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i]) yield return map.cellIndices.IndexToCell(i);
                }
            }
        }

        /// <summary>Saved as the list of true cell indices — an area is typically sparse relative to the
        /// whole map, so this beats <c>Map.EncodeRunLength</c>'s dense per-cell token stream for the common
        /// case. <paramref name="keyPrefix"/> keys the saved node: <see cref="AreaManager"/> calls this for
        /// several Areas in the one flat scope <c>Map.ExposeData</c> opens (no per-Area sub-node of its own),
        /// so a shared literal label here would have every Area after the first read back the first one's
        /// cells — three sibling elements the same name, and <see cref="Scribe_Collections"/> reads by name,
        /// not by call order.</summary>
        public void ExposeData(string keyPrefix)
        {
            List<int>? trueCells = Scribe.mode == LoadSaveMode.Saving ? TrueIndices() : null;
            Scribe_Collections.Look(ref trueCells, keyPrefix + "TrueCells", LookMode.Value);

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

        /// <summary>Where the player has told the settlement to raise a roof, over and above whatever
        /// <see cref="AutoBuildRoofAreaSetter"/> already added on its own (RimWorld: <c>Verse.AreaManager.BuildRoof</c>).
        /// <see cref="WorkGiver_BuildRoof"/> is the one thing that reads it.</summary>
        public Area BuildRoof { get; }

        /// <summary>Where the player has forbidden a roof, overriding both auto-roofing and an existing roof
        /// alike (RimWorld: <c>Verse.AreaManager.NoRoof</c>). <see cref="AutoBuildRoofAreaSetter"/> never adds
        /// a cell here; <see cref="WorkGiver_RemoveRoof"/> is what acts on it.</summary>
        public Area NoRoof { get; }

        public AreaManager(Map.Map map)
        {
            Home = new Area(map, "Home area");
            BuildRoof = new Area(map, "Build roof area");
            NoRoof = new Area(map, "No roof area");
        }

        public void ExposeData()
        {
            Home.ExposeData("home");
            BuildRoof.ExposeData("buildRoof");
            NoRoof.ExposeData("noRoof");
        }
    }
}
