using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// One band of a value — fertility, in <c>BiomeDef.terrainsByFertility</c> — and the terrain that paints
    /// it (RimWorld: <c>Verse.TerrainThreshold</c>, same fields and defaults). Bands are read in order and
    /// both ends are inclusive, so where two touch the earlier one wins.
    /// </summary>
    public class TerrainThreshold
    {
        public TerrainDef terrain = null!;
        public float min = -1000f;
        public float max = 1000f;

        /// <summary>The terrain of the first band holding <paramref name="val"/>, or null when none does or
        /// there are no bands (RimWorld: <c>TerrainThreshold.TerrainAtValue</c>).</summary>
        public static TerrainDef? TerrainAtValue(List<TerrainThreshold>? threshes, float val)
        {
            if (threshes == null) return null;
            for (int i = 0; i < threshes.Count; i++)
            {
                if (threshes[i].min <= val && threshes[i].max >= val) return threshes[i].terrain;
            }
            return null;
        }
    }
}
