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

    /// <summary>
    /// ThingDefs <c>MapGen</c> places directly: natural rock, a mineable ore vein, loose chunks, wild plant
    /// growth, and — for <see cref="GenStep_Ruins"/> — the wall it builds, the stuff-capable resource Defs it
    /// may build that wall from, and the loot it may scatter inside. Every one of these already exists as
    /// ordinary content elsewhere (Building, Items, Weapons) and is only referenced here, never invented.
    /// </summary>
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

        // ---- Ruins (GenStep_Ruins) ----

        public static ThingDef Wall = null!;

        /// <summary>Stuff-capable resource Defs a ruin wall may be built from (Stony/Metallic/Woody).</summary>
        public static ThingDef BlocksSandstone = null!;
        public static ThingDef Steel = null!;
        public static ThingDef WoodLog = null!;

        /// <summary>Loot a ruin may scatter inside: stackable resources plus a couple of hand weapons.</summary>
        public static ThingDef Silver = null!;
        public static ThingDef MeleeWeapon_Knife = null!;
        public static ThingDef MeleeWeapon_Club = null!;
        public static ThingDef Bow_Short = null!;
    }
}
