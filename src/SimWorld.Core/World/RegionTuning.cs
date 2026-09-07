namespace SimWorld.World
{
    /// <summary>
    /// Region-partition tuning for <see cref="Gen.WorldGenStep_Regions"/> (spec §5b.1). RimWorld has no
    /// region concept to source these from — every constant here is this port's own judgement call, sized
    /// to read sensibly against <c>Gen.WorldGenStep_Terrain</c>'s own elevation/rainfall ranges rather than
    /// picked in a vacuum.
    /// </summary>
    public static class RegionTuning
    {
        /// <summary>Land tiles a region aims to cover, on average. Chosen so a region reads as "a kind of
        /// place" — several biome patches, a river reach, a stretch of coast — rather than one region per
        /// settlement site or one region per continent.</summary>
        public const int TargetTilesPerRegion = 40;

        /// <summary>
        /// Seeds within one connected landmass are kept apart by at least this fraction of that landmass's
        /// average per-seed radius, so seeds spread roughly evenly instead of clustering — the same shape as
        /// <c>Gen.WorldGenStep_Factions.PickSettlementTile</c>'s minimum-separation rule, applied to region
        /// seeds instead of settlements.
        /// </summary>
        public const float SeedMinSeparationFactor = 0.7f;

        /// <summary>Baseline cost to cross between two ordinary, similar land tiles — the unit every other
        /// crossing cost below is relative to.</summary>
        public const float BaseCrossingCost = 1f;

        /// <summary>Extra crossing cost when the two tiles carry different biomes: a real ecological
        /// boundary, and the single strongest natural-frontier signal terrain offers.</summary>
        public const float BiomeChangeCost = 8f;

        /// <summary>Elevation difference (m) above which two tiles count as separated by "a large elevation
        /// step (a ridge)" rather than a gentle slope.</summary>
        public const float RidgeElevationThresholdMeters = 350f;

        /// <summary>Extra crossing cost once <see cref="RidgeElevationThresholdMeters"/> is cleared.</summary>
        public const float RidgeCrossingCost = 10f;

        /// <summary>Mild continuous cost per metre of elevation difference below the ridge threshold, so
        /// "similar elevation" (spec §5b.1's own phrase) still costs less than a middling difference even
        /// when neither tile is rugged enough to count as a ridge.</summary>
        public const float ElevationCostPerMeter = 0.01f;

        /// <summary>Extra crossing cost when a river link runs along the edge between the two tiles: a
        /// waterway is a natural boundary in its own right.</summary>
        public const float RiverCrossingCost = 6f;

        /// <summary>
        /// Extra crossing cost when exactly one of the two tiles is coastal (touches a water tile) and the
        /// other is not. Spec §5b.1 names "a coastline (tile.WaterCovered on either side)" as a natural
        /// frontier, but region growth never crosses a water tile at all (see
        /// <see cref="Gen.WorldGenStep_Regions"/>), so no land-land edge can literally have water "on either
        /// side" of it. Read instead as: the coastline itself is the frontier, and two land tiles disagreeing
        /// on whether they touch the sea are the two sides of it. Flagged as an own reading of an ambiguous
        /// line in the module's report-back.
        /// </summary>
        public const float CoastalMismatchCost = 5f;
    }
}
