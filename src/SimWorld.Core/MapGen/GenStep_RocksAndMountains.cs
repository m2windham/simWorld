using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Places natural rock outcrops and mountains onto the <see cref="EdificeGrid"/>, one cell of solid rock
    /// per cell whose <see cref="MapGenContext.elevationGrid"/> value ranks in the tile's own
    /// <see cref="MapGenTuning.TargetRockFraction"/> — a per-map quantile threshold, exactly the calibration
    /// technique <c>WorldGenStep_Terrain</c> already uses for land fraction, so the target coverage holds
    /// regardless of the noise's actual distribution. The tile's Stone deposit raises that coverage; its Ore
    /// deposit guarantees a proportional number of mineable ore veins inside the rock, so a tile the world map
    /// promised ore on always produces a map with ore in it (spec §5).
    /// </summary>
    public class GenStep_RocksAndMountains : GenStep
    {
        private static readonly ThingDef[] RockTypes = { MapGenThingDefOf.Sandstone, MapGenThingDefOf.Granite, MapGenThingDefOf.Limestone };

        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            Map.Map map = ctx.map;
            Tile tile = ctx.tile;

            float stoneMagnitude = tile.DepositMagnitude(DepositDefOf.Stone);
            float targetFraction = GenMath.Clamp01(MapGenTuning.TargetRockFraction(tile.hilliness) + stoneMagnitude * MapGenTuning.StoneFractionBonus);
            bool[] rock = ComputeRockMask(ctx.elevationGrid, targetFraction);

            if (targetFraction > 0f)
            {
                ThingDef dominantRock = rand.Element(RockTypes[0], RockTypes[1], RockTypes[2]);
                SpawnRock(map, rock, dominantRock);
                PlaceOreVeins(map, rand, rock, tile.DepositMagnitude(DepositDefOf.Ore));
            }

            ctx.rock = rock;
        }

        /// <summary>True for exactly the top <paramref name="targetFraction"/> of cells by elevation value (0 and 1 handled without touching the noise at all, so a Flat tile never rounds up to a stray rock cell).</summary>
        private static bool[] ComputeRockMask(float[] elevation, float targetFraction)
        {
            int n = elevation.Length;
            var rock = new bool[n];
            if (targetFraction <= 0f) return rock;
            if (targetFraction >= 1f)
            {
                for (int i = 0; i < n; i++) rock[i] = true;
                return rock;
            }

            float threshold = QuantileThreshold(elevation, targetFraction);
            for (int i = 0; i < n; i++)
            {
                rock[i] = elevation[i] >= threshold;
            }
            return rock;
        }

        private static float QuantileThreshold(float[] values, float topFraction)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            int rockCount = (int)Math.Round(topFraction * sorted.Length);
            rockCount = Math.Max(1, Math.Min(sorted.Length, rockCount));
            int rankIndex = sorted.Length - rockCount;
            return sorted[rankIndex];
        }

        private static void SpawnRock(Map.Map map, bool[] rock, ThingDef rockDef)
        {
            int sizeX = map.Size.x, sizeZ = map.Size.z;
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int i = map.cellIndices.CellToIndex(x, z);
                    if (!rock[i]) continue;
                    Thing edifice = ThingMaker.MakeThing(rockDef);
                    GenSpawn.Spawn(edifice, new IntVec3(x, 0, z), map);
                }
            }
        }

        private static void PlaceOreVeins(Map.Map map, RandomStream rand, bool[] rock, float oreMagnitude)
        {
            if (oreMagnitude <= 0f) return;

            List<IntVec3> rockCells = CollectRockCells(map, rock);
            if (rockCells.Count == 0) return;

            int veinCount = Math.Max(1, (int)Math.Round(oreMagnitude * MapGenTuning.OreVeinsPerFullMagnitude));
            veinCount = Math.Min(veinCount, rockCells.Count);

            for (int k = 0; k < veinCount; k++)
            {
                int pick = rand.Range(k, rockCells.Count);
                (rockCells[k], rockCells[pick]) = (rockCells[pick], rockCells[k]);
                IntVec3 cell = rockCells[k];

                Thing? existing = map.edificeGrid[cell];
                existing?.Destroy();

                Thing ore = ThingMaker.MakeThing(MapGenThingDefOf.MineableSteel);
                GenSpawn.Spawn(ore, cell, map);
            }
        }

        private static List<IntVec3> CollectRockCells(Map.Map map, bool[] rock)
        {
            var list = new List<IntVec3>();
            int sizeX = map.Size.x, sizeZ = map.Size.z;
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    if (rock[map.cellIndices.CellToIndex(x, z)]) list.Add(new IntVec3(x, 0, z));
                }
            }
            return list;
        }
    }
}
