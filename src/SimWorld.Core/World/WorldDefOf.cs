using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>Biomes referenced directly by generation code (fallback defaults) or tests; every other biome is reached only by <see cref="BiomeWorker"/> scoring.</summary>
    [DefOf]
    public static class BiomeDefOf
    {
        public static BiomeDef Ocean = null!;
        public static BiomeDef Lake = null!;
        public static BiomeDef TemperateForest = null!;
        public static BiomeDef Desert = null!;
        public static BiomeDef Tundra = null!;
        public static BiomeDef IceSheet = null!;
    }

    [DefOf]
    public static class WorldObjectDefOf
    {
        public static WorldObjectDef Settlement = null!;
        public static WorldObjectDef Caravan = null!;
    }
}
