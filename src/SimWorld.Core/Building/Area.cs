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

        /// <summary>Kept as cells are set, as RimWorld's <c>Area.trueCountInt</c> is: cleaning asks for it once per
        /// filth it considers, and counting the grid each time was most of an idle band's job search.</summary>
        private int trueCount;

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
                if (!GenGrid.InBounds(c, map)) return;
                int i = map.cellIndices.CellToIndex(c);
                if (grid[i] == value) return;
                grid[i] = value;
                trueCount += value ? 1 : -1;
            }
        }

        public int TrueCount => trueCount;

        /// <summary>Every cell in this area, in index order (RimWorld: <c>Area.ActiveCells</c>).</summary>
        public IEnumerable<IntVec3> ActiveCells
        {
            get
            {
                if (trueCount == 0) yield break;
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i]) yield return map.cellIndices.IndexToCell(i);
                }
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
                trueCount = 0;
                if (trueCells != null)
                {
                    for (int i = 0; i < trueCells.Count; i++)
                    {
                        int idx = trueCells[i];
                        if (idx >= 0 && idx < grid.Length && !grid[idx])
                        {
                            grid[idx] = true;
                            trueCount++;
                        }
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
    /// two Areas this port ships.</summary>
    public sealed class AreaManager
    {
        public Area Home { get; }

        /// <summary>Where snow is shovelled (RimWorld: <c>Verse.Area_SnowClear</c>, <c>AreaManager.SnowClear</c>):
        /// painted by the player (<see cref="Map.View.MapCommands.SetSnowClearArea"/>), read by
        /// <see cref="Weather.WorkGiver_ClearSnow"/>.</summary>
        public Area SnowClear { get; }

        public AreaManager(Map.Map map)
        {
            Home = new Area(map, "Home area");
            SnowClear = new Area(map, "Snow clear");
        }

        /// <summary>The home area keeps writing at the map's own level, where saves made before the snow-clear
        /// area existed have it; the snow-clear area gets its own node so the two "trueCells" never collide.
        /// A save without that node loads with an empty snow-clear area.</summary>
        public void ExposeData()
        {
            Home.ExposeData();
            if (Scribe.EnterNode("snowClearArea"))
            {
                try
                {
                    SnowClear.ExposeData();
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
        }
    }
}
