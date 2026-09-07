using System;
using System.Collections.Generic;
using SimWorld.World;

namespace SimWorld.Caravans
{
    /// <summary>
    /// Per-tile and per-edge travel cost for caravans (RimWorld: <c>RimWorld.Planet.WorldPathGrid</c>).
    /// A tile's own difficulty is hilliness plus its biome's <see cref="BiomeDef.movementDifficulty"/>
    /// (RimWorld: <c>WorldPathGrid.CalculatedMovementDifficultyAt</c>); a road crossing the edge into it
    /// then multiplies that down. This is a separate, simpler approximation from
    /// <c>Gen.WorldGenStep_Roads</c>'s own hilliness cost table — that one only ever picks which tiles a
    /// generated road follows, not how long an actual caravan takes to cross them.
    /// </summary>
    public static class WorldPathGrid
    {
        /// <summary>RimWorld: <c>WorldPathGrid.ImpassableMovementDifficulty</c>. Effectively "never cross this".</summary>
        public const float ImpassableMovementDifficulty = 1000f;

        private static readonly Dictionary<Hilliness, float> HillinessBase = new Dictionary<Hilliness, float>
        {
            [Hilliness.Undefined] = 1f,
            [Hilliness.Flat] = 1f,
            [Hilliness.SmallHills] = 1.5f,
            [Hilliness.LargeHills] = 2f,
            [Hilliness.Mountainous] = 3f,
            [Hilliness.Impassable] = ImpassableMovementDifficulty,
        };

        /// <summary>RimWorld: <c>WorldPathGrid.CalculatedMovementDifficultyAt</c>. Water, a missing biome, or an impassable biome/hilliness all read as impassable.</summary>
        public static float CalculatedMovementDifficultyAt(Tile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (tile.WaterCovered || tile.biome == null || tile.biome.impassable) return ImpassableMovementDifficulty;
            float hillinessComponent = HillinessBase.TryGetValue(tile.hilliness, out float h) ? h : 1f;
            if (hillinessComponent >= ImpassableMovementDifficulty) return ImpassableMovementDifficulty;
            return hillinessComponent + tile.biome.movementDifficulty;
        }

        /// <summary>
        /// Cost of moving from <paramref name="fromTile"/> into <paramref name="toTile"/>: the destination
        /// tile's own difficulty, discounted by the best road (if any) linking the two tiles.
        /// </summary>
        public static float MovementCostBetween(WorldGrid grid, int fromTile, int toTile)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            float baseCost = CalculatedMovementDifficultyAt(grid.Tiles[toTile]);
            if (baseCost >= ImpassableMovementDifficulty) return ImpassableMovementDifficulty;
            RoadDef? road = BestRoadBetween(grid, fromTile, toTile);
            return road != null ? baseCost * road.movementCostMultiplier : baseCost;
        }

        private static RoadDef? BestRoadBetween(WorldGrid grid, int a, int b)
        {
            RoadDef? best = null;
            foreach (RoadLink link in grid.Tiles[a].Roads)
            {
                if (link.neighbor != b) continue;
                if (best == null || link.road.priority > best.priority) best = link.road;
            }
            return best;
        }
    }
}
