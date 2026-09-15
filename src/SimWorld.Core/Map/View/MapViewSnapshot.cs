using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// Everything a host needs to draw one settlement interior, as one plain snapshot
    /// (<c>docs/spec/simworld-spec.md</c> §12a). This is the seam spec §12a deliberately left open — the god
    /// view reports only whether a settlement's interior exists, and until this existed the host could not ask
    /// what was standing on it, where, or who was walking around.
    ///
    /// <para/><b>It is <c>God/View</c>'s discipline one scale down</b>, and the arguments there carry
    /// unchanged:
    /// <list type="bullet">
    /// <item>A live reference is a write surface. Anything holding a <see cref="ThingDef"/> reaches its
    /// workers and can drive the simulation from the render thread, and nothing in the type system says it
    /// may not.</item>
    /// <item>A live reference tears. The host renders across frames while the sim ticks; a cell list read
    /// halfway through a tick shows a map that never existed at any single instant. On a 40,000-cell map read
    /// every frame, that is not a rare race — it is the normal case.</item>
    /// <item>A live reference is not a contract. Every internal rename becomes a host break, which is most of
    /// what being engine-free was for.</item>
    /// </list>
    /// So every handle here is a <c>defName</c> string and every position is the core's own
    /// <see cref="IntVec3"/>. There is no colour, no mesh, no material and no engine type anywhere in this
    /// namespace: the <c>defName -&gt; mesh</c> registry is the host's, which is the only place it can live
    /// while the core stays engine-free. <c>MapViewTests</c> asserts all of that structurally rather than
    /// trusting it.
    ///
    /// <para/><b>Where this differs from <c>God/View</c>, and why.</b> A civilization rollup is cheap and
    /// slow-moving, so it is captured whole every time the view opens. A map is neither: a generated
    /// <c>TribalStart</c> interior holds ~13,000 mineable rocks and ~800 wild plants across 40,000 cells, and
    /// a host that re-captured that every frame would be dead on arrival. So this class has a second entry
    /// point — <see cref="CaptureChanges(int, MapViewVersions)"/> — and the host uses <see cref="Capture(int)"/>
    /// once, then that, for ever. See <see cref="MapViewTracker"/> for the three change rates that shape it,
    /// and <c>docs/perf/map-view.md</c> for what each costs in milliseconds.
    ///
    /// <para/><b>What this deliberately does not carry.</b> Per-citizen detail beyond what drawing needs.
    /// <see cref="PawnView"/> has a position, a pose and what a person is wearing; it has no mood, no skills,
    /// no health breakdown and no relations. Opening every person to paint a settlement is exactly what spec
    /// §11.3's tiering exists to prevent, and a named citizen is a different, narrower query — the same
    /// refusal <c>GodViewSnapshot</c> makes one scale up.
    ///
    /// <para/><b>Absent things report themselves rather than throwing.</b> No content loaded, no game, no
    /// settlement on that tile, no interior generated yet — each is a state a host has to draw, and each comes
    /// back as a snapshot saying so. A null return would push every host into the same four null checks, and
    /// an exception would make "the player clicked a town with no interior" a crash.
    /// </summary>
    public sealed class MapViewSnapshot
    {
        private MapViewSnapshot(
            bool contentLoaded,
            bool hasMap,
            string? absenceReason,
            int tile,
            string? settlementName,
            long sessionId,
            int ticksGame,
            int sizeX,
            int sizeZ,
            int chunkEdge,
            int chunksX,
            int chunksZ,
            TerrainLayer terrain,
            RoofLayer roofs,
            IReadOnlyList<MapViewChunk> chunks,
            IReadOnlyList<PawnView> pawns,
            MapViewVersions versions)
        {
            ContentLoaded = contentLoaded;
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            Tile = tile;
            SettlementName = settlementName;
            SessionId = sessionId;
            TicksGame = ticksGame;
            SizeX = sizeX;
            SizeZ = sizeZ;
            ChunkEdge = chunkEdge;
            ChunksX = chunksX;
            ChunksZ = chunksZ;
            Terrain = terrain;
            Roofs = roofs;
            Chunks = chunks;
            Pawns = pawns;
            Versions = versions;
        }

        /// <summary>
        /// Whether any content has been loaded into <see cref="DefDatabase.Global"/> at all — the same trap
        /// <c>GodViewSnapshot.ContentLoaded</c> exists for, and it bites a map host harder. A host that never
        /// loaded the core's defs gets a perfectly valid-looking empty snapshot, which is indistinguishable
        /// from a settlement whose interior has not been generated. The fix is one call before anything else:
        /// <c>CoreContent.Load</c> into a <see cref="DefDatabase"/>, and that database assigned to
        /// <see cref="DefDatabase.Global"/>.
        /// </summary>
        public bool ContentLoaded { get; }

        /// <summary>False when there is no interior to draw. <see cref="AbsenceReason"/> says which of the
        /// several reasons applies, in words a host can put on screen.</summary>
        public bool HasMap { get; }

        /// <summary>Why there is nothing to draw, or null when there is. Always populated when
        /// <see cref="HasMap"/> is false, so a host never has to compose the explanation itself — the same
        /// treatment <c>GodCommandResult.Reason</c> gives a refusal.</summary>
        public string? AbsenceReason { get; }

        /// <summary>The world tile asked for, or -1 when the map was captured directly.</summary>
        public int Tile { get; }

        public string? SettlementName { get; }

        /// <summary>Identity of the view session this belongs to; see <see cref="MapViewTracker.SessionId"/>.
        /// A host stores it alongside its cached geometry and drops that cache when it changes.</summary>
        public long SessionId { get; }

        /// <summary>The tick this was taken at. Every position below is that tick's, not a mixture.</summary>
        public int TicksGame { get; }

        public int SizeX { get; }

        public int SizeZ { get; }

        /// <summary>Chunk edge in cells, so a host can size its own bucketing to match.</summary>
        public int ChunkEdge { get; }

        public int ChunksX { get; }

        public int ChunksZ { get; }

        public TerrainLayer Terrain { get; }

        public RoofLayer Roofs { get; }

        /// <summary>Every chunk, in index order, each with the non-pawn Things standing in it.</summary>
        public IReadOnlyList<MapViewChunk> Chunks { get; }

        /// <summary>Every spawned pawn. Never chunked — see <see cref="MapViewTracker"/>.</summary>
        public IReadOnlyList<PawnView> Pawns { get; }

        /// <summary>What to hand back to <see cref="CaptureChanges(int, MapViewVersions)"/> next time.</summary>
        public MapViewVersions Versions { get; }

        /// <summary>Every non-pawn Thing on the map, flattened across <see cref="Chunks"/>. A method rather
        /// than a property because it is a walk over what the snapshot already holds, not a second copy of
        /// it.</summary>
        public IEnumerable<ThingView> AllThings()
        {
            for (int i = 0; i < Chunks.Count; i++)
            {
                IReadOnlyList<ThingView> things = Chunks[i].Things;
                for (int j = 0; j < things.Count; j++) yield return things[j];
            }
        }

        // ---- capture ----

        /// <summary>
        /// The interior of whichever settlement the god currently has open, whole.
        ///
        /// <para/><b>Prefer this to <see cref="Capture(int)"/>.</b> The host is told never to carry its own
        /// idea of which settlement is selected — <c>GodCommands.FocusSettlement</c> writes it and
        /// <c>GodViewSnapshot.FocusedSettlementTile</c> reads it back, and two disagreeing notions of what the
        /// player is looking at is a bug that takes a week to find. A host that passes a tile it remembered is
        /// keeping a second notion; this overload asks the simulation instead, so the map drawn is the map
        /// attended, always. It also handles the case that makes the remembered tile wrong in the first place:
        /// the focused settlement being destroyed, after which attention falls back to civilization scope on
        /// its own (<c>AttentionManager.Reconcile</c>) and this reports that rather than a stale town.
        /// </summary>
        public static MapViewSnapshot Capture()
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return Absent(-1, null, reason!);
            return Capture(tile.Value);
        }

        /// <summary>Changes to the interior of whichever settlement the god has open — the per-frame call, on
        /// the focus rather than on a tile the host remembered. See <see cref="Capture()"/>.</summary>
        public static MapViewDelta CaptureChanges(MapViewVersions? held)
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return MapViewDelta.Absent(reason!);
            return CaptureChanges(tile.Value, held);
        }

        private static int? FocusedTile(out string? reason)
        {
            if (Find.CurrentGame == null)
            {
                reason = "No game is running, so no settlement is open.";
                return null;
            }

            World.Settlement? focused = Find.God.Attention.FocusedSettlement;
            if (focused == null)
            {
                reason = "No settlement is open — the god is at civilization scope.";
                return null;
            }

            reason = null;
            return focused.tile;
        }

        /// <summary>
        /// The whole interior of the settlement on <paramref name="tile"/>, from scratch.
        ///
        /// <para/><b>A settlement is named by its world tile</b> — the same handle <c>GodCommands</c> takes,
        /// and for the same reason: a value the host can hold across a reload and a repaint which cannot be
        /// used to reach the settlement object. <c>SettlementSummary.Tile</c> on a
        /// <c>GodViewSnapshot</c> is where a host gets one, and <c>GodCommands.OpenSettlement(tile)</c> is
        /// what generates the interior this then reads.
        ///
        /// <para/>Call this once per settlement the host opens. After that, call
        /// <see cref="CaptureChanges(int, MapViewVersions)"/>.
        /// </summary>
        public static MapViewSnapshot Capture(int tile)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out string? name);
            if (map == null) return Absent(tile, name, reason!);
            return Capture(map, tile, name);
        }

        /// <summary>Captures a map directly. The host cannot reach one — it holds no <c>Map</c> and the seam
        /// hands none out — so this is for the core's own tests, tools and benchmarks, and for any future
        /// caller inside the core that already has the map in hand.</summary>
        public static MapViewSnapshot Capture(SimWorld.Map.Map map) => Capture(map, -1, null);

        private static MapViewSnapshot Capture(SimWorld.Map.Map map, int tile, string? settlementName)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            MapViewTracker tracker = map.mapView;
            var chunks = new List<MapViewChunk>(tracker.ChunkCount);
            var chunkVersions = new int[tracker.ChunkCount];
            for (int i = 0; i < tracker.ChunkCount; i++)
            {
                chunks.Add(BuildChunk(map, tracker, i));
                chunkVersions[i] = tracker.VersionOfChunk(i);
            }

            return new MapViewSnapshot(
                DefDatabase.Global.DefCount > 0,
                hasMap: true,
                absenceReason: null,
                tile,
                settlementName,
                tracker.SessionId,
                Find.TickManager.TicksGame,
                map.Size.x,
                map.Size.z,
                tracker.ChunkEdge,
                tracker.ChunksX,
                tracker.ChunksZ,
                BuildTerrain(map, tracker),
                BuildRoofs(map, tracker),
                chunks,
                BuildPawns(map),
                new MapViewVersions(tracker.SessionId, tracker.TerrainVersion, tracker.RoofVersion, chunkVersions));
        }

        /// <summary>
        /// Only what has changed since the host last read this settlement — the call a host makes every frame.
        /// Hand back the <see cref="Versions"/> from the previous read; pass null the first time, or after a
        /// load, and the delta comes back as a full resync.
        /// </summary>
        public static MapViewDelta CaptureChanges(int tile, MapViewVersions? held)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out _);
            if (map == null) return MapViewDelta.Absent(reason!);
            return CaptureChanges(map, held);
        }

        /// <summary>Changes on a map held directly; see <see cref="Capture(SimWorld.Map.Map)"/> for who calls
        /// this shape.</summary>
        public static MapViewDelta CaptureChanges(SimWorld.Map.Map map, MapViewVersions? held)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            MapViewTracker tracker = map.mapView;

            // A resync on anything that makes the held versions untrustworthy rather than merely stale: a
            // different session (a reload, or a different settlement), or a chunk count that does not match
            // (a map of another size). Both are cheap to test and the alternative — trusting an array from
            // somewhere else — draws the previous game's town with no error anywhere.
            bool resync = held == null
                || held.SessionId != tracker.SessionId
                || held.Chunks.Count != tracker.ChunkCount;

            var changed = new List<MapViewChunk>();
            var chunkVersions = new int[tracker.ChunkCount];
            for (int i = 0; i < tracker.ChunkCount; i++)
            {
                int version = tracker.VersionOfChunk(i);
                chunkVersions[i] = version;
                if (resync || held!.Chunks[i] != version) changed.Add(BuildChunk(map, tracker, i));
            }

            bool terrainChanged = resync || held!.Terrain != tracker.TerrainVersion;
            bool roofsChanged = resync || held!.Roofs != tracker.RoofVersion;

            return new MapViewDelta(
                hasMap: true,
                absenceReason: null,
                resync,
                tracker.SessionId,
                Find.TickManager.TicksGame,
                map.Size.x,
                map.Size.z,
                tracker.ChunkEdge,
                tracker.ChunksX,
                tracker.ChunksZ,
                terrainChanged ? BuildTerrain(map, tracker) : null,
                roofsChanged ? BuildRoofs(map, tracker) : null,
                changed,
                BuildPawns(map),
                new MapViewVersions(tracker.SessionId, tracker.TerrainVersion, tracker.RoofVersion, chunkVersions));
        }

        // ---- layers ----

        private static TerrainLayer BuildTerrain(SimWorld.Map.Map map, MapViewTracker tracker)
        {
            int n = map.cellIndices.NumGridCells;
            var cells = new int[n];
            var palette = new List<string>();
            var index = new Dictionary<TerrainDef, int>();

            for (int i = 0; i < n; i++)
            {
                TerrainDef def = map.terrainGrid.TerrainAt(map.cellIndices.IndexToCell(i));
                if (!index.TryGetValue(def, out int slot))
                {
                    slot = palette.Count;
                    palette.Add(def.defName);
                    index[def] = slot;
                }
                cells[i] = slot;
            }

            return new TerrainLayer(tracker.TerrainVersion, map.Size.x, map.Size.z, palette, cells);
        }

        private static RoofLayer BuildRoofs(SimWorld.Map.Map map, MapViewTracker tracker)
        {
            int n = map.cellIndices.NumGridCells;
            var cells = new int[n];
            var palette = new List<string>();
            var index = new Dictionary<RoofDef, int>();

            for (int i = 0; i < n; i++)
            {
                RoofDef? def = map.roofGrid.RoofAt(map.cellIndices.IndexToCell(i));
                if (def == null)
                {
                    cells[i] = RoofLayer.Unroofed;
                    continue;
                }
                if (!index.TryGetValue(def, out int slot))
                {
                    slot = palette.Count;
                    palette.Add(def.defName);
                    index[def] = slot;
                }
                cells[i] = slot;
            }

            return new RoofLayer(tracker.RoofVersion, map.Size.x, map.Size.z, palette, cells);
        }

        /// <summary>
        /// Every non-pawn Thing whose own position falls inside one chunk.
        ///
        /// <para/>Read off <see cref="ThingGrid"/> cell by cell rather than by filtering
        /// <c>ListerThings.AllThings</c>, which matters for the reason the chunking exists: a delta touching
        /// two chunks reads ~2,000 cells, where the lister walk would read all ~13,800 Things to find the
        /// handful that moved. The full capture pays the same cost per cell across the whole map and that is
        /// the number <c>docs/perf/map-view.md</c> reports.
        ///
        /// <para/>A multi-cell Thing is registered in the grid under <i>every</i> cell it occupies, so it is
        /// emitted only at the cell that is its own <c>Position</c> — one chunk owns it, nothing is drawn
        /// twice, and no dedupe set is needed.
        /// </summary>
        private static MapViewChunk BuildChunk(SimWorld.Map.Map map, MapViewTracker tracker, int index)
        {
            CellRect rect = tracker.RectOfChunk(index);
            var things = new List<ThingView>();

            for (int z = rect.minZ; z <= rect.maxZ; z++)
            {
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    var cell = new IntVec3(x, 0, z);
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < here.Count; i++)
                    {
                        Thing t = here[i];
                        if (t.def.category == ThingCategory.Pawn) continue;
                        if (t.Position != cell) continue;
                        things.Add(ViewOf(t));
                    }
                }
            }

            return new MapViewChunk(index, rect, tracker.VersionOfChunk(index), things);
        }

        private static ThingView ViewOf(Thing t)
        {
            CellRect occupied = t.OccupiedRect();
            int maxHp = t.MaxHitPoints;
            float health = !t.def.useHitPoints || maxHp <= 0
                ? 1f
                : Clamp01(t.HitPoints / (float)maxHp);
            float growth = t is Building.Plant plant ? plant.Growth : -1f;

            return new ThingView(
                t.thingIDNumber,
                t.def.defName,
                t.Stuff?.defName,
                t.Position,
                t.Rotation,
                new IntVec3(occupied.minX, 0, occupied.minZ),
                new IntVec2(occupied.width, occupied.height),
                t.stackCount,
                t.def.category,
                t.def.altitudeLayer,
                health,
                growth);
        }

        private static List<PawnView> BuildPawns(SimWorld.Map.Map map)
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            var views = new List<PawnView>(spawned.Count);
            for (int i = 0; i < spawned.Count; i++) views.Add(ViewOf(spawned[i]));
            return views;
        }

        private static PawnView ViewOf(Pawn p)
        {
            IReadOnlyList<ThingWithComps> worn = p.apparel?.WornApparel ?? Array.Empty<ThingWithComps>();
            var apparel = new List<string>(worn.Count);
            for (int i = 0; i < worn.Count; i++) apparel.Add(worn[i].def.defName);

            bool moving = p.pather != null && p.pather.Moving;

            return new PawnView(
                p.thingIDNumber,
                p.def.defName,
                p.kindDef?.defName,
                p.Label,
                p.Position,
                p.Rotation,
                p.gender,
                p.BodySize,
                p.ageTracker?.CurLifeStage?.defName,
                p.faction?.def.defName,
                p.Downed,
                p.Dead,
                p.Asleep,
                moving,
                moving ? p.pather!.Destination.Cell : IntVec3.Invalid,
                p.MentalStateDef?.defName,
                p.health != null ? Clamp01(p.health.summaryHealth.SummaryHealthPercent) : 1f,
                apparel,
                p.equipment?.Primary?.def.defName);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ---- resolution ----

        /// <summary>
        /// The interior map of the settlement on <paramref name="tile"/>, or null with a sentence saying which
        /// of the reasons applies. The reasons are separate rather than one "nothing to draw" because they
        /// call for different things on screen: an ungenerated interior wants an "open this settlement"
        /// affordance, a stale tile wants the selection cleared, and no game at all wants a main menu.
        /// </summary>
        private static SimWorld.Map.Map? InteriorOf(int tile, out string? reason, out string? name)
        {
            name = null;

            if (Find.CurrentGame == null)
            {
                reason = "No game is running, so no settlement has an interior.";
                return null;
            }

            World.Settlement? settlement = God.AttentionManager.SettlementAt(tile);
            if (settlement == null)
            {
                reason = "No settlement on tile " + tile.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                return null;
            }

            name = settlement.name;
            SimWorld.Map.Map? map = settlement.InteriorMap;
            if (map == null)
            {
                reason = settlement.name + " has no interior yet — open it first.";
                return null;
            }

            reason = null;
            return map;
        }

        private static MapViewSnapshot Absent(int tile, string? name, string reason)
        {
            var noCells = Array.Empty<int>();
            var noPalette = Array.Empty<string>();
            return new MapViewSnapshot(
                DefDatabase.Global.DefCount > 0,
                hasMap: false,
                reason,
                tile,
                name,
                sessionId: 0L,
                Find.TickManager.TicksGame,
                sizeX: 0,
                sizeZ: 0,
                chunkEdge: MapViewTracker.ChunkSize,
                chunksX: 0,
                chunksZ: 0,
                new TerrainLayer(0, 0, 0, noPalette, noCells),
                new RoofLayer(0, 0, 0, noPalette, noCells),
                Array.Empty<MapViewChunk>(),
                Array.Empty<PawnView>(),
                new MapViewVersions(0L, 0, 0, noCells));
        }
    }
}
