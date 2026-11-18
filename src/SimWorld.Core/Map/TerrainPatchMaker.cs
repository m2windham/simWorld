using System.Collections.Generic;
using SimWorld.Noise;

namespace SimWorld.Map
{
    /// <summary>
    /// One layer of patches a biome lays over its fertility bands: a Perlin field sampled at each cell's own
    /// coordinates, read through <see cref="thresholds"/>, on cells whose fertility lies in
    /// [<see cref="minFertility"/>, <see cref="maxFertility"/>] (RimWorld: <c>Verse.TerrainPatchMaker</c>, same
    /// fields and defaults, listed in <c>BiomeDef.terrainPatchMakers</c> and consulted by
    /// <c>GenStep_Terrain.TerrainFrom</c> before the fertility bands). This is where most of a RimWorld map's
    /// rich soil, mud and marsh come from.
    /// <para/>
    /// <b>Departures.</b> RimWorld caches the noise on the def and re-seeds it from the global <c>Rand</c>
    /// whenever the map changes; here the def holds no state, and <see cref="MapGen.GenStep_Terrain"/> builds
    /// the noise per generation (<see cref="MakeNoise"/>) from its own seeded stream. RimWorld's
    /// <c>minSize</c> (drop a patch smaller than N cells, by flood fill) is not ported: no biome here uses it.
    /// </summary>
    public class TerrainPatchMaker
    {
        public List<TerrainThreshold> thresholds = new List<TerrainThreshold>();
        public float perlinFrequency = 0.01f;
        public float perlinLacunarity = 2f;
        public float perlinPersistence = 0.5f;
        public int perlinOctaves = 6;

        /// <summary>Fertility window, in the raw-noise units of <c>BiomeDef.terrainsByFertility</c>.</summary>
        public float minFertility = -999f;
        public float maxFertility = 999f;

        /// <summary>The patch field for one map (RimWorld: <c>TerrainPatchMaker.Init</c>; its QualityMode.Medium is
        /// libnoise's Standard).</summary>
        public Perlin MakeNoise(int seed) =>
            new Perlin(perlinFrequency, perlinLacunarity, perlinPersistence, perlinOctaves, seed, NoiseQuality.Standard);

        /// <summary>The patch terrain at <paramref name="c"/>, or null where this layer lays nothing (RimWorld:
        /// <c>TerrainPatchMaker.TerrainAt</c>, without <c>minSize</c>).</summary>
        public TerrainDef? TerrainAt(IntVec3 c, Perlin noise, float rawFertility)
        {
            if (rawFertility < minFertility || rawFertility > maxFertility) return null;
            return TerrainThreshold.TerrainAtValue(thresholds, (float)noise.GetValue(c.x, c.y, c.z));
        }
    }
}
