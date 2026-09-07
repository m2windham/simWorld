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

        /// <summary>
        /// Moves a single-cell Thing (every pawn, every item) from one cell to another without walking
        /// <see cref="CellRect.Cells"/>'s iterator — <see cref="Thing.Position"/>'s setter uses this for its
        /// fast path. A pawn's own move is the only thing in this codebase that calls the Position setter
        /// on an already-spawned Thing repeatedly (system 9's <c>Pawn_PathFollower</c>, roughly every dozen
        /// ticks per pawn); the general <see cref="Deregister"/>/<see cref="Register"/> pair remains for
        /// anything with a multi-cell footprint, which nothing moves today.
        /// </summary>
        public void MoveSingleCell(Thing thing, IntVec3 oldCell, IntVec3 newCell)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (GenGrid.InBounds(oldCell, map)) grid[map.cellIndices.CellToIndex(oldCell)].Remove(thing);
            if (GenGrid.InBounds(newCell, map)) grid[map.cellIndices.CellToIndex(newCell)].Add(thing);
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
