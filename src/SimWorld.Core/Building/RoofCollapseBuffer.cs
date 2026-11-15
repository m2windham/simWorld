using System.Collections.Generic;

using SimWorld.Map;

namespace SimWorld.Building
{
    /// <summary>
    /// The roof cells condemned and not yet dropped (RimWorld: <c>Verse.RoofCollapseBuffer</c>, same four
    /// members). A list for the order the cells fall in, because every hit a falling roof deals is a draw
    /// from the seeded stream, and a set beside it so "already condemned?" does not cost a list scan on a
    /// map that is mostly roof.
    ///
    /// <para/><b>Where it lives, and where RimWorld keeps it.</b> RimWorld hangs one of these off the map
    /// (<c>map.roofCollapseBuffer</c>) and has <c>RoofCollapseBufferResolver</c> empty it on the next map
    /// tick. Here <see cref="RoofCollapseCellsFinder"/> holds one for the length of a single collapse event
    /// and empties it before returning — see that class for why the one-tick delay is not ported. What is
    /// ported is the part that matters: finding which cells fall only ever <i>marks</i>, and dropping them is
    /// one loop over this list, so a collapse that brings down another holder adds to the work still to do
    /// instead of starting a second collapse inside the first.
    /// </summary>
    public sealed class RoofCollapseBuffer
    {
        private readonly List<IntVec3> cellsToCollapse = new List<IntVec3>();
        private readonly HashSet<IntVec3> marked = new HashSet<IntVec3>();

        /// <summary>The condemned cells, in the order they were marked. Grows while it is being dropped —
        /// which is why <see cref="RoofCollapserImmediate.DropRoofInCells(List{IntVec3}, Map.Map)"/> walks it
        /// by index.</summary>
        public List<IntVec3> CellsMarkedToCollapse => cellsToCollapse;

        public bool IsMarkedToCollapse(IntVec3 c) => marked.Contains(c);

        /// <summary>Condemns <paramref name="c"/>; false if it already was.</summary>
        public bool MarkToCollapse(IntVec3 c)
        {
            if (!marked.Add(c)) return false;
            cellsToCollapse.Add(c);
            return true;
        }

        public void Clear()
        {
            cellsToCollapse.Clear();
            marked.Clear();
        }
    }
}
