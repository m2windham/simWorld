using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Thresholds the <c>MapGen.GenStep_*</c> pipeline reads to turn one world tile's already-generated data
    /// (elevation, hilliness, rainfall, swampiness, biome, rivers, deposits) into a local map (spec §5, §5b.5:
    /// "a settlement's interior when the player enters it"). RimWorld's own local map generator is not
    /// published in a form this port can source exact constants from (unlike world gen's land-fraction and
    /// lapse-rate constants, which the spec's own references pin down); every number below is this port's own
    /// judgement call, documented at its declaration per <c>CLAUDE.md</c>'s "say so plainly" rule and pinned by
    /// a behavioural test (a band, a trend, or a tile/map correspondence) rather than asserted as a literal.
    /// </summary>
    public static class MapGenTuning
    {
        // ---- Elevation / fertility noise (GenStep_ElevationFertility) ----

        /// <summary>How many noise cycles fit across one map edge; higher reads "busier" at a fixed map size.</summary>
        public const double NoiseFrequency = 6.0;

        // ---- Terrain (GenStep_Terrain) ----

        /// <summary>At/above this <see cref="Tile.swampiness"/>, ground reads as boggy enough for Marsh/MarshyTerrain rather than ordinary soil.</summary>
        public const float MarshSwampinessFloor = 0.3f;

        /// <summary>Within the swampy band, fertility at/above this reads as true Marsh; below it, the drier MarshyTerrain.</summary>
        public const float MarshFertilityFloor = 0.5f;

        /// <summary>Biome forageability at/below this, combined with <see cref="AridRainfallCeilingMm"/>, reads as arid enough for bare Sand.</summary>
        public const float AridForageabilityCeiling = 0.25f;

        /// <summary>Annual rainfall (mm) ceiling for the same arid/Sand rule.</summary>
        public const float AridRainfallCeilingMm = 300f;

        /// <summary>Fertility at/above this paints SoilRich instead of ordinary Soil.</summary>
        public const float RichSoilFertilityFloor = 0.75f;

        /// <summary>Fertility at/below this paints bare Gravel instead of ordinary Soil.</summary>
        public const float GravelFertilityCeiling = 0.2f;

        /// <summary>Map cells of river width per world-scale <see cref="RiverDef.widthOnWorld"/> unit.</summary>
        public const float RiverWidthCellsPerWorldUnit = 3f;

        /// <summary>At/above this <see cref="RiverDef.widthOnWorld"/>, the river channel is deep (impassable) rather than shallow (fordable).</summary>
        public const float RiverDeepWidthOnWorldFloor = 2.0f;

        /// <summary>How many noise cycles the river's meander completes across the map's far axis.</summary>
        public const double RiverWiggleFrequency = 2.2;

        /// <summary>Meander amplitude as a fraction of the map's near axis.</summary>
        public const float RiverWiggleAmplitude = 0.22f;

        // ---- Rocky outcrops & mountains (GenStep_RocksAndMountains) ----

        /// <summary>
        /// Target fraction of the map's cells that read as solid natural rock before any deposit adjustment,
        /// by <see cref="Hilliness"/>. Flat never gets rock; Impassable is mostly mountain but still leaves
        /// open ground, since a settlement with literally no buildable cell would be unplayable.
        /// </summary>
        public static float TargetRockFraction(Hilliness hilliness) => hilliness switch
        {
            Hilliness.Flat => 0f,
            Hilliness.SmallHills => 0.08f,
            Hilliness.LargeHills => 0.22f,
            Hilliness.Mountainous => 0.45f,
            Hilliness.Impassable => 0.7f,
            _ => 0f,
        };

        /// <summary>Extra rock-fraction credit per full unit of the tile's Stone deposit magnitude.</summary>
        public const float StoneFractionBonus = 0.15f;

        /// <summary>Guaranteed mineable ore veins per full unit of the tile's Ore deposit magnitude, so any nonzero magnitude reads as "ore is here" rather than a coin flip.</summary>
        public const float OreVeinsPerFullMagnitude = 12f;

        // ---- Caves (GenStep_Caves): a directional random walk, not cellular automata — see the report. ----

        /// <summary>Caves attempted per full-mountain (rock fraction 1.0) map; scaled down linearly by actual rock fraction.</summary>
        public const float CavesPerFullMountain = 7f;

        public const int CaveMinLength = 12;
        public const int CaveMaxLength = 45;

        /// <summary>Chance the walk keeps its current heading each step rather than turning to a new random cardinal direction.</summary>
        public const float CaveStraightChance = 0.78f;

        /// <summary>Chance a given cave carves two cells wide rather than one.</summary>
        public const float CaveWideChance = 0.3f;

        // ---- Roofs (GenStep_Roofs) ----

        /// <summary>Chebyshev radius sampled around a non-rock cell to grade how enclosed by rock it is.</summary>
        public const int RoofSampleRadius = 2;

        /// <summary>Local rock fraction (within <see cref="RoofSampleRadius"/>) at/above which a non-rock cell is deep enough under the mountain for a thick roof.</summary>
        public const float ThickRoofRockFraction = 0.85f;

        /// <summary>Local rock fraction at/above which a non-rock cell gets at least a thin roof (a cave passage or a mountain's edge).</summary>
        public const float ThinRoofRockFraction = 0.35f;

        // ---- Scatterers (GenStep_Scatterers) ----

        /// <summary>Cells per scattered chunk at Stone magnitude 0 (sparse) and 1 (dense); linearly interpolated between.</summary>
        public const float ChunkCellsPerItemSparse = 6000f;
        public const float ChunkCellsPerItemDense = 500f;

        public static float ChunkCellsPerItem(float stoneMagnitude) =>
            GenMath.Lerp(ChunkCellsPerItemSparse, ChunkCellsPerItemDense, GenMath.Clamp01(stoneMagnitude));

        /// <summary>Cells per scattered wild-plant marker at plant signal 0 (sparse) and 1 (dense); linearly interpolated between.</summary>
        public const float PlantCellsPerItemSparse = 900f;
        public const float PlantCellsPerItemDense = 30f;

        public static float PlantCellsPerItem(float plantSignal) =>
            GenMath.Lerp(PlantCellsPerItemSparse, PlantCellsPerItemDense, GenMath.Clamp01(plantSignal));
    }
}
