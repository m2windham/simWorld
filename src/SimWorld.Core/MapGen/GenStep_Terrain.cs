using System;
using SimWorld.Map;
using SimWorld.Noise;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Paints every cell's ground terrain from the elevation/fertility grids plus the tile's own biome and
    /// swampiness, then — if the tile carries a river — carves a meandering band of water clear across the
    /// map. RimWorld renders rivers as splines sized by <see cref="RiverDef.widthOnWorld"/>; this is this
    /// port's own simplification (a single noise-perturbed band per map), but it keeps the one correspondence
    /// the spec calls load-bearing: a tile on a river always produces a map with water crossing it (spec §5).
    /// </summary>
    public class GenStep_Terrain : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            Map.Map map = ctx.map;
            Tile tile = ctx.tile;
            BiomeDef? biome = tile.biome;
            int sizeX = map.Size.x, sizeZ = map.Size.z;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int i = map.cellIndices.CellToIndex(x, z);
                    var c = new IntVec3(x, 0, z);
                    TerrainDef terrain = PickBaseTerrain(tile, biome, ctx.fertilityGrid[i]);
                    map.terrainGrid.SetTerrain(c, terrain);
                }
            }

            if (tile.Rivers.Count > 0)
            {
                CarveRiver(ctx, rand);
            }
        }

        private static TerrainDef PickBaseTerrain(Tile tile, BiomeDef? biome, float fertility)
        {
            if (tile.swampiness >= MapGenTuning.MarshSwampinessFloor)
            {
                return fertility >= MapGenTuning.MarshFertilityFloor ? MapGenTerrainDefOf.Marsh : MapGenTerrainDefOf.MarshyTerrain;
            }
            bool arid = biome != null && biome.forageability <= MapGenTuning.AridForageabilityCeiling && tile.rainfall <= MapGenTuning.AridRainfallCeilingMm;
            if (arid)
            {
                return TerrainDefOf.Sand;
            }
            if (fertility >= MapGenTuning.RichSoilFertilityFloor)
            {
                return MapGenTerrainDefOf.SoilRich;
            }
            if (fertility <= MapGenTuning.GravelFertilityCeiling)
            {
                return TerrainDefOf.Gravel;
            }
            return TerrainDefOf.Soil;
        }

        private static void CarveRiver(MapGenContext ctx, RandomStream rand)
        {
            Map.Map map = ctx.map;
            RiverDef? widest = WidestRiver(ctx.tile);
            if (widest == null) return;

            int width = Math.Max(1, (int)Math.Round(widest.widthOnWorld * MapGenTuning.RiverWidthCellsPerWorldUnit));
            TerrainDef waterTerrain = widest.widthOnWorld >= MapGenTuning.RiverDeepWidthOnWorldFloor ? TerrainDefOf.WaterDeep : TerrainDefOf.WaterShallow;

            bool alongX = rand.Bool;
            var wiggle = new Perlin(frequency: MapGenTuning.RiverWiggleFrequency, lacunarity: 2.0, persistence: 0.5, octaveCount: 3, seed: rand.Int, quality: NoiseQuality.Standard);

            int runLength = alongX ? map.Size.z : map.Size.x;
            int crossSize = alongX ? map.Size.x : map.Size.z;
            double invRunLength = 1.0 / Math.Max(1, runLength);

            for (int along = 0; along < runLength; along++)
            {
                double sample = wiggle.GetValue(along * invRunLength, 0.0, 0.0);
                int center = (int)Math.Round(crossSize * 0.5 + sample * crossSize * MapGenTuning.RiverWiggleAmplitude);
                CarveRiverBand(map, waterTerrain, center, width, along, alongX);
            }
        }

        private static void CarveRiverBand(Map.Map map, TerrainDef waterTerrain, int center, int width, int along, bool alongX)
        {
            int crossSize = alongX ? map.Size.x : map.Size.z;
            int half = width / 2;
            for (int offset = -half; offset <= half; offset++)
            {
                int cross = center + offset;
                if (cross < 0 || cross >= crossSize) continue;
                IntVec3 c = alongX ? new IntVec3(cross, 0, along) : new IntVec3(along, 0, cross);
                map.terrainGrid.SetTerrain(c, waterTerrain);
            }
        }

        private static RiverDef? WidestRiver(Tile tile)
        {
            RiverDef? widest = null;
            foreach (RiverLink link in tile.Rivers)
            {
                if (widest == null || link.river.widthOnWorld > widest.widthOnWorld) widest = link.river;
            }
            return widest;
        }
    }
}
