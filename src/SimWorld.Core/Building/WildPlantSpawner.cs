using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.MapGen;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>The wild plant <see cref="WildPlantSpawner"/> regrows, bound by defName in its own
    /// <c>[DefOf]</c> class the way every other per-module one here is (<see cref="ConstructionThingDefOf"/>).
    /// The same def <c>MapGen.MapGenThingDefOf.WildPlant</c> names: generation scatters it, this regrows
    /// it.</summary>
    [DefOf]
    public static class WildPlantDefOf
    {
        public static ThingDef WildPlant = null!;
    }

    /// <summary>
    /// Wild plants coming back after something clears them (RimWorld: <c>RimWorld.WildPlantSpawner</c>, a
    /// per-map component that keeps a map's undergrowth near the density its biome supports).
    ///
    /// <para/><b>The defect this closes.</b> <c>MapGen.GenStep_Scatterers</c> scattered undergrowth across a
    /// new map at the density its tile's biome implied, and that was the last word on the subject: cut a
    /// map's plants for wood, burn them, build over them, and the ground stayed bare for the rest of the
    /// game. <see cref="BiomeDef.wildPlantRegrowDays"/> — content authored for all eleven land biomes, from a
    /// tropical rainforest's 8 days to an extreme desert's 80 — was read by no line of the core. So a
    /// rainforest and a desert recovered at exactly the same speed, which is to say neither of them did.
    ///
    /// <para/><b>The model.</b> Two numbers, both taken from content rather than invented here:
    /// <list type="bullet">
    /// <item>How much undergrowth this map should carry — <see cref="MapGenTuning.WildPlantSignal"/> and
    /// <see cref="MapGenTuning.PlantCellsPerItem"/>, the very formula generation used to place it, so a map
    /// regrows toward the density it was born with rather than toward a second, separately-tuned one.</item>
    /// <item>How fast it comes back — <see cref="BiomeDef.wildPlantRegrowDays"/>, read as the days a map
    /// stripped bare needs to fill itself again. That reading is this port's own (RimWorld's exact use of the
    /// field is not sourceable in this sandbox), so it is pinned by tests asserting the trend and the rough
    /// timescale — a rainforest beats a desert, a cleared map is meaningfully back within its own regrow
    /// window — never by a literal count.</item>
    /// </list>
    /// The two combine into a plants-per-tick rate of <c>desired / (regrowDays × 60000)</c>, rolled as a
    /// chance each tick while the map is below its desired count. Linear, not asymptotic: a map missing half
    /// its plants and a map missing all of them regrow at the same rate per tick, which is what makes
    /// "regrow days" mean the plain thing it says.
    ///
    /// <para/><b>Determinism.</b> Every roll goes through <see cref="RandomStream.ChanceSeeded"/> and
    /// <see cref="RandomStream.RangeSeeded"/>, seeded from the map's own id and the current tick, so this
    /// draws nothing from the ambient <see cref="Rand.Current"/> stream and cannot shift any other system's
    /// sequence — the same reason <c>World.EmergenceManager</c> owns a stream of its own. Two runs of the
    /// same game regrow the same plants in the same cells on the same ticks.
    ///
    /// <para/><b>A map with no world tile does nothing here</b>, exactly as <c>Weather.MapClimate</c> treats
    /// the same state: most tests build a bare <see cref="Map.Map"/> that never sat on a planet, and nothing
    /// should start growing on it.
    /// </summary>
    public static class WildPlantSpawner
    {
        /// <summary>Random cells tried per spawn before giving up until the next tick. A miss is cheap and
        /// common (roofed, built on, water, infertile, already planted); an exhaustive search for a cell the
        /// undergrowth does not need is not worth paying for, the same trade
        /// <c>MapGen.GenStep_Scatterers</c> already makes when it scatters.</summary>
        public const int MaxCellTriesPerSpawn = 5;

        /// <summary>Called once per map per tick from <see cref="Map.Map.MapTick"/>.</summary>
        public static void WildPlantSpawnerTick(Map.Map map)
        {
            if (map == null) return;

            Tile? tile = TryResolveTile(map);
            if (tile == null) return;

            BiomeDef? biome = tile.biome;
            if (biome == null || biome.wildPlantRegrowDays <= 0f) return;

            int desired = DesiredWildPlantCount(map, tile);
            if (desired <= 0) return;
            if (map.listerThings.ThingsOfDef(WildPlantDefOf.WildPlant).Count >= desired) return;

            int tick = Find.TickManager.TicksGame;
            float chance = SpawnChancePerTick(desired, biome.wildPlantRegrowDays);
            if (!RandomStream.ChanceSeeded(chance, SeedFor(map, tick, 0))) return;

            TrySpawnOne(map, tick);
        }

        /// <summary>
        /// How many wild plants this map's tile supports, from the same density formula
        /// <c>MapGen.GenStep_Scatterers</c> placed them with.
        /// </summary>
        public static int DesiredWildPlantCount(Map.Map map, Tile tile)
        {
            float signal = MapGenTuning.WildPlantSignal(tile);
            if (signal <= 0f) return 0;
            float cellsPerItem = MapGenTuning.PlantCellsPerItem(signal);
            if (cellsPerItem <= 0f) return 0;
            return (int)(map.cellIndices.NumGridCells / cellsPerItem);
        }

        /// <summary>
        /// Chance of one plant returning this tick: the whole map's worth spread evenly over
        /// <paramref name="regrowDays"/> days, capped at 1 (a cap the shipped biomes never come near — the
        /// densest biome on a default-sized map sits near 0.004).
        /// </summary>
        public static float SpawnChancePerTick(int desiredCount, float regrowDays)
        {
            if (desiredCount <= 0 || regrowDays <= 0f) return 0f;
            float perTick = desiredCount / (regrowDays * GenDate.TicksPerDay);
            return perTick > 1f ? 1f : perTick;
        }

        private static void TrySpawnOne(Map.Map map, int tick)
        {
            for (int attempt = 1; attempt <= MaxCellTriesPerSpawn; attempt++)
            {
                int x = RandomStream.RangeSeeded(0, map.Size.x, SeedFor(map, tick, attempt * 2));
                int z = RandomStream.RangeSeeded(0, map.Size.z, SeedFor(map, tick, attempt * 2 + 1));
                var cell = new IntVec3(x, 0, z);
                if (!CanRegrowAt(map, cell)) continue;

                Thing plant = ThingMaker.MakeThing(WildPlantDefOf.WildPlant);
                if (plant is Plant seedling) seedling.Growth = Plant.SeedlingGrowth;
                GenSpawn.Spawn(plant, cell, map);
                return;
            }
        }

        /// <summary>Open, unroofed, fertile ground with nothing already growing on it — the same conditions
        /// generation scatters plants under, plus the "nothing already growing here" check generation can
        /// skip because it only ever runs on an empty map.</summary>
        private static bool CanRegrowAt(Map.Map map, IntVec3 cell)
        {
            if (map.roofGrid.Roofed(cell)) return false;
            if (map.edificeGrid[cell] != null) return false;
            if (map.thingGrid.CellContains(cell, ThingCategory.Plant)) return false;
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            return !terrain.IsWater && terrain.fertility > 0f;
        }

        /// <summary>The world tile this map sits on, or null when it has none (no world current, no tile
        /// assigned, or a tile outside this world's grid) — resolved live and guarded exactly as
        /// <c>Weather.MapClimate.TryResolve</c> does it, and for the same reason.</summary>
        private static Tile? TryResolveTile(Map.Map map)
        {
            global::SimWorld.World.World? world = Find.World;
            if (world == null || world.grid == null) return null;
            if (map.tile < 0 || map.tile >= world.grid.TilesCount) return null;
            return world.grid.Tiles[map.tile];
        }

        /// <summary>A seed unique to (map, tick, purpose); <see cref="RandomStream"/>'s seeded statics hash it
        /// before use, so a plain mix is enough to keep two maps, two ticks and two cell tries independent.</summary>
        private static int SeedFor(Map.Map map, int tick, int purpose)
        {
            unchecked
            {
                uint hash = (uint)map.uniqueID * 2654435761u;
                hash ^= (uint)tick * 2246822519u;
                hash ^= (uint)purpose * 3266489917u;
                return (int)hash;
            }
        }
    }
}
