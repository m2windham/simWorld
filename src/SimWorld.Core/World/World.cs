using System.Collections.Generic;
using System.Linq;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.World
{
    /// <summary>
    /// One generated planet (RimWorld: <c>RimWorld.Planet.World</c>). <see cref="grid"/> and every tile's
    /// derived data (biome, elevation, rivers, roads, ...) are pure functions of <see cref="info"/> and are
    /// never saved — <see cref="RegenerateGrid"/> rebuilds them from the seed on load. <see cref="factions"/>
    /// and <see cref="worldObjects"/> (settlements) ARE saved: they can diverge from their generated state
    /// during play (a settlement destroyed, a faction's fortunes changed), so they are not safe to
    /// regenerate. RimWorld instead compresses the tile arrays directly into the save; this port trades a
    /// slightly larger CPU cost on load for a much simpler save format, which is provable by round-trip
    /// (see <c>WorldGenTests.Scribe_round_trip_regenerates_an_identical_grid</c>).
    /// </summary>
    public class World : IExposable
    {
        public WorldInfo info = null!;
        public WorldGrid grid = null!;
        public List<Faction> factions = new List<Faction>();
        public List<WorldObject> worldObjects = new List<WorldObject>();

        /// <summary>
        /// The region partition (spec §5b.1), rebuilt by <c>Gen.WorldGenStep_Regions</c>/<c>Gen.WorldGenStep_Deposits</c>
        /// exactly like <see cref="grid"/>'s own tile data — a pure function of <see cref="info"/>, never saved.
        /// </summary>
        public List<WorldRegion> regions = new List<WorldRegion>();

        /// <summary>
        /// Rival civilizations emerging from play rather than existing at time zero (spec §5b.4/§5b.5). Saved
        /// (its own random stream's position is the whole of its state) so a reloaded save continues the exact
        /// same emergence history rather than restarting it. Ticked from <see cref="WorldTick"/>, the same
        /// "something a caller ticks" seam every other per-world process already uses — this is not a second,
        /// competing game loop.
        /// </summary>
        public EmergenceManager emergence = null!;

        private int nextObjectId = 1;

        /// <summary>For Scribe's deep-load construction.</summary>
        public World()
        {
        }

        public World(WorldInfo info, WorldGrid grid)
        {
            this.info = info;
            this.grid = grid;
            this.emergence = new EmergenceManager(info.seed);
        }

        /// <summary>Every settlement in the world. Always a real <see cref="Settlement"/> — every founding path
        /// (<see cref="SettlementFounder.Found"/>, <see cref="SettlementFounder.FoundColony"/>) constructs one,
        /// never a bare <see cref="WorldObject"/> (spec §5b.5).</summary>
        public IEnumerable<Settlement> Settlements => worldObjects.OfType<Settlement>();

        /// <summary>Call once per game tick: advances every world object (RimWorld: <c>Verse.WorldObjectsHolder.WorldObjectsHolderTick</c>, folded into <c>World</c> here), then <see cref="EmergenceManager.Tick"/>. Caravans use this to step along their path and consume food.</summary>
        public void WorldTick()
        {
            for (int i = 0; i < worldObjects.Count; i++)
            {
                worldObjects[i].Tick(this);
            }
            emergence.Tick(this);
        }

        /// <summary>Hands out a unique, deterministic-per-world id for a newly created <see cref="Faction"/> or <see cref="WorldObject"/>.</summary>
        public string NextLoadId(string prefix) => prefix + "_" + nextObjectId++;

        public void ExposeData()
        {
            WorldInfo? i = info;
            Scribe_Deep.Look(ref i, "info");
            info = i!;

            Scribe_Values.Look(ref nextObjectId, "nextObjectId", 1);

            List<Faction>? f = factions;
            Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
            factions = f ?? new List<Faction>();

            List<WorldObject>? w = worldObjects;
            Scribe_Collections.Look(ref w, "worldObjects", LookMode.Deep);
            worldObjects = w ?? new List<WorldObject>();

            EmergenceManager? e = emergence;
            Scribe_Deep.Look(ref e, "emergence");
            // Older saves (or a freshly-constructed World whose caller skipped the (info, grid) constructor)
            // carry no emergence state at all — fall back to a fresh manager seeded from info exactly as the
            // (info, grid) constructor would, rather than leaving a null that WorldTick would throw on.
            emergence = e ?? new EmergenceManager(info.seed);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RegenerateGrid();
            }
        }

        /// <summary>
        /// Rebuilds <see cref="grid"/> from <see cref="info"/> alone: the icosphere at
        /// <see cref="WorldInfo.subdivisionLevel"/>, then every non-faction <c>WorldGenStepDef</c> (terrain,
        /// biomes, rivers, roads) in order. Never re-runs faction/settlement placement — those came from
        /// the save's <see cref="worldObjects"/>/<see cref="factions"/> instead.
        /// </summary>
        public void RegenerateGrid()
        {
            grid = WorldGrid.Generate(info.subdivisionLevel);
            Gen.WorldGenerator.RegenerateTerrain(info, this);
        }
    }
}
