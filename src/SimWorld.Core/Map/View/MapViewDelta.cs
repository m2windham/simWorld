using System;
using System.Collections.Generic;

using SimWorld.Sim;

namespace SimWorld.Map.View
{
    /// <summary>
    /// What changed on one settlement interior since the host last looked — the call a host makes every frame,
    /// where <see cref="MapViewSnapshot.Capture(int)"/> is the call it makes once.
    ///
    /// <para/><b>How a host uses this, in full.</b> Open a settlement through <c>GodCommands.OpenSettlement</c>,
    /// then:
    /// <code>
    /// // once
    /// MapViewSnapshot snapshot = MapViewSnapshot.Capture(tile);
    /// if (!snapshot.HasMap) { ShowMessage(snapshot.AbsenceReason); return; }
    /// BuildScene(snapshot);
    /// MapViewVersions held = snapshot.Versions;
    ///
    /// // every frame
    /// MapViewDelta delta = MapViewSnapshot.CaptureChanges(tile, held);
    /// if (!delta.HasMap) { DropScene(); return; }
    /// if (delta.FullResync) DropScene();
    /// if (delta.Terrain != null) RebuildTerrain(delta.Terrain);
    /// if (delta.Roofs != null) RebuildRoofs(delta.Roofs);
    /// foreach (MapViewChunk chunk in delta.ChangedChunks) RebuildChunk(chunk);
    /// DrawPawns(delta.Pawns);
    /// held = delta.Versions;
    /// </code>
    /// The steady state of that loop is: no terrain, no roofs, no changed chunks, and one short list of
    /// pawns — which is the whole reason this type exists. See <c>docs/perf/map-view.md</c> for what each
    /// branch costs.
    ///
    /// <para/><b>Holding a stale or foreign <see cref="MapViewVersions"/> is safe.</b> One from another map,
    /// another session or another map size comes back as <see cref="FullResync"/> — every layer and every
    /// chunk present, exactly as if this were the first read. The failure mode of this design is a wasted
    /// frame, never a wrong picture.
    ///
    /// <para/><b>Pawns are always complete.</b> They are never versioned and never partial: <see cref="Pawns"/>
    /// is every spawned pawn on the map at <see cref="TicksGame"/>, and a pawn missing from it has left the
    /// map or died. A host should treat the list as the truth and retire anything it drew that is not in it.
    /// </summary>
    public sealed class MapViewDelta
    {
        internal MapViewDelta(
            bool hasMap,
            string? absenceReason,
            bool fullResync,
            long sessionId,
            int ticksGame,
            int sizeX,
            int sizeZ,
            int chunkEdge,
            int chunksX,
            int chunksZ,
            TerrainLayer? terrain,
            RoofLayer? roofs,
            IReadOnlyList<MapViewChunk> changedChunks,
            IReadOnlyList<PawnView> pawns,
            MapViewVersions versions)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            FullResync = fullResync;
            SessionId = sessionId;
            TicksGame = ticksGame;
            SizeX = sizeX;
            SizeZ = sizeZ;
            ChunkEdge = chunkEdge;
            ChunksX = chunksX;
            ChunksZ = chunksZ;
            Terrain = terrain;
            Roofs = roofs;
            ChangedChunks = changedChunks;
            Pawns = pawns;
            Versions = versions;
        }

        /// <summary>False when there is no longer an interior to draw — the settlement was destroyed, or the
        /// game ended. <see cref="AbsenceReason"/> says which.</summary>
        public bool HasMap { get; }

        /// <summary>Always populated when <see cref="HasMap"/> is false; null otherwise.</summary>
        public string? AbsenceReason { get; }

        /// <summary>The host must drop everything it had cached: this delta carries the whole map, not a
        /// difference from it. True on the first read, after a load, and whenever the versions handed back
        /// belong to some other map.</summary>
        public bool FullResync { get; }

        public long SessionId { get; }

        public int TicksGame { get; }

        public int SizeX { get; }

        public int SizeZ { get; }

        public int ChunkEdge { get; }

        public int ChunksX { get; }

        public int ChunksZ { get; }

        /// <summary>The whole terrain grid, or null when it has not changed since the held versions. Terrain
        /// is re-sent whole rather than per cell because it moves so rarely that a per-cell diff would cost
        /// more to maintain than the occasional 40,000-cell re-read costs to take.</summary>
        public TerrainLayer? Terrain { get; }

        /// <summary>The whole roof grid, or null when unchanged. Separate from the terrain because a roof
        /// collapse and a floor being laid are different events at very different rates.</summary>
        public RoofLayer? Roofs { get; }

        /// <summary>Only the chunks whose contents moved, each complete in itself — a host rebuilds a changed
        /// chunk rather than patching it, which is both simpler and what an instanced renderer wants anyway.
        /// Empty in the steady state.</summary>
        public IReadOnlyList<MapViewChunk> ChangedChunks { get; }

        /// <summary>Every spawned pawn, always, complete. See this class's own remarks.</summary>
        public IReadOnlyList<PawnView> Pawns { get; }

        /// <summary>Hand this back on the next call.</summary>
        public MapViewVersions Versions { get; }

        internal static MapViewDelta Absent(string reason) =>
            new MapViewDelta(
                hasMap: false,
                reason,
                fullResync: true,
                sessionId: 0L,
                Find.TickManager.TicksGame,
                sizeX: 0,
                sizeZ: 0,
                chunkEdge: MapViewTracker.ChunkSize,
                chunksX: 0,
                chunksZ: 0,
                terrain: null,
                roofs: null,
                Array.Empty<MapViewChunk>(),
                Array.Empty<PawnView>(),
                new MapViewVersions(0L, 0, 0, Array.Empty<int>()));
    }
}
