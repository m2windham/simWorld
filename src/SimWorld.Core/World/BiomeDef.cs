using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>
    /// A climate zone (RimWorld: <c>RimWorld.BiomeDef</c>). Plant/animal spawn tables belong to later
    /// systems (production, fauna); this port carries only what world generation and travel need.
    /// </summary>
    public class BiomeDef : Def
    {
        public Type workerClass = typeof(BiomeWorker);

        public float animalDensity = 1f;
        public float plantDensity = 1f;

        /// <summary>Mean time between disease incidents on a settlement here, in days. Infinity = never.</summary>
        public float diseaseMtbDays = float.PositiveInfinity;

        /// <summary>Relative weight when auto-placing a settlement (see <c>Gen.WorldGenStep_Factions</c>).</summary>
        public float settlementSelectionWeight = 1f;

        /// <summary>No pawn or caravan can enter (ocean, deep lake).</summary>
        public bool impassable;

        /// <summary>A settlement may be founded on a tile with this biome.</summary>
        public bool canBuildBase = true;

        /// <summary>Eligible for unweighted random pick (e.g. quest/scenario site selection); false for biomes only reached by score, like the ice caps.</summary>
        public bool canAutoChoose = true;

        public bool allowRoads = true;
        public bool allowRivers = true;

        /// <summary>Multiplies travel cost across a tile of this biome.</summary>
        public float movementDifficulty = 1f;

        /// <summary>0..1; how much food a forager can find here.</summary>
        public float forageability;

        public float wildPlantRegrowDays = 10f;

        /// <summary>Marks biomes RimWorld excludes from ordinary starting-site selection (ice sheet, extreme desert).</summary>
        public bool isExtremeBiome;

        private BiomeWorker? workerInt;

        public BiomeWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (BiomeWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!typeof(BiomeWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from BiomeWorker.";
        }
    }

    /// <summary>
    /// Scores how well a biome fits a generated tile (RimWorld: <c>RimWorld.BiomeWorker</c>). World gen picks,
    /// per tile, the biome whose worker returns the highest score; each worker gates on
    /// <see cref="Tile.WaterCovered"/> first so land and water biomes never compete with each other.
    /// Every band below is this port's own simplified approximation of RimWorld's decompiled scoring
    /// (documented per class) — shaped the same way, not numerically identical.
    /// </summary>
    public class BiomeWorker
    {
        public BiomeDef def = null!;

        public virtual float GetScore(Tile tile, int tileId) => 0f;
    }

    /// <summary>Deep, open water that isn't a small enclosed body (see <see cref="Tile.lakeCandidate"/>).</summary>
    public class BiomeWorker_Ocean : BiomeWorker
    {
        public override float GetScore(Tile tile, int tileId) => tile.WaterCovered && !tile.lakeCandidate ? 100f : -100f;
    }

    /// <summary>A small enclosed body of water (flood-fill component under the lake-size threshold).</summary>
    public class BiomeWorker_Lake : BiomeWorker
    {
        public override float GetScore(Tile tile, int tileId) => tile.WaterCovered && tile.lakeCandidate ? 100f : -100f;
    }

    /// <summary>Water cold enough to freeze solid year-round; outranks Ocean/Lake so polar seas get it instead.</summary>
    public class BiomeWorker_SeaIce : BiomeWorker
    {
        public const float TemperatureThreshold = -40f;

        public override float GetScore(Tile tile, int tileId) => tile.WaterCovered && tile.temperature < TemperatureThreshold ? 150f : -100f;
    }

    /// <summary>
    /// Base for every land biome: never eligible on a water tile, otherwise scores by closeness to an
    /// "ideal" (temperature, rainfall) point, RimWorld's real approach in spirit (nearest-climate-wins) if
    /// not in exact constants.
    /// </summary>
    public abstract class BiomeWorker_Land : BiomeWorker
    {
        protected abstract float IdealTemperature { get; }
        protected abstract float IdealRainfall { get; }

        /// <summary>°C of temperature deviation treated as equivalent to 1mm of rainfall deviation.</summary>
        protected virtual float TemperatureWeight => 2f;
        protected virtual float RainfallWeight => 0.04f;

        public override float GetScore(Tile tile, int tileId)
        {
            if (tile.WaterCovered) return -1000f;
            float score = 100f
                - Math.Abs(tile.temperature - IdealTemperature) * TemperatureWeight
                - Math.Abs(tile.rainfall - IdealRainfall) * RainfallWeight;
            return score + ExtraScore(tile);
        }

        /// <summary>Gating bonuses/penalties beyond the ideal-point distance (swamp requirement, desert rainfall caps, ...).</summary>
        protected virtual float ExtraScore(Tile tile) => 0f;
    }

    /// <summary>Permanent ice cap; RimWorld gates this on being far colder than any vegetated biome tolerates.</summary>
    public class BiomeWorker_IceSheet : BiomeWorker
    {
        public const float TemperatureThreshold = -25f;

        public override float GetScore(Tile tile, int tileId)
        {
            if (tile.WaterCovered) return -1000f;
            return tile.temperature <= TemperatureThreshold ? 100f - tile.temperature : -100f;
        }
    }

    /// <summary>Cold, low vegetation, permafrost-adjacent.</summary>
    public class BiomeWorker_Tundra : BiomeWorker_Land
    {
        protected override float IdealTemperature => -3f;
        protected override float IdealRainfall => 500f;
    }

    /// <summary>Cold coniferous forest belt.</summary>
    public class BiomeWorker_BorealForest : BiomeWorker_Land
    {
        protected override float IdealTemperature => 1f;
        protected override float IdealRainfall => 1200f;
    }

    /// <summary>Cold, waterlogged ground; only wins where <see cref="Tile.swampiness"/> is meaningful.</summary>
    public class BiomeWorker_ColdBog : BiomeWorker_Land
    {
        protected override float IdealTemperature => 0f;
        protected override float IdealRainfall => 1600f;
        protected override float ExtraScore(Tile tile) => tile.swampiness >= 0.3f ? 30f : -80f;
    }

    /// <summary>Temperate deciduous forest — RimWorld's default farmland biome.</summary>
    public class BiomeWorker_TemperateForest : BiomeWorker_Land
    {
        protected override float IdealTemperature => 12f;
        protected override float IdealRainfall => 1600f;
    }

    /// <summary>Temperate wetland.</summary>
    public class BiomeWorker_TemperateSwamp : BiomeWorker_Land
    {
        protected override float IdealTemperature => 14f;
        protected override float IdealRainfall => 1800f;
        protected override float ExtraScore(Tile tile) => tile.swampiness >= 0.3f ? 30f : -80f;
    }

    /// <summary>Hot, very wet jungle.</summary>
    public class BiomeWorker_TropicalRainforest : BiomeWorker_Land
    {
        protected override float IdealTemperature => 26f;
        protected override float IdealRainfall => 3000f;
    }

    /// <summary>Hot wetland.</summary>
    public class BiomeWorker_TropicalSwamp : BiomeWorker_Land
    {
        protected override float IdealTemperature => 26f;
        protected override float IdealRainfall => 2400f;
        protected override float ExtraScore(Tile tile) => tile.swampiness >= 0.3f ? 30f : -80f;
    }

    /// <summary>Warm, dry scrubland between forest and true desert.</summary>
    public class BiomeWorker_AridShrubland : BiomeWorker_Land
    {
        protected override float IdealTemperature => 20f;
        protected override float IdealRainfall => 400f;
    }

    /// <summary>Hot, dry desert.</summary>
    public class BiomeWorker_Desert : BiomeWorker_Land
    {
        protected override float IdealTemperature => 24f;
        protected override float IdealRainfall => 150f;
        protected override float ExtraScore(Tile tile) => tile.rainfall > 600f ? -80f : 0f;
    }

    /// <summary>The driest, hottest band; only wins on very low rainfall.</summary>
    public class BiomeWorker_ExtremeDesert : BiomeWorker_Land
    {
        protected override float IdealTemperature => 30f;
        protected override float IdealRainfall => 50f;
        protected override float ExtraScore(Tile tile) => tile.rainfall < 200f && tile.temperature > 28f ? 40f : -200f;
    }
}
