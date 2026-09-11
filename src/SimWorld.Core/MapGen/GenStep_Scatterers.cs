using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Scatters loose rock chunks and wild plant growth over open ground, densities read from the tile's own
    /// promise: chunks scale with the Stone deposit magnitude, plants with the biome's <see cref="BiomeDef.plantDensity"/>
    /// blended with the Timber deposit magnitude (spec §5). Neither is a full RimWorld system yet — chunks are
    /// the existing haulable-resource ThingDefs, and wild plant growth is a single flavor/occupancy marker
    /// rather than a growth-staged Plant (see the report) — but both are guided by what the world map already
    /// said about this tile rather than placed independent of it.
    /// </summary>
    public class GenStep_Scatterers : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            Tile tile = ctx.tile;

            float stoneMagnitude = tile.DepositMagnitude(DepositDefOf.Stone);

            // The plant signal itself lives in MapGenTuning because Building.WildPlantSpawner regrows this
            // map's plants toward the same density afterwards, and the two must not drift apart.
            float plantSignal = MapGenTuning.WildPlantSignal(tile);

            ScatterThings(ctx, rand, MapGenTuning.ChunkCellsPerItem(stoneMagnitude), PickChunkDef(rand), requireFertile: false);
            if (plantSignal > 0f)
            {
                ScatterThings(ctx, rand, MapGenTuning.PlantCellsPerItem(plantSignal), MapGenThingDefOf.WildPlant, requireFertile: true);
            }
        }

        private static ThingDef PickChunkDef(RandomStream rand) =>
            rand.Element(MapGenThingDefOf.ChunkSandstone, MapGenThingDefOf.ChunkGranite, MapGenThingDefOf.ChunkLimestone);

        /// <summary>
        /// Places roughly <c>Area / cellsPerItem</c> instances of <paramref name="def"/> at random cells,
        /// skipping rock, roofed, water, and (for plants) infertile ground. A handful of misses (an already-
        /// occupied cell picked twice) are an acceptable miss rate for flavor scatter, not worth a full-map
        /// scan to avoid.
        /// </summary>
        private static void ScatterThings(MapGenContext ctx, RandomStream rand, float cellsPerItem, ThingDef def, bool requireFertile)
        {
            if (cellsPerItem <= 0f) return;
            Map.Map map = ctx.map;
            int count = (int)(map.cellIndices.NumGridCells / cellsPerItem);

            for (int placed = 0; placed < count; placed++)
            {
                int x = rand.Range(0, map.Size.x);
                int z = rand.Range(0, map.Size.z);
                int i = map.cellIndices.CellToIndex(x, z);
                if (ctx.rock[i]) continue;

                var c = new IntVec3(x, 0, z);
                if (map.roofGrid.Roofed(c)) continue;
                if (map.edificeGrid[c] != null) continue;

                TerrainDef terrain = map.terrainGrid.TerrainAt(c);
                if (terrain.IsWater) continue;
                if (requireFertile && terrain.fertility <= 0f) continue;

                Thing thing = ThingMaker.MakeThing(def);
                GenSpawn.Spawn(thing, c, map);
            }
        }
    }
}
