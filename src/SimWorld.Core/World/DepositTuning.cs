namespace SimWorld.World
{
    /// <summary>
    /// Thresholds <c>Gen.WorldGenStep_Deposits</c> reads to turn generated terrain (elevation, hilliness,
    /// rainfall, biome, rivers) into per-tile <see cref="DepositDef"/> magnitudes (spec §5b.2). RimWorld has
    /// no deposit-from-terrain system to source these from — every constant below is this port's own
    /// judgement call, documented at its declaration per <c>CLAUDE.md</c>'s "say so plainly" rule, and sized
    /// to read sensibly against <c>Gen.WorldGenStep_Terrain</c>'s own elevation (-1500m..2200m) and rainfall
    /// (roughly 0..4000mm before the overall-rainfall multiplier) ranges rather than picked in a vacuum.
    /// </summary>
    public static class DepositTuning
    {
        // ---- Fresh water ----

        /// <summary>Magnitude granted when the tile carries a river link at all — terrain gives no stronger
        /// fresh-water signal than an actual watercourse.</summary>
        public const float FreshWaterRiverMagnitude = 1.0f;

        /// <summary>Magnitude granted when a neighbouring tile is a small enclosed body of water
        /// (<see cref="Tile.lakeCandidate"/>) rather than open ocean — a lake shore, not a sea coast.</summary>
        public const float FreshWaterLakeAdjacentMagnitude = 0.85f;

        /// <summary>Below this annual rainfall (mm), ambient rain alone is too little to read as a reliable
        /// fresh-water source absent a river or lake.</summary>
        public const float FreshWaterRainfallFloorMm = 1200f;

        /// <summary>At/above this rainfall (mm), ambient rain alone saturates its (capped) contribution.</summary>
        public const float FreshWaterRainfallSaturationMm = 2400f;

        /// <summary>Ceiling on rainfall's own contribution — reliable rain still reads as weaker than an
        /// actual watercourse or lake shore.</summary>
        public const float FreshWaterRainfallMaxMagnitude = 0.5f;

        // ---- Deep soil / arable ----

        /// <summary>Above this elevation (m), ground is treated as too far from a valley floor to farm.</summary>
        public const float ArableMaxElevationMeters = 400f;

        /// <summary>Rainfall band (mm) arable land scores best in; a triangular falloff either side of the
        /// midpoint models "moderate-to-high rainfall" rather than an all-or-nothing cutoff.</summary>
        public const float ArableRainfallMinMm = 600f;

        public const float ArableRainfallMaxMm = 2200f;

        /// <summary>Extra credit for a low-lying tile that also touches a river — a true floodplain.</summary>
        public const float ArableFloodplainBonus = 0.25f;

        // ---- Clay ----

        /// <summary>Two or more river links on one tile reads as a meander or confluence: classic clay
        /// deposition, so it gets a high, near-fixed magnitude rather than a graded one.</summary>
        public const float ClayBendMagnitude = 0.9f;

        /// <summary>A single river link on low ground still deposits clay, just less reliably than a bend.</summary>
        public const float ClayFloodplainMagnitude = 0.5f;

        /// <summary>Elevation ceiling (m) for the single-river-link floodplain case.</summary>
        public const float ClayMaxElevationMeters = 250f;

        // ---- Flint ----

        /// <summary>Elevation ceiling (m) for "lowland" in the flint rule.</summary>
        public const float FlintMaxElevationMeters = 300f;

        /// <summary>Rainfall (mm) above which the ground reads as too wet for the chalk/flint downs this
        /// deposit stands in for; magnitude ramps down to 0 as rainfall approaches this ceiling.</summary>
        public const float FlintRainfallCeilingMm = 1400f;

        /// <summary>Ceiling on flint's own magnitude even at zero rainfall — it is a useful but not
        /// dominant material next to stone/ore.</summary>
        public const float FlintMaxMagnitude = 0.8f;

        // ---- Stone (by Hilliness) ----

        public const float StoneMagnitudeSmallHills = 0.4f;
        public const float StoneMagnitudeLargeHills = 0.7f;
        public const float StoneMagnitudeMountainous = 1.0f;

        // ---- Ore (by Hilliness; rarer than stone at every band) ----

        /// <summary>Ore is rare but not absent on gentle hills.</summary>
        public const float OreMagnitudeSmallHills = 0.05f;

        public const float OreMagnitudeLargeHills = 0.35f;
        public const float OreMagnitudeMountainous = 0.85f;

        // ---- Coal (terrain-derived from swampiness/rainfall/elevation, deliberately NOT from hilliness the
        // way Ore is — spec §5b.2 requires coal to be "distinctly distributed from Ore, which sits in hills
        // and mountains". Real coal measures are ancient swamp and floodplain sediment: low-lying ground with
        // a long history of standing water, so this reads Tile.swampiness (the port's own closest proxy for
        // "historically boggy ground", already gated to low elevation/high rainfall by
        // Gen.WorldGenStep_Terrain.SwampinessFor) as the primary signal, with rainfall and river presence as
        // secondary corroborating signals and a hard hilliness ceiling so the two categories cannot converge
        // on the same terrain. RimWorld has no coal-formation model to source any of this from — every
        // constant here is this port's own judgement call. ----

        /// <summary>Elevation ceiling (m) for coal-bearing ground. Ancient swamp/floodplain basins are
        /// valley-floor terrain, never highland; sized like <see cref="ArableMaxElevationMeters"/> since both
        /// describe the same class of low ground.</summary>
        public const float CoalMaxElevationMeters = 350f;

        /// <summary>Coal never appears above <see cref="Hilliness.SmallHills"/> at all (see <see cref="OreMagnitudeLargeHills"/>/
        /// <see cref="OreMagnitudeMountainous"/> for the terrain Ore instead dominates), and even on gentle
        /// hills it reads weaker than on flat ground — a hill's own better drainage makes a lasting swamp
        /// less likely than on a true floodplain.</summary>
        public const float CoalSmallHillsFactor = 0.5f;

        /// <summary>Below this <see cref="Tile.swampiness"/>, the ground alone doesn't read as historically
        /// wet enough to carry coal; ramps to full credit at <see cref="CoalSwampinessCeiling"/>.</summary>
        public const float CoalSwampinessFloor = 0.1f;

        public const float CoalSwampinessCeiling = 0.5f;

        /// <summary>Rainfall band (mm) coal-bearing ground sits in even where the swampiness noise sample
        /// itself didn't land high — wide and wet, since sustained heavy rainfall over geological time is
        /// what waterlogs ground in the first place. Triangular falloff either side of the midpoint, the
        /// same idiom as <see cref="ArableRainfallMinMm"/>/<see cref="ArableRainfallMaxMm"/>.</summary>
        public const float CoalRainfallMinMm = 1200f;

        public const float CoalRainfallMaxMm = 3200f;

        /// <summary>Ceiling on how much rainfall alone (absent real swampiness) can contribute — a wet but
        /// never-boggy tile is a much weaker signal than an actual historic swamp.</summary>
        public const float CoalRainfallOnlyCeiling = 0.4f;

        /// <summary>Extra credit for a low-lying tile that also touches a river — a buried sediment basin,
        /// the same idea as <see cref="ArableFloodplainBonus"/> and <see cref="ClayFloodplainMagnitude"/>.</summary>
        public const float CoalFloodplainBonus = 0.2f;

        // ---- Salt ----

        /// <summary>Magnitude for a tile touching open ocean (not a lake) — salt panning/harvesting.</summary>
        public const float SaltCoastalMagnitude = 0.6f;

        /// <summary>Elevation floor (m) for the rare inland-spring case.</summary>
        public const float SaltSpringMinElevationMeters = 1200f;

        /// <summary>Rainfall floor (mm) for the same.</summary>
        public const float SaltSpringMinRainfallMm = 2000f;

        /// <summary>Inland springs are rarer and less productive than a coast, so capped lower.</summary>
        public const float SaltSpringMagnitude = 0.3f;

        // ---- Timber / Game (straight from the biome) ----

        /// <summary>Divisor normalizing <c>BiomeDef.plantDensity</c> (shipped content tops out at 1.8, tropical
        /// rainforest) into [0,1] with headroom left for a modded biome that goes higher.</summary>
        public const float PlantDensityNormalizer = 2.0f;

        /// <summary>Same idea for <c>BiomeDef.animalDensity</c> (shipped content tops out at 1.6).</summary>
        public const float AnimalDensityNormalizer = 2.0f;

        // ---- Ford ----

        /// <summary>Elevation ceiling (m) for a river reach shallow/gentle enough to ford on foot.</summary>
        public const float FordMaxElevationMeters = 200f;

        /// <summary>Magnitude floor once a tile qualifies as a ford at all, so the deposit never reads as
        /// "barely there" right at the elevation ceiling.</summary>
        public const float FordMinMagnitude = 0.3f;

        // ---- Defensible ground ----

        /// <summary>Elevation (m) a tile must clear over its neighbours' mean before "meaningfully above"
        /// applies at all.</summary>
        public const float DefensibleElevationMarginMeters = 150f;

        /// <summary>Elevation advantage (m, as a multiple of the margin above) at which the height signal
        /// saturates to a full 1.0 magnitude.</summary>
        public const float DefensibleElevationSaturationFactor = 2f;

        /// <summary>Share of a tile's edges that must carry a river link before it counts as "a bend or a
        /// peninsula" (RimWorld tiles have 5 or 6 neighbours; this is a fraction so both counts work).</summary>
        public const float DefensibleRiverFractionThreshold = 0.5f;

        /// <summary>Magnitude granted once the river-fraction test passes.</summary>
        public const float DefensibleRiverBendMagnitude = 0.7f;
    }
}
