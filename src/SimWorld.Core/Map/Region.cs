using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>
    /// A maximal group of walkable cells that flood fill reaches together without crossing a doorway
    /// (RimWorld: <c>Verse.Region</c>) — the node type of the graph <see cref="RegionGrid"/> holds and
    /// <see cref="RegionTraverser"/> searches. Built by <see cref="RegionMaker"/>; torn down and rebuilt
    /// wholesale by <see cref="RegionAndRoomUpdater"/> when a passability change touches it, never patched
    /// cell by cell in place.
    ///
    /// <b>Trim</b>: RimWorld's own Region additionally carries <c>extentsClose</c>/<c>extentsLimit</c>
    /// bounding rectangles (used by its "closest region" search) and a <c>Room</c> back-reference. Nothing
    /// in this pass needs a closest-region search, and this port's Room/RoomGroup concept
    /// (<see cref="Building.RoomTracker"/>) is flood-filled independently off the edifice grid rather than
    /// off this graph — see that class's own remarks for why this pass does not unify them. Both are left
    /// out rather than added unused.
    /// </summary>
    public sealed class Region
    {
        private static int nextId;

        /// <summary>Not required to be stable across a rebuild (RimWorld's aren't either) — purely a debug
        /// label; nothing keys behaviour off a specific value.</summary>
        public readonly int id;

        public RegionType type = RegionType.Normal;

        internal readonly List<IntVec3> cells = new List<IntVec3>();
        internal readonly List<RegionLink> links = new List<RegionLink>();

        /// <summary>Rolling per-search visited-stamp (RimWorld: <c>Region.reachedIndex</c>) so
        /// <see cref="RegionTraverser"/> can BFS without allocating or clearing a fresh visited set every
        /// query — the same trick <see cref="AI.PathFinder"/>'s own <c>generation</c> field already plays
        /// per cell.</summary>
        internal int reachedIndex;

        public Region()
        {
            id = nextId++;
        }

        public IReadOnlyList<IntVec3> Cells => cells;

        public IReadOnlyList<RegionLink> Links => links;

        public override string ToString() => "Region" + id.ToString() + "(" + type + ", " + cells.Count + " cells)";
    }
}
