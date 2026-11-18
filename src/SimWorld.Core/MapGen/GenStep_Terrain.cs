using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The ground at a cell of fertility <paramref name="fertility"/> (RimWorld: the fertility half of
        /// <c>GenStep_Terrain.TerrainFrom</c>). A boggy tile paints marsh first — this port's stand-in for the
        /// biome <c>terrainPatchMakers</c> RimWorld consults at the same point, which are not ported — and
        /// everything else is the biome's own <see cref="BiomeDef.terrainsByFertility"/> band.
        /// <para/>
        /// <b>Units.</b> RimWorld reads its fertility grid as raw Perlin output, about -1..1, and its biome
        /// thresholds are written in those units. This port's <see cref="MapGenContext.fertilityGrid"/> is the
        /// same kind of noise remapped to 0..1 (<see cref="GenStep_ElevationFertility"/>: raw * 0.5 + 0.5), so
        /// the cell's value is mapped back with <see cref="RawFertility"/> and the XML keeps RimWorld's numbers.
        /// <para/>
        /// <b>Not ported:</b> RimWorld's elevation bands in the same method (gravel just below a mountain's
        /// foot, rock floor under it), because this port raises mountains by a per-tile elevation quantile
        /// (<see cref="GenStep_RocksAndMountains"/>), not RimWorld's fixed 0.61, so there is no fixed band to
        /// sit gravel in. A biome with no band holding the value falls back to Sand, as RimWorld's does after
        /// logging; a tile with no biome at all (only ever a test fixture) is plain Soil.
        /// </summary>
        private static TerrainDef PickBaseTerrain(Tile tile, BiomeDef? biome, float fertility)
        {
            if (tile.swampiness >= MapGenTuning.MarshSwampinessFloor)
            {
                return fertility >= MapGenTuning.MarshFertilityFloor ? MapGenTerrainDefOf.Marsh : MapGenTerrainDefOf.MarshyTerrain;
            }
            if (biome == null) return TerrainDefOf.Soil;
            return TerrainThreshold.TerrainAtValue(biome.terrainsByFertility, RawFertility(fertility)) ?? TerrainDefOf.Sand;
        }

        /// <summary>A <see cref="MapGenContext.fertilityGrid"/> value (0..1) in RimWorld's raw-noise units (-1..1),
        /// the units <see cref="BiomeDef.terrainsByFertility"/> is written in.</summary>
        public static float RawFertility(float fertility) => fertility * 2f - 1f;

        /// <summary>Every ground terrain <see cref="PickBaseTerrain"/> can paint on <paramref name="tile"/>,
        /// whatever the cell's noise: what a map of this tile can be made of before rivers and roads. Kept
        /// beside <see cref="PickBaseTerrain"/> because it is that method's branches, read without a cell: the
        /// biome's bands that overlap the -1..1 a cell can hold.</summary>
        public static TerrainDef[] BaseTerrainsFor(Tile tile)
        {
            if (tile.swampiness >= MapGenTuning.MarshSwampinessFloor)
            {
                return new[] { MapGenTerrainDefOf.Marsh, MapGenTerrainDefOf.MarshyTerrain };
            }
            BiomeDef? biome = tile.biome;
            if (biome == null) return new[] { TerrainDefOf.Soil };

            var terrains = new List<TerrainDef>();
            float lowest = RawFertility(0f), highest = RawFertility(1f);
            if (biome.terrainsByFertility != null)
            {
                foreach (TerrainThreshold band in biome.terrainsByFertility)
                {
                    if (band.max < lowest || band.min > highest || terrains.Contains(band.terrain)) continue;
                    terrains.Add(band.terrain);
                }
            }
            if (terrains.Count == 0) terrains.Add(TerrainDefOf.Sand);
            return terrains.ToArray();
        }

        /// <summary>
        /// Carves the tile's river across the map. Where the world grid is available, the river runs along the
        /// heading of the neighbouring tile it actually flows to — the same real bearing
        /// <see cref="GenStep_Roads"/> uses, through the shared <see cref="MapGenGeometry"/> — so a river that
        /// leaves the tile north-east arrives on the map's north-east side. Without a grid (the tile-only
        /// <see cref="MapGenerator.GenerateMap"/> overload, which most of the map-gen suite calls) there is no
        /// real heading to be had, and the axis falls back to a coin flip, which is what this did in every case
        /// before the grid was threaded through.
        /// </summary>
        private static void CarveRiver(MapGenContext ctx, RandomStream rand)
        {
            Map.Map map = ctx.map;
            RiverDef? widest = WidestRiver(ctx.tile);
            if (widest == null) return;

            int width = Math.Max(1, (int)Math.Round(widest.widthOnWorld * MapGenTuning.RiverWidthCellsPerWorldUnit));
            TerrainDef waterTerrain = widest.widthOnWorld >= MapGenTuning.RiverDeepWidthOnWorldFloor ? TerrainDefOf.WaterDeep : TerrainDefOf.WaterShallow;
            var wiggle = new Perlin(frequency: MapGenTuning.RiverWiggleFrequency, lacunarity: 2.0, persistence: 0.5, octaveCount: 3, seed: rand.Int, quality: NoiseQuality.Standard);

            if (TryRiverCourseFromWorld(ctx, out IntVec3 from, out IntVec3 to))
            {
                CarveRiverCourse(map, waterTerrain, from, to, width, wiggle);
                return;
            }

            bool alongX = rand.Bool;
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

        /// <summary>
        /// The two map-edge points a river enters and leaves by, from the tile's own river links. Two or more
        /// links give both ends a real heading (the river bends through the map as the world says it does);
        /// one link gives one real end and takes the other as its opposite, because a river crosses a tile
        /// rather than stopping in the middle of it, and the far side is the one thing the world data does not
        /// say. False when no link can be located on the grid at all.
        /// </summary>
        private static bool TryRiverCourseFromWorld(MapGenContext ctx, out IntVec3 from, out IntVec3 to)
        {
            from = default;
            to = default;
            WorldGrid? grid = ctx.grid;
            if (grid == null) return false;

            double? firstBearing = null;
            double? secondBearing = null;
            foreach (RiverLink link in ctx.tile.Rivers)
            {
                if (!MapGenGeometry.CanLocate(grid, link.neighbor)) continue;
                double bearing = MapGenGeometry.BearingBetween(grid, ctx.tileId, link.neighbor);
                if (firstBearing == null) firstBearing = bearing;
                else if (secondBearing == null) { secondBearing = bearing; break; }
            }
            if (firstBearing == null) return false;

            from = MapGenGeometry.EdgePointAtBearing(ctx.map, firstBearing.Value);
            to = MapGenGeometry.EdgePointAtBearing(ctx.map, secondBearing ?? firstBearing.Value + Math.PI);
            return true;
        }

        /// <summary>
        /// Paints a wiggling band of water from one edge point to the other. The same Perlin wiggle the
        /// axis-aligned path uses, applied perpendicular to the course rather than to a map axis, so a
        /// diagonal river meanders exactly as much as a straight one does.
        /// </summary>
        private static void CarveRiverCourse(Map.Map map, TerrainDef waterTerrain, IntVec3 from, IntVec3 to, int width, Perlin wiggle)
        {
            double dx = to.x - from.x;
            double dz = to.z - from.z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-6) return;

            double perpX = -dz / length;
            double perpZ = dx / length;
            double amplitude = Math.Max(map.Size.x, map.Size.z) * MapGenTuning.RiverWiggleAmplitude;

            int steps = Math.Max(1, (int)Math.Ceiling(length));
            int half = width / 2;

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double offset = wiggle.GetValue(t, 0.0, 0.0) * amplitude;
                double cxd = from.x + dx * t + perpX * offset;
                double czd = from.z + dz * t + perpZ * offset;

                for (int oz = -half; oz <= half; oz++)
                {
                    for (int ox = -half; ox <= half; ox++)
                    {
                        int x = (int)Math.Round(cxd) + ox;
                        int z = (int)Math.Round(czd) + oz;
                        if (x < 0 || x >= map.Size.x || z < 0 || z >= map.Size.z) continue;
                        map.terrainGrid.SetTerrain(new IntVec3(x, 0, z), waterTerrain);
                    }
                }
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
