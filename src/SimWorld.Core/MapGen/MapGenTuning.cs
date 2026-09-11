using SimWorld.Map;
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

        /// <summary>
        /// How thick the undergrowth on a tile should be, 0..1: the biome's own <see cref="BiomeDef.plantDensity"/>
        /// blended evenly with how much Timber the tile carries. Lives here, rather than inline in
        /// <see cref="GenStep_Scatterers"/> where it started, because the map's wild plants are placed by
        /// generation and then *maintained* by <c>Building.WildPlantSpawner</c> — and a map that regrows
        /// toward a different density than it was generated at would drift away from its own tile over time.
        /// One formula, two readers.
        /// </summary>
        public static float WildPlantSignal(Tile tile)
        {
            if (tile == null) return 0f;
            float plantDensity = tile.biome?.plantDensity ?? 0f;
            float timberMagnitude = tile.DepositMagnitude(DepositDefOf.Timber);
            return GenMath.Clamp01(plantDensity * 0.5f + timberMagnitude * 0.5f);
        }

        // ---- Ruins (GenStep_Ruins): wall rectangles with gaps and rubble, weathered rather than pristine.
        // See that class's own doc comment for what of RimWorld's real ruin generation (RuleDef/SymbolResolver,
        // GenStep_ScatterShrines) this deliberately does not port. Every number below is this port's own
        // judgement call — RimWorld's real per-tile ruin count and size range are not sourced or verified
        // against decompiled source in this sandbox — pinned by a behavioural test (ruins appear in believable
        // numbers, density scales with map area, a ruin never encloses a pocket the region graph cannot reach
        // from the border) rather than trusted as a literal. ----

        /// <summary>Map cells per placement attempt; scales attempt count — and so ruin density — with map area, the same way every other scatterer in this file scales off cell count rather than a flat count.</summary>
        public const float RuinCellsPerAttempt = 2500f;

        /// <summary>Side length range (each axis independently) of one ruin's outer wall rectangle.</summary>
        public static readonly IntRange RuinSizeRange = new IntRange(4, 9);

        /// <summary>Cells kept clear of the map border on every side, so a ruin's own rectangle — before <see cref="RuinCellsMarginBeforePlacement"/> is even applied around it — never gets clipped by the map boundary.</summary>
        public const int RuinEdgeMargin = 2;

        /// <summary>Cells expanded around a candidate rect (and around every already-placed ruin) before the water/rock/overlap checks — a placement margin, not a spacing rule of its own: it keeps a ruin off a mountain's edge or a riverbank rather than just barely inside it, and keeps two ruins from reading as one fused structure.</summary>
        public const int RuinCellsMarginBeforePlacement = 4;

        /// <summary>Chance an ordinary (non-corner) wall-ring cell is a gap — missing wall — rather than standing wall.</summary>
        public const float RuinWallGapChance = 0.3f;

        /// <summary>Corners get a lower gap chance than an ordinary wall cell, so a ruin still reads as a rectangle rather than a scatter of unrelated wall stubs.</summary>
        public const float RuinCornerGapChance = 0.1f;

        /// <summary>
        /// However the dice fall, at least this many of the ring's non-corner cells are forced open. This is
        /// the structural half of "never walls a pawn into a pocket it cannot leave" (see GenStep_Ruins's own
        /// doc for why a structural guarantee was chosen over a runtime region-graph repair, and for the
        /// region-graph test that checks the result rather than trusting the construction alone).
        /// </summary>
        public const int RuinMinGuaranteedGaps = 2;

        /// <summary>Chance a gap cell also carries a piece of rubble (a loose rock chunk) rather than standing bare.</summary>
        public const float RuinRubbleChance = 0.5f;

        /// <summary>Fraction of MaxHitPoints (min, max) a standing ruin wall keeps — weathered, never pristine. A gap already models a wall reduced all the way to nothing, so this range never reaches down to that.</summary>
        public const float RuinWallHitPointsFractionMin = 0.15f;
        public const float RuinWallHitPointsFractionMax = 0.65f;

        /// <summary>Chance a ruin's interior is roofed (an ancient structure that partly survived) rather than open to the sky.</summary>
        public const float RuinRoofedChance = 0.35f;

        /// <summary>Chance a ruin holds any loot at all.</summary>
        public const float RuinLootChance = 0.55f;

        /// <summary>Loot pieces placed when a ruin does carry loot.</summary>
        public static readonly IntRange RuinLootItemCountRange = new IntRange(1, 3);

        /// <summary>Stack size range for a stackable loot resource (silver, steel, wood); clamped to the item's own stackLimit besides.</summary>
        public static readonly IntRange RuinLootStackRange = new IntRange(5, 40);

        // ---- Roads (GenStep_Roads) ----

        /// <summary>
        /// Width, in cells, of a carried-over street. SimWorld's own — unlike <see cref="RiverDef.widthOnWorld"/>,
        /// which gives rivers a real world-scale number to derive a cell width from, RimWorld's local roads
        /// have no published width to source at all; pinned by a behavioural test (a street is present and
        /// several cells wide, not an exact literal) rather than trusted as a literal.
        /// </summary>
        public const int RoadWidthCells = 3;

        // ---- Interior map sizing (Settlement.EnterMap) ----

        /// <summary>
        /// The map-size ladder <see cref="MapSizeForPopulation"/> picks a rung from. The brief that asked for
        /// population-based sizing says outright that RimWorld's own local map size is a fixed player-chosen
        /// setting with nothing behind it to source, so every rung here — and every threshold in
        /// <see cref="MapSizeForPopulation"/> that picks among them — is this port's own judgement call,
        /// anchored on the existing flat default (<see cref="MapGenerator.DefaultMapSizeX"/>) as the middle
        /// rung so a settlement of unremarkable population gets exactly what every settlement got before this
        /// existed. Only the *band* (a larger population never picks a smaller rung) is asserted by test,
        /// never a literal side length.
        /// </summary>
        private static readonly int[] MapSideLadder =
        {
            MapGenerator.DefaultMapSizeX - 50,
            MapGenerator.DefaultMapSizeX - 25,
            MapGenerator.DefaultMapSizeX,
            MapGenerator.DefaultMapSizeX + 25,
            MapGenerator.DefaultMapSizeX + 50,
            MapGenerator.DefaultMapSizeX + 75,
            MapGenerator.DefaultMapSizeX + 100,
        };

        /// <summary>Population at which the ladder's second rung unlocks — the founding band's own maximum (spec §5b.3: 20-40) already clears it.</summary>
        private const int FirstRungPopulation = 40;

        /// <summary>Population roughly multiplies by this for every rung further up the ladder.</summary>
        private const double RungPopulationGrowth = 2.5;

        /// <summary>
        /// A larger settlement gets a larger interior (spec: "size the map by the settlement, not by a
        /// constant") — <paramref name="population"/> is <see cref="World.Settlement.TotalPopulation"/>, read
        /// by <see cref="World.Settlement.EnterMap"/> at first entry. Square, like every map this generator
        /// already produces.
        /// </summary>
        public static IntVec2 MapSizeForPopulation(int population)
        {
            int rung = 0;
            double threshold = FirstRungPopulation;
            while (rung < MapSideLadder.Length - 1 && population >= threshold)
            {
                rung++;
                threshold *= RungPopulationGrowth;
            }
            int side = MapSideLadder[rung];
            return new IntVec2(side, side);
        }
    }
}
