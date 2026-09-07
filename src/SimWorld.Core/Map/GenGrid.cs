using System.Collections.Generic;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>Static per-cell queries against a map's grids (RimWorld: <c>Verse.GenGrid</c>).</summary>
    public static class GenGrid
    {
        public static bool InBounds(IntVec3 c, Map map)
        {
            if (map == null) return false;
            return c.x >= 0 && c.x < map.Size.x && c.z >= 0 && c.z < map.Size.z;
        }

        public static bool Walkable(IntVec3 c, Map map) => map.pathGrid.Walkable(c);

        /// <summary>Walkable and nothing occupying the cell forbids stopping there (a pawn can end a path here).</summary>
        public static bool Standable(IntVec3 c, Map map)
        {
            if (!Walkable(c, map)) return false;
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.passability != Traversability.Standable) return false;
            }
            return true;
        }

        public static bool Impassable(IntVec3 c, Map map) => !Walkable(c, map);

        public static Thing? GetEdifice(IntVec3 c, Map map) => map.edificeGrid[c];

        public static TerrainDef GetTerrain(IntVec3 c, Map map) => map.terrainGrid.TerrainAt(c);

        public static RoofDef? GetRoof(IntVec3 c, Map map) => map.roofGrid.RoofAt(c);

        public static bool Roofed(IntVec3 c, Map map) => map.roofGrid.Roofed(c);

        public static IReadOnlyList<Thing> GetThingList(IntVec3 c, Map map) => map.thingGrid.ThingsListAt(c);
    }
}
