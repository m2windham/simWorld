using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// How many trees a map carries, read off its tile. One formula with two readers, the same rule
    /// <see cref="MapGenTuning.WildPlantSignal"/> follows: <see cref="GenStep_Trees"/> places this many and
    /// <c>Building.WildPlantSpawner</c> regrows toward it, so a map felled for its wood recovers toward the
    /// forest it was born with and never toward a second, separately tuned one.
    /// </summary>
    public static class TreeTuning
    {
        /// <summary>
        /// Map cells per tree on a tile whose Timber deposit is at full magnitude; a tile with less timber has
        /// proportionally fewer. This port's own figure: RimWorld's tree density falls out of each biome's
        /// <c>wildPlants</c> commonalities, which this port does not carry. At 20 a temperate forest (Timber
        /// 0.6) grows a tree every 33 cells, about 1,200 on a 200×200 map, a desert (0.075) one every 267, and a
        /// tile with no timber none at all. Pinned by <c>TreeTests</c> as a trend over the Timber deposit, never
        /// by this literal.
        /// </summary>
        public const float CellsPerTreeAtFullTimber = 20f;

        /// <summary>Terrain fertility a tree needs to take root: RimWorld's poplar asks for 70% ("Min
        /// Fertility 70%", the wiki's poplar page). Read against this port's own terrain values, so soil and
        /// marsh grow trees and sand and gravel do not.</summary>
        public const float MinFertility = 0.7f;

        /// <summary>Below this much Timber a tile grows no trees at all, rather than the handful a vanishing
        /// magnitude would still divide out to.</summary>
        public const float MinTimberMagnitude = 0.01f;

        /// <summary>Random cells <see cref="GenStep_Trees"/> may try per tree it wants before giving up on
        /// the rest: generous enough that a map with a fair share of fertile open ground reaches its target,
        /// bounded so a mostly rock or water map does not search forever.</summary>
        public const int MaxAttemptsPerTree = 4;

        public static float TimberMagnitude(Tile tile) => tile == null ? 0f : tile.DepositMagnitude(DepositDefOf.Timber);

        /// <summary>Trees a map of <paramref name="numCells"/> cells on <paramref name="tile"/> should carry.</summary>
        public static int DesiredTreeCount(int numCells, Tile tile)
        {
            float timber = TimberMagnitude(tile);
            if (timber < MinTimberMagnitude || numCells <= 0) return 0;
            return (int)(numCells * timber / CellsPerTreeAtFullTimber);
        }

        /// <summary>Open, unroofed, fertile-enough ground holding nothing at all. A tree does not share its
        /// cell: it is pass-through-only, so an item or a building under one would be unreachable.</summary>
        public static bool CanGrowTreeAt(Map.Map map, IntVec3 c)
        {
            if (map.roofGrid.Roofed(c)) return false;
            if (map.edificeGrid[c] != null) return false;
            if (map.thingGrid.ThingsListAt(c).Count > 0) return false;
            TerrainDef terrain = map.terrainGrid.TerrainAt(c);
            return !terrain.IsWater && terrain.passability != Traversability.Impassable && terrain.fertility >= MinFertility;
        }
    }

    /// <summary>
    /// Trees on a new map (RimWorld: the trees <c>GenStep_Plants</c> places from a biome's wild-plant list).
    /// <para/>
    /// <b>The defect this closes.</b> Every building a settlement decides for itself costs wood, and nothing
    /// on a generated map could produce any: there was no tree. A founding band placed a bed blueprint for each
    /// citizen and waited forever for materials. Seed 777 of the storyteller bench had no wood on its map and
    /// no bed after 45 days; seed 12345 had 52 wood of ruin loot, which built one bed.
    /// <para/>
    /// <b>Density comes from the world map.</b> The tile's Timber deposit (spec §5) is what the world already
    /// promised about this land's wood; this is where that promise becomes something a citizen can fell. See
    /// <see cref="TreeTuning"/>. The deposit is itself read off the biome's plant density, so a forest is
    /// thick with trees and a desert nearly bare.
    /// <para/>
    /// Its own step, seeded by its own def name like every other, so adding it moved no roll any existing step
    /// makes. Ordered after <see cref="GenStep_Scatterers"/> so it never lands a tree on a chunk or a bush, and
    /// before <see cref="GenStep_Ruins"/> and <see cref="GenStep_Roads"/>, which clear whatever stands on the
    /// ground they need.
    /// </summary>
    public class GenStep_Trees : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            Map.Map map = ctx.map;
            int count = TreeTuning.DesiredTreeCount(map.cellIndices.NumGridCells, ctx.tile);
            if (count <= 0) return;

            RandomStream rand = SeededStream(ctx);
            ThingDef tree = Building.TreeDefOf.Plant_TreePoplar;

            // Random cells until the map holds its target, with a bounded number of misses. Unlike
            // GenStep_Scatterers' one draw per item, a miss is retried: WildPlantSpawner regrows toward this
            // same target, so a map generated short of it would spend its first weeks thickening its forest
            // rather than starting at the density its tile supports. A map with too little fertile open
            // ground to reach the target simply stops at the attempt bound.
            int placed = 0;
            int maxAttempts = count * TreeTuning.MaxAttemptsPerTree;
            for (int attempt = 0; attempt < maxAttempts && placed < count; attempt++)
            {
                int x = rand.Range(0, map.Size.x);
                int z = rand.Range(0, map.Size.z);
                if (ctx.rock[map.cellIndices.CellToIndex(x, z)]) continue;

                var c = new IntVec3(x, 0, z);
                if (!TreeTuning.CanGrowTreeAt(map, c)) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(tree), c, map);
                placed++;
            }
        }
    }
}
