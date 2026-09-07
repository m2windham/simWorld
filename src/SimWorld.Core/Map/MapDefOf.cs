using SimWorld.Defs;

namespace SimWorld.Map
{
    [DefOf]
    public static class TerrainDefOf
    {
        public static TerrainDef Soil = null!;
        public static TerrainDef Sand = null!;
        public static TerrainDef Gravel = null!;
        public static TerrainDef WaterShallow = null!;
        public static TerrainDef WaterDeep = null!;
    }

    [DefOf]
    public static class RoofDefOf
    {
        public static RoofDef RoofConstructed = null!;
        public static RoofDef RoofRockThin = null!;
        public static RoofDef RoofRockThick = null!;
    }

    [DefOf]
    public static class TerrainAffordanceDefOf
    {
        public static TerrainAffordanceDef Light = null!;
        public static TerrainAffordanceDef Medium = null!;
        public static TerrainAffordanceDef Heavy = null!;
        public static TerrainAffordanceDef GrowSoil = null!;
    }
}
