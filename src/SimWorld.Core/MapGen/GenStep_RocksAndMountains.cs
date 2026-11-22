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

                // Which vein comes out of the ground is content's decision, weighted by each Def's own
                // mineableScatterCommonality (RimWorld: GenStep_ScatterLumpsMineable). Steel stays the
                // fallback for a content set that declares no commonality at all, which is what this step
                // placed unconditionally before the mining ladder existed.
                ThingDef veinDef = MineableUtility.RandomVeinDef(rand) ?? MapGenThingDefOf.MineableSteel;
                Thing ore = ThingMaker.MakeThing(veinDef);
                GenSpawn.Spawn(ore, cell, map);
                GrowLump(map, rand, rock, cell, veinDef);
            }
        }

        /// <summary>
        /// Grows the vein seeded at <paramref name="seed"/> to its Def's
        /// <see cref="ThingDef.mineableScatterLumpSizeRange"/> through adjacent plain rock (RimWorld:
        /// <c>GenStep_ScatterLumpsMineable</c> fills a <c>GridShapeMaker.IrregularLump</c>). Each step takes a
        /// random cell of the lump's rock frontier, so the shape is irregular and seeded. Never overwrites another
        /// vein; stops early where the rock runs out. A one-cell range draws nothing, so content that sets no
        /// range generates exactly the map it did before. (crafting.materialladder)
        /// </summary>
        private static void GrowLump(Map.Map map, RandomStream rand, bool[] rock, IntVec3 seed, ThingDef veinDef)
        {
            IntRange size = veinDef.mineableScatterLumpSizeRange;
            if (size.max <= 1) return;
            int target = rand.Range(size);
            var frontier = new List<IntVec3>();
            int grown = 1;
            AddFrontier(map, rock, seed, frontier);
            while (grown < target && frontier.Count > 0)
            {
                int pick = rand.Range(0, frontier.Count);
                IntVec3 c = frontier[pick];
                frontier[pick] = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);
                Thing? existing = map.edificeGrid[c];
                if (existing == null || existing.def.mineableScatterCommonality > 0f) continue; // taken, or a vein
                existing.Destroy();
                GenSpawn.Spawn(ThingMaker.MakeThing(veinDef), c, map);
                grown++;
                AddFrontier(map, rock, c, frontier);
            }
        }

        private static void AddFrontier(Map.Map map, bool[] rock, IntVec3 c, List<IntVec3> frontier)
        {
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 nb = c + GenAdj.CardinalDirections[i];
                if (!GenGrid.InBounds(nb, map) || !rock[map.cellIndices.CellToIndex(nb.x, nb.z)]) continue;
                Thing? edifice = map.edificeGrid[nb];
                if (edifice != null && edifice.def.mineableScatterCommonality <= 0f && !frontier.Contains(nb)) frontier.Add(nb);
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
