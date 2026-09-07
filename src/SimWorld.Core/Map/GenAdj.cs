using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>Neighbour offsets and multi-cell footprints (RimWorld: <c>Verse.GenAdj</c>).</summary>
    public static class GenAdj
    {
        /// <summary>North, East, South, West, in that order.</summary>
        public static readonly IntVec3[] CardinalDirections =
        {
            new IntVec3(0, 0, 1),
            new IntVec3(1, 0, 0),
            new IntVec3(0, 0, -1),
            new IntVec3(-1, 0, 0),
        };

        /// <summary>NE, SE, SW, NW, in that order.</summary>
        public static readonly IntVec3[] DiagonalDirections =
        {
            new IntVec3(1, 0, 1),
            new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, -1),
            new IntVec3(-1, 0, 1),
        };

        /// <summary>All 8 neighbours, clockwise from North: N, NE, E, SE, S, SW, W, NW.</summary>
        public static readonly IntVec3[] AdjacentCells =
        {
            new IntVec3(0, 0, 1),
            new IntVec3(1, 0, 1),
            new IntVec3(1, 0, 0),
            new IntVec3(1, 0, -1),
            new IntVec3(0, 0, -1),
            new IntVec3(-1, 0, -1),
            new IntVec3(-1, 0, 0),
            new IntVec3(-1, 0, 1),
        };

        public static IEnumerable<IntVec3> CellsAdjacent8Way(IntVec3 cell)
        {
            for (int i = 0; i < AdjacentCells.Length; i++)
            {
                yield return cell + AdjacentCells[i];
            }
        }

        public static IEnumerable<IntVec3> CellsAdjacentCardinal(IntVec3 cell)
        {
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                yield return cell + CardinalDirections[i];
            }
        }

        /// <summary>
        /// Footprint a Thing of <paramref name="size"/> occupies centered on <paramref name="center"/> and
        /// facing <paramref name="rot"/> (RimWorld: <c>Verse.GenAdj.OccupiedRect</c>). East/West rotation
        /// swaps width and depth; for an even span the extra cell falls on the low side of center.
        /// </summary>
        public static CellRect OccupiedRect(IntVec3 center, Rot4 rot, IntVec2 size)
        {
            if (size.x == 1 && size.z == 1) return CellRect.SingleCell(center);
            int width = size.x;
            int height = size.z;
            if (rot.IsHorizontal)
            {
                int tmp = width;
                width = height;
                height = tmp;
            }
            int cornerX = center.x - (width - 1) / 2;
            int cornerZ = center.z - (height - 1) / 2;
            return new CellRect(cornerX, cornerZ, width, height);
        }
    }
}
