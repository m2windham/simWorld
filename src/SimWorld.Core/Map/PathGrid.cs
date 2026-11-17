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

        /// <summary>What a free-standing fire on a cell adds to that cell's <i>perceived</i> cost (RimWorld:
        /// <c>PathGrid.CalculatedCostAt</c> with <c>perceivedStatic</c>, which adds 1000 for a fire on the cell
        /// and 150 for each fire in the eight around it). Recalled from the decompile, not sourced; the tests
        /// pin "a path goes round a fire and a wanderer does not stop in one", not the literals.</summary>
        public const int PerceivedCostFireOnCell = 1000;

        /// <summary>See <see cref="PerceivedCostFireOnCell"/>: per burning neighbour.</summary>
        public const int PerceivedCostFireAdjacent = 150;

        /// <summary>Terrain cost plus every spawned non-pawn thing's <c>pathCost</c> at this cell; impassable
        /// overrides all of it. This is what walking the cell actually costs (<c>Pawn_PathFollower</c>).</summary>
        public int CalculatedCostAt(IntVec3 c) => CalculatedCostAt(c, perceivedStatic: false);

        /// <summary>
        /// <see cref="CalculatedCostAt(IntVec3)"/>, plus, when <paramref name="perceivedStatic"/>, what a pawn
        /// planning a route or choosing where to stand makes of fire on and beside the cell (RimWorld's own
        /// split: the cached grid the pathfinder and the wander-cell check read is the perceived one, while
        /// the follower pays only the real cost — so a pawn routes round a fire but is not slowed to a crawl
        /// inside one it is already standing in).
        /// </summary>
        public int CalculatedCostAt(IntVec3 c, bool perceivedStatic)
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

            // Snow underfoot (RimWorld: CalculatedCostAt adds SnowUtility.MovementTicksAddOn of the cell's
            // snow category). Real cost, not only perceived: deep snow slows the walker, not just the plan.
            cost += Weather.SnowUtility.MovementTicksAddOn(map.snowGrid.GetCategory(c));

            if (perceivedStatic)
            {
                if (FireUtility.StaticFireIn(c, map) != null) cost += PerceivedCostFireOnCell;
                for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
                {
                    if (FireUtility.StaticFireIn(c + GenAdj.AdjacentCells[i], map) != null) cost += PerceivedCostFireAdjacent;
                }
            }
            return cost;
        }

        public int PerceivedPathCostAt(IntVec3 c) => perceivedPathCost[map.cellIndices.CellToIndex(c)];

        public void RecalculatePerceivedPathCostAt(IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return;
            int i = map.cellIndices.CellToIndex(c);
            bool wasWalkable = perceivedPathCost[i] < ImpassableCost;
            perceivedPathCost[i] = CalculatedCostAt(c, perceivedStatic: true);
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
                perceivedPathCost[map.cellIndices.CellToIndex(c)] = CalculatedCostAt(c, perceivedStatic: true);
            }
            Version++;
        }

        public bool Walkable(IntVec3 c) => GenGrid.InBounds(c, map) && PerceivedPathCostAt(c) < ImpassableCost;

        public bool WalkableFast(int index) => perceivedPathCost[index] < ImpassableCost;
    }
}
