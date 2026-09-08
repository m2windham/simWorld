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

    /// <summary>Every deposit category <c>Gen.WorldGenStep_Deposits</c> derives from terrain (spec §5b.2).</summary>
    [DefOf]
    public static class DepositDefOf
    {
        public static DepositDef FreshWater = null!;
        public static DepositDef ArableSoil = null!;
        public static DepositDef Clay = null!;
        public static DepositDef Flint = null!;
        public static DepositDef Stone = null!;
        public static DepositDef Ore = null!;
        public static DepositDef Coal = null!;
        public static DepositDef Salt = null!;
        public static DepositDef Timber = null!;
        public static DepositDef Game = null!;
        public static DepositDef Ford = null!;
        public static DepositDef DefensibleGround = null!;
    }
}
