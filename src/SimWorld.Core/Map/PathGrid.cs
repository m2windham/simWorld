using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>
    /// Cached per-cell path cost (RimWorld: <c>Verse.PathGrid</c>). <see cref="CalculatedCostAt"/> derives the
    /// true cost from terrain and things each time; <see cref="perceivedPathCost"/> is that value cached and
    /// kept up to date by <see cref="RecalculatePerceivedPathCostAt"/> so the pathfinder (later systems) can
    /// read a flat array instead of recomputing per lookup.
    /// </summary>
    public sealed class PathGrid
    {
        /// <summary>Cost used for any cell a pawn cannot enter at all.</summary>
        public const int ImpassableCost = 10000;

        private readonly Map map;
        private readonly int[] perceivedPathCost;

        /// <summary>Bumped every time any cell's cost changes. Nothing currently reads it to decide whether
        /// to recompute (<see cref="RecalculatePerceivedPathCostAt"/> below does that itself, cell by cell,
        /// via <see cref="Map.regionAndRoomUpdater"/>) — kept as a cheap, map-wide "did anything at all
        /// change" signal since a test already asserts on it directly.</summary>
        public int Version { get; private set; }

        public PathGrid(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            perceivedPathCost = new int[map.cellIndices.NumGridCells];
            RecalculateAllPerceivedPathCosts();
        }

        /// <summary>Terrain cost plus every spawned non-pawn thing's <c>pathCost</c> at this cell; impassable overrides all of it.</summary>
        public int CalculatedCostAt(IntVec3 c)
        {
            TerrainDef terrain = map.terrainGrid.TerrainAt(c);
            if (terrain.passability == Traversability.Impassable) return ImpassableCost;

            int cost = terrain.pathCost;
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def.category == ThingCategory.Pawn) continue;
                if (thing.def.passability == Traversability.Impassable) return ImpassableCost;
                cost += thing.def.pathCost;
            }
            return cost;
        }

        public int PerceivedPathCostAt(IntVec3 c) => perceivedPathCost[map.cellIndices.CellToIndex(c)];

        public void RecalculatePerceivedPathCostAt(IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return;
            int i = map.cellIndices.CellToIndex(c);
            bool wasWalkable = perceivedPathCost[i] < ImpassableCost;
            perceivedPathCost[i] = CalculatedCostAt(c);
            Version++;

            // Only a walkability *flip* can change the region graph's shape — a cost change between two
            // still-walkable values (heavier brush, say) never moves a region boundary, so dirtying the
            // graph for it would rebuild regions for nothing every time terrain cost is merely re-tuned.
            bool isWalkable = perceivedPathCost[i] < ImpassableCost;
            if (wasWalkable != isWalkable)
            {
                map.regionAndRoomUpdater.Notify_DirtyCell(c);
            }
        }

        public void RecalculateAllPerceivedPathCosts()
        {
            foreach (IntVec3 c in map.AllCells)
            {
                perceivedPathCost[map.cellIndices.CellToIndex(c)] = CalculatedCostAt(c);
            }
            Version++;
        }

        public bool Walkable(IntVec3 c) => GenGrid.InBounds(c, map) && PerceivedPathCostAt(c) < ImpassableCost;

        public bool WalkableFast(int index) => perceivedPathCost[index] < ImpassableCost;
    }
}
