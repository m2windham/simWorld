using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>
    /// Every spawned Thing indexed by the cell(s) it occupies (RimWorld: <c>Verse.ThingGrid</c>). A multi-cell
    /// Thing is registered under every cell of its <see cref="Thing.OccupiedRect"/>.
    /// </summary>
    public sealed class ThingGrid
    {
        private readonly Map map;
        private readonly List<Thing>[] grid;

        public ThingGrid(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            int n = map.cellIndices.NumGridCells;
            grid = new List<Thing>[n];
            for (int i = 0; i < n; i++)
            {
                grid[i] = new List<Thing>();
            }
        }

        public void Register(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            foreach (IntVec3 c in thing.OccupiedRect().Cells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                grid[map.cellIndices.CellToIndex(c)].Add(thing);
            }
        }

        public void Deregister(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            foreach (IntVec3 c in thing.OccupiedRect().Cells)
            {
                if (!GenGrid.InBounds(c, map)) continue;
                grid[map.cellIndices.CellToIndex(c)].Remove(thing);
            }
        }

        public IReadOnlyList<Thing> ThingsListAt(IntVec3 c) => grid[map.cellIndices.CellToIndex(c)];

        public IEnumerable<Thing> ThingsAt(IntVec3 c) => ThingsListAt(c);

        public T? ThingAt<T>(IntVec3 c) where T : Thing
        {
            List<Thing> list = grid[map.cellIndices.CellToIndex(c)];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is T typed) return typed;
            }
            return null;
        }

        public bool CellContains(IntVec3 c, ThingCategory category)
        {
            List<Thing> list = grid[map.cellIndices.CellToIndex(c)];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].def.category == category) return true;
            }
            return false;
        }
    }
}
