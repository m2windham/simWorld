using System;

namespace SimWorld.Map
{
    /// <summary>
    /// Cell-grid line of sight (RimWorld: <c>Verse.GenSight</c>). <see cref="LineOfSight"/> walks a
    /// "supercover" Bresenham line — every cell the line actually passes through, never skipping a corner
    /// diagonally — from <paramref name="start"/> to <paramref name="end"/>, ported from
    /// <c>Verse.GenSight.LineOfSight</c> (Chillu1/RimWorldDecompiled, <c>Verse/GenSight.cs</c>) with its
    /// half-cell leaning offsets and <c>CellRect</c> overload dropped: this port has no partial-cell leaning
    /// concept, so every call is single-cell start/end.
    ///
    /// This is deliberately <b>not</b> a region-graph query. <see cref="RegionGrid"/>/<see cref="Region"/>
    /// answer reachability (can a pawn walk from A to B, ever crossing a doorway) — a fundamentally
    /// different question from "is there an unbroken sightline between these two cells right now", which
    /// depends on exactly which cells the line grazes, not on which side of a doorway anything sits. Forcing
    /// visibility onto the reachability graph would answer the wrong question, so this is a fresh Bresenham
    /// walk over <see cref="GenGrid"/>/<see cref="EdificeGrid"/> instead.
    /// </summary>
    public static class GenSight
    {
        /// <summary>
        /// True if nothing blocks sight from <paramref name="start"/> to <paramref name="end"/>. The
        /// destination cell itself is never checked (RimWorld's own convention: what stands ON the target
        /// doesn't block a shot AT the target), and <paramref name="start"/> is skipped too when
        /// <paramref name="skipFirstCell"/> is set (the shooter's own cell never blocks its own shot).
        /// </summary>
        public static bool LineOfSight(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell = false)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(start, map) || !GenGrid.InBounds(end, map)) return false;

            bool tieBreakToX = start.x != end.x ? start.x < end.x : start.z < end.z;
            int dx = Math.Abs(end.x - start.x);
            int dz = Math.Abs(end.z - start.z);
            int x = start.x;
            int z = start.z;
            int stepsLeft = 1 + dx + dz; // Manhattan path length + 1; the loop below never visits the last (destination) cell.
            int xInc = end.x > start.x ? 1 : -1;
            int zInc = end.z > start.z ? 1 : -1;
            int error = 2 * dx - 2 * dz;

            while (stepsLeft > 1)
            {
                if (!skipFirstCell || x != start.x || z != start.z)
                {
                    if (!CanBeSeenOver(new IntVec3(x, 0, z), map)) return false;
                }

                if (error > 0 || (error == 0 && tieBreakToX))
                {
                    x += xInc;
                    error -= 4 * dz;
                }
                else
                {
                    z += zInc;
                    error += 4 * dx;
                }
                stepsLeft--;
            }
            return true;
        }

        /// <summary>
        /// Whether a cell blocks sight through it (RimWorld: <c>GridsUtility.CanBeSeenOverFast</c>): the
        /// edifice occupying it, if any, must not fully fill the cell. <b>Deviation:</b> real RimWorld also
        /// exempts an open door; this port's <see cref="Building.Door"/> carries no open/closed state at all
        /// (see that class's own remarks) and its <c>fillPercent</c> stays 1 permanently, so a Door always
        /// blocks sight here, matching how it already always blocks path cost.
        /// </summary>
        public static bool CanBeSeenOver(IntVec3 c, Map map)
        {
            Things.Thing? edifice = GenGrid.GetEdifice(c, map);
            return edifice == null || edifice.def.Fillage != Defs.FillCategory.Full;
        }
    }
}
