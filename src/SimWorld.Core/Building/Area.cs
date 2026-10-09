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

        /// <summary>How many cells are set, kept current by the indexer and <see cref="ExposeData"/> so that
        /// <see cref="TrueCount"/> is a field read (RimWorld: <c>BoolGrid.trueCountInt</c>, which
        /// <c>Area.TrueCount</c> forwards to). It was a walk of the whole grid; a think-tree scan asked it once
        /// per piece of filth through <c>CleaningBounds.IsCleanable</c>, so a
        /// 40,000-cell loop sat inside a per-candidate test.</summary>
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
                int index = map.cellIndices.CellToIndex(c);
                // RimWorld: BoolGrid.Set returns early when the cell already holds the value, and only a real
                // change moves the count.
                if (grid[index] == value) return;
                grid[index] = value;
                trueCount += value ? 1 : -1;
            }
        }

        /// <summary>How many cells are set. O(1) (RimWorld: <c>Area.TrueCount</c> =&gt; <c>BoolGrid.TrueCount</c>).</summary>
        public int TrueCount => trueCount;

        /// <summary>Every cell currently set, in cell-index order (RimWorld: <c>Area.ActiveCells</c> =&gt;
        /// <c>BoolGrid.ActiveCells</c>). Walked by a cell-scanning
        /// <see cref="Work.WorkGiver_Scanner.PotentialWorkCellsGlobal"/> — see
        /// <see cref="WorkGiver_BuildRoof"/>/<see cref="WorkGiver_RemoveRoof"/>. As RimWorld's does, it yields
        /// nothing without looking at the grid when no cell is set, and stops walking once it has yielded as
        /// many cells as are set — an empty roof area costs nothing instead of a 40,000-cell walk per scan.</summary>
        public IEnumerable<IntVec3> ActiveCells
        {
            get
            {
                if (trueCount == 0) yield break;
                int yielded = 0;
                for (int i = 0; i < grid.Length; i++)
                {
                    if (!grid[i]) continue;
                    yield return map.cellIndices.IndexToCell(i);
                    yielded++;
                    if (yielded >= trueCount) break;
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
                trueCount = 0;
                if (trueCells != null)
                {
                    for (int i = 0; i < trueCells.Count; i++)
                    {
                        int idx = trueCells[i];
                        // Counted as it is set, so a repeated or out-of-range index in a hand-edited save
                        // cannot leave TrueCount disagreeing with the grid.
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
