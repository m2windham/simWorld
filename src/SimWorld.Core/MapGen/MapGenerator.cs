using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Entry point of local map generation (RimWorld: <c>Verse.MapGenerator</c>). This is the seam spec §11.2
    /// calls "the next substantial piece of work": <see cref="GenerateMapFor"/> takes a settlement's world
    /// tile — which already carries biome, elevation, hilliness, rainfall, swampiness, rivers, roads and
    /// deposits — and generates the interior map that tile promised, so a settlement is no longer a world
    /// object with no inside.
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>RimWorld's default local map size.</summary>
        public const int DefaultMapSizeX = 250;
        public const int DefaultMapSizeZ = 250;

        /// <summary>
        /// Generates the interior map for a settlement (spec §5b.5's "settlement interior" seam).
        /// <paramref name="sizeOverride"/> stands in for "the settlement's size/importance" the brief calls
        /// for: <see cref="global::SimWorld.WorldObject"/> does not yet carry any such field (see the
        /// report), so today every caller gets <see cref="DefaultMapSizeX"/>×<see cref="DefaultMapSizeZ"/>
        /// unless it asks for something else explicitly.
        /// </summary>
        public static Map.Map GenerateMapFor(WorldObject settlement, World.World world, IntVec2? sizeOverride = null, MapGeneratorDef? genDef = null)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (world == null) throw new ArgumentNullException(nameof(world));

            Tile tile = world.grid.Tiles[settlement.tile];
            string seedString = world.info.seedString + "_map_" + settlement.tile.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return GenerateMap(tile, settlement.tile, seedString, sizeOverride, genDef);
        }

        /// <summary>
        /// The underlying worker: generates a map for any world tile given an explicit seed, without needing
        /// a full <see cref="global::SimWorld.World"/>/<see cref="WorldObject"/> pair. Tests use this
        /// directly to generate from hand-built tiles and to prove the tile→map correspondence.
        /// </summary>
        public static Map.Map GenerateMap(Tile tile, int tileId, string seedString, IntVec2? sizeOverride = null, MapGeneratorDef? genDef = null)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (seedString == null) throw new ArgumentNullException(nameof(seedString));

            IntVec2 size = sizeOverride ?? new IntVec2(DefaultMapSizeX, DefaultMapSizeZ);
            if (size.x <= 0 || size.z <= 0) throw new ArgumentOutOfRangeException(nameof(sizeOverride), "Map size must be positive.");
            MapGeneratorDef generatorDef = genDef ?? MapGeneratorDefOf.Base;

            var map = new Map.Map(size.x, size.z, TerrainDefOf.Soil);
            map.tile = tileId;
            var ctx = new MapGenContext(map, tile, tileId, seedString);

            var steps = new List<GenStepDef>(generatorDef.genSteps);
            steps.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (GenStepDef stepDef in steps)
            {
                stepDef.Worker.Generate(ctx);
            }
            return map;
        }
    }
}
