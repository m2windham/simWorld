using System;

using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// What has changed on one map since the host last looked, at the coarsest granularity that is still
    /// useful — the half of the <c>Map/View</c> seam that lives on the simulation's side of it.
    ///
    /// <para/><b>Why this exists at all.</b> <see cref="MapViewSnapshot"/> could have been the whole seam, the
    /// way <c>God/View</c>'s one <c>Capture()</c> is. It cannot be, and the reason is measured rather than
    /// suspected: a generated <c>TribalStart</c> interior holds ~13,000 mineable rocks and ~800 wild plants on
    /// a 200x200 map, which is 40,000 cells. Rebuilding all of that every frame is dead on arrival, and a host
    /// that did it would be paying the cost of a map that has not changed in a thousand ticks.
    ///
    /// <para/><b>Three rates, three treatments</b>, which is the entire design:
    /// <list type="bullet">
    /// <item><b>Terrain</b> barely changes — a floor built, a rock mined out. One version number for the whole
    /// grid; when it moves, the host re-pulls all 40,000 cells, and that is still rare enough to be free.</item>
    /// <item><b>Things</b> change rarely and locally. The map is cut into square chunks, each with its own
    /// version; a spawn, a despawn or a move bumps the chunk(s) it touched, and the host asks only for chunks
    /// whose version has moved. A settlement building a wall re-sends one chunk, not a map.</item>
    /// <item><b>Pawns</b> move constantly and are few — tens, not thousands. They are never chunked and never
    /// versioned: every read re-captures all of them, because tracking which chunk a walking pawn is in would
    /// cost more than simply re-reading twenty positions.</item>
    /// </list>
    /// That last point is why <see cref="Notify_ThingChangedAt"/> ignores pawns outright. If it did not, every
    /// pawn step would dirty a chunk and drag its ~1,000 rocks back across the seam with it — the exact cost
    /// the chunking exists to avoid.
    ///
    /// <para/><b>What a version does and does not promise.</b> A chunk version moves when a Thing is
    /// registered in, deregistered from, or moved within that chunk — that is, when what is drawn <i>where</i>
    /// changes. It deliberately does not move for a Thing that mutates in place without touching the grids: a
    /// stack growing from 20 to 40 steel, hit points falling, a plant growing. Those are field writes on a
    /// Thing with no call into <see cref="ThingGrid"/> to hook, and adding a notification to each of them
    /// would mean touching a dozen systems to catch a redraw nobody has yet asked for. A host that draws stack
    /// numbers or damage should re-pull on its own schedule, or call <see cref="Notify_ChunkChangedAt"/> from
    /// whatever made the change. This limitation is stated in <c>docs/perf/map-view.md</c> as well, because it
    /// is the one thing about this model that will surprise someone.
    ///
    /// <para/><b>Nothing here is saved.</b> Versions are transient by construction: a loaded map gets a fresh
    /// tracker with a fresh <see cref="SessionId"/>, the host sees an id it does not recognise, and re-pulls
    /// everything. That is the same discipline <c>God/View</c> follows — hold no state, re-derive — and it is
    /// why there is no Scribe round-trip for this type to have.
    /// </summary>
    public sealed class MapViewTracker
    {
        /// <summary>
        /// Chunk edge in cells. 32 gives a 200x200 map 7x7 = 49 chunks of at most 1,024 cells, which is the
        /// band where both failure modes are away from you: much smaller and the per-chunk bookkeeping and the
        /// delta's own list dominate; much larger and one wall built in a corner re-sends a quarter of the map.
        /// Not a RimWorld number — RimWorld has no equivalent, because its renderer and its simulation are one
        /// assembly and it draws from live grids. Chosen here, measured in <c>docs/perf/map-view.md</c>, and
        /// deliberately a constant rather than a tunable until something needs it to be one.
        /// </summary>
        public const int ChunkSize = 32;

        private static long nextSessionId = 1;

        private readonly int chunkSize;
        private readonly int[] chunkVersions;

        private int terrainVersion = 1;
        private int roofVersion = 1;

        internal MapViewTracker(int mapSizeX, int mapSizeZ, int chunkSize = ChunkSize)
        {
            if (mapSizeX <= 0) throw new ArgumentOutOfRangeException(nameof(mapSizeX));
            if (mapSizeZ <= 0) throw new ArgumentOutOfRangeException(nameof(mapSizeZ));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            this.chunkSize = chunkSize;
            MapSizeX = mapSizeX;
            MapSizeZ = mapSizeZ;
            ChunksX = (mapSizeX + chunkSize - 1) / chunkSize;
            ChunksZ = (mapSizeZ + chunkSize - 1) / chunkSize;
            chunkVersions = new int[ChunksX * ChunksZ];
            for (int i = 0; i < chunkVersions.Length; i++) chunkVersions[i] = 1;
            SessionId = AllocateSessionId();
        }

        /// <summary>
        /// Identity of this tracker, and the host's answer to "is what I hold still about this map?".
        ///
        /// <para/>A host compares the id on a <see cref="MapViewDelta"/> against the one it holds; different
        /// means everything it cached belongs to a map that no longer exists — a different settlement, or the
        /// same settlement rebuilt by a load — and it must take the full resync the delta already carries.
        /// Without it, a reloaded map whose versions happened to restart at the numbers the host was holding
        /// would read as "nothing changed" and draw the previous game's town.
        ///
        /// <para/>Unlike <c>Map.uniqueID</c> — which seeds the wild-plant spawner and so genuinely steers
        /// outcomes — this number feeds nothing but an equality test. It cannot reach the simulation, so a
        /// process-global counter is safe here in a way it explicitly is not there.
        /// </summary>
        public long SessionId { get; }

        public int MapSizeX { get; }

        public int MapSizeZ { get; }

        public int ChunksX { get; }

        public int ChunksZ { get; }

        public int ChunkCount => chunkVersions.Length;

        /// <summary>Chunk edge in cells for this tracker; <see cref="ChunkSize"/> unless a test chose another.</summary>
        public int ChunkEdge => chunkSize;

        /// <summary>Bumped by every terrain write; when it moves the whole terrain grid is re-sent.</summary>
        public int TerrainVersion => terrainVersion;

        /// <summary>Bumped by every roof write. Separate from the terrain's because the two change for
        /// completely different reasons — a floor laid versus a mountain roof collapsing — and a host that
        /// re-pulled 40,000 terrain cells every time a roof fell would be paying for the wrong event.</summary>
        public int RoofVersion => roofVersion;

        public int VersionOfChunk(int index) => chunkVersions[index];

        /// <summary>Restarts session ids from one, the way <c>Map.ResetMapIdCounter</c> restarts map ids and
        /// for the same reason: a test must not inherit a counter from the test before it. Safe to leave
        /// alone in a host — the counter only has to be monotonic within one process for the equality test
        /// above to mean what it says.</summary>
        public static void ResetSessionIdCounter() => nextSessionId = 1;

        private static long AllocateSessionId() => nextSessionId++;

        public int ChunkIndexAt(IntVec3 c) => ChunkIndexAt(c.x, c.z);

        public int ChunkIndexAt(int x, int z)
        {
            int cx = x / chunkSize;
            int cz = z / chunkSize;
            return cz * ChunksX + cx;
        }

        /// <summary>The cell rect chunk <paramref name="index"/> covers, clipped to the map — the edge chunks
        /// of a map whose size is not a multiple of <see cref="ChunkEdge"/> are short, and a host drawing a
        /// chunk's bounds has to be told that rather than inferring a square.</summary>
        public CellRect RectOfChunk(int index)
        {
            int cx = index % ChunksX;
            int cz = index / ChunksX;
            int minX = cx * chunkSize;
            int minZ = cz * chunkSize;
            int width = Math.Min(chunkSize, MapSizeX - minX);
            int height = Math.Min(chunkSize, MapSizeZ - minZ);
            return new CellRect(minX, minZ, width, height);
        }

        public void Notify_TerrainChanged() => terrainVersion++;

        public void Notify_RoofChanged() => roofVersion++;

        /// <summary>
        /// A Thing entered, left or moved within the cell — called from <see cref="ThingGrid"/>, which is the
        /// one place all three of those go through.
        ///
        /// <para/><b>Pawns are skipped deliberately.</b> See this class's own remarks: a walking pawn would
        /// otherwise dirty a chunk every few ticks and pull its thousand rocks back over the seam with it,
        /// and the pawns are re-read in full on every capture anyway.
        /// </summary>
        public void Notify_ThingChangedAt(Thing thing, IntVec3 cell)
        {
            if (thing?.def != null && thing.def.category == ThingCategory.Pawn) return;
            Notify_ChunkChangedAt(cell);
        }

        /// <summary>
        /// Marks the chunk containing <paramref name="cell"/> as changed, whatever changed there. This is the
        /// escape hatch for the in-place mutation this tracker cannot see on its own (a stack count, a plant's
        /// growth): whoever makes such a change can say so here, and until someone needs that, nothing does.
        /// </summary>
        public void Notify_ChunkChangedAt(IntVec3 cell)
        {
            if (cell.x < 0 || cell.x >= MapSizeX || cell.z < 0 || cell.z >= MapSizeZ) return;
            chunkVersions[ChunkIndexAt(cell.x, cell.z)]++;
        }
    }
}
