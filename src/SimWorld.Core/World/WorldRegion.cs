using System.Collections.Generic;

namespace SimWorld.World
{
    /// <summary>
    /// A contiguous group of tiles with an identity: a name, a dominant biome, a climate summary and a
    /// resource profile (spec §5b.1). RimWorld has no equivalent — "region" is this port's own translation,
    /// the coarser-than-a-tile grain the player picks at before drilling into a site with
    /// <c>Siting.SiteScorer</c>. Built by <see cref="Gen.WorldGenStep_Regions"/>, then its
    /// <see cref="resourceProfile"/> filled in by <see cref="Gen.WorldGenStep_Deposits"/>. Never saved: like
    /// the rest of the tile grid, it is a pure function of <see cref="WorldInfo"/> and is rebuilt by
    /// <see cref="World.RegenerateGrid"/>.
    /// </summary>
    public sealed class WorldRegion
    {
        public int id;

        public string name = "";

        /// <summary>The biome held by the most member tiles; null only for a region with zero tiles (never produced in practice).</summary>
        public BiomeDef? dominantBiome;

        /// <summary>Mean of member tiles' <see cref="Tile.temperature"/>, °C.</summary>
        public float meanTemperature;

        /// <summary>Mean of member tiles' <see cref="Tile.rainfall"/>, mm.</summary>
        public float meanRainfall;

        /// <summary>Every land tile id this region owns. Disjoint from every other region's; water tiles never appear in any region's list.</summary>
        public readonly List<int> tiles = new List<int>();

        public readonly ResourceProfile resourceProfile = new ResourceProfile();
    }

    /// <summary>
    /// A region's coarse resource summary: the mean magnitude of each <see cref="DepositDef"/> across its
    /// member tiles (spec §5b.1: "not exact deposits: a region promises a kind of place, and the specifics
    /// are stage two"). Filled in by <see cref="Gen.WorldGenStep_Deposits"/> once per-tile deposits exist.
    /// </summary>
    public sealed class ResourceProfile
    {
        private readonly Dictionary<DepositDef, float> meanMagnitude = new Dictionary<DepositDef, float>();

        public float MeanMagnitudeOf(DepositDef def) => meanMagnitude.TryGetValue(def, out float v) ? v : 0f;

        public void SetMeanMagnitude(DepositDef def, float value) => meanMagnitude[def] = value;

        public IReadOnlyDictionary<DepositDef, float> All => meanMagnitude;
    }
}
