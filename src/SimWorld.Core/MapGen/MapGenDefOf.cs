using SimWorld.Defs;
using SimWorld.Map;

namespace SimWorld.MapGen
{
    /// <summary>The default local map generator (spec §5b): elevation/fertility through roofs, in that order.</summary>
    [DefOf]
    public static class MapGeneratorDefOf
    {
        public static MapGeneratorDef Base = null!;
    }

    /// <summary>
    /// <see cref="TerrainDef"/>s <c>MapGen</c> paints that aren't already exposed through
    /// <see cref="global::SimWorld.Map.TerrainDefOf"/> (which covers only Soil/Sand/Gravel/water).
    /// </summary>
    [DefOf]
    public static class MapGenTerrainDefOf
    {
        public static TerrainDef SoilRich = null!;
        public static TerrainDef Marsh = null!;
        public static TerrainDef MarshyTerrain = null!;
        public static TerrainDef StreetDirtPath = null!;
        public static TerrainDef StreetDirtRoad = null!;
        public static TerrainDef StreetStoneRoad = null!;
        public static TerrainDef StreetAncientAsphalt = null!;
    }

    /// <summary>ThingDefs <c>MapGen</c> places directly: natural rock, a mineable ore vein, loose chunks and wild plant growth.</summary>
    [DefOf]
    public static class MapGenThingDefOf
    {
        public static ThingDef Sandstone = null!;
        public static ThingDef Granite = null!;
        public static ThingDef Limestone = null!;
        public static ThingDef MineableSteel = null!;
        public static ThingDef ChunkSandstone = null!;
        public static ThingDef ChunkGranite = null!;
        public static ThingDef ChunkLimestone = null!;
        public static ThingDef WildPlant = null!;
    }
}
