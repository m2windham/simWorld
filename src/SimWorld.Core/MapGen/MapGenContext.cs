using System;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Per-generation state shared across a <see cref="MapGeneratorDef"/>'s ordered <see cref="GenStep"/>s.
    /// RimWorld keeps this kind of generation-time scratch data in <c>MapGenerator</c>'s own static
    /// dictionaries (<c>floatGrids</c>, <c>data</c>) rather than as fields on the (effectively singleton, one
    /// per <see cref="GenStepDef"/>) step instances; this port uses a typed context object instead so the
    /// two scratch grids and the rock mask are compiler-checked rather than looked up by string key.
    /// </summary>
    public sealed class MapGenContext
    {
        public readonly Map.Map map;

        /// <summary>The world tile this map is generated for — the seam <see cref="MapGenerator"/> exists to serve.</summary>
        public readonly Tile tile;

        public readonly int tileId;

        /// <summary>Base seed text every <see cref="GenStep"/> derives its own stream from (see <see cref="GenStep.SeededStream"/>).</summary>
        public readonly string seedString;

        /// <summary>0..1 per cell, built by <see cref="GenStep_ElevationFertility"/>; read by <see cref="GenStep_RocksAndMountains"/>.</summary>
        public float[] elevationGrid = Array.Empty<float>();

        /// <summary>0..1 per cell, built by <see cref="GenStep_ElevationFertility"/>; read by <see cref="GenStep_Terrain"/> and <see cref="GenStep_Scatterers"/>.</summary>
        public float[] fertilityGrid = Array.Empty<float>();

        /// <summary>
        /// True where <see cref="GenStep_RocksAndMountains"/> placed a natural rock (or ore vein) edifice.
        /// <see cref="GenStep_Caves"/> clears cells it carves through, so by the time <see cref="GenStep_Roofs"/>
        /// and <see cref="GenStep_Scatterers"/> read it, it reflects the mountain's final, cave-riddled shape.
        /// </summary>
        public bool[] rock = Array.Empty<bool>();

        public MapGenContext(Map.Map map, Tile tile, int tileId, string seedString)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.tile = tile ?? throw new ArgumentNullException(nameof(tile));
            this.tileId = tileId;
            this.seedString = seedString ?? throw new ArgumentNullException(nameof(seedString));
        }
    }
}
