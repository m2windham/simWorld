using System;
using System.Collections.Generic;
using SimWorld.Map;

namespace SimWorld.AI
{
    /// <summary>
    /// The cell sequence a <see cref="PathFinder"/> search produced, start (exclusive) to destination
    /// inclusive (RimWorld: <c>Verse.AI.PawnPath</c>). Pooled — <see cref="Get"/>/<see cref="ReleaseToPool"/> —
    /// so a pawn requesting a new path every so often never allocates a fresh node list per call.
    /// </summary>
    public sealed class PawnPath
    {
        private static readonly Stack<PawnPath> pool = new Stack<PawnPath>();

        private readonly List<IntVec3> nodes = new List<IntVec3>();
        private int curIndex;
        private bool found;

        private PawnPath()
        {
        }

        /// <summary>Shared sentinel for "no path exists"; never pooled.</summary>
        public static readonly PawnPath NotFound = new PawnPath { found = false };

        public bool Found => found;

        public bool Finished => curIndex >= nodes.Count;

        public int NodesLeftCount => nodes.Count - curIndex;

        internal static PawnPath Get()
        {
            PawnPath path = pool.Count > 0 ? pool.Pop() : new PawnPath();
            path.nodes.Clear();
            path.curIndex = 0;
            path.found = true;
            return path;
        }

        internal void AddNode(IntVec3 cell) => nodes.Add(cell);

        /// <summary>The cell <paramref name="nodesAhead"/> steps from the next unconsumed one; clamped to the last node.</summary>
        public IntVec3 Peek(int nodesAhead)
        {
            if (nodes.Count == 0) return IntVec3.Invalid;
            int i = Math.Min(curIndex + nodesAhead, nodes.Count - 1);
            return nodes[Math.Max(i, 0)];
        }

        public IntVec3 ConsumeNextNode()
        {
            IntVec3 result = nodes[curIndex];
            curIndex++;
            return result;
        }

        public void ReleaseToPool()
        {
            if (ReferenceEquals(this, NotFound)) return;
            pool.Push(this);
        }
    }
}
