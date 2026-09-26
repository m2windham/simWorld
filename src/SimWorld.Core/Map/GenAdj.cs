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
        /// Footprint a Thing of <paramref name="size"/> occupies at <paramref name="center"/> facing
        /// <paramref name="rot"/> (RimWorld: <c>Verse.GenAdj.OccupiedRect</c>, through
        /// <see cref="AdjustForRotation"/>). The footprint turns about the Thing's own cell: a 1x2 bed
        /// facing North covers its cell and the one north of it, facing South its cell and the one south of
        /// it, and so on round, so <c>Position</c> is always the same end of the bed (its sleeping slot — see
        /// <c>AI.BedUtility.GetSleepingSlotPos</c>).
        ///
        /// <para/>This used to swap width and depth for East/West and stop there, so every even span fell on
        /// the high side of <paramref name="center"/> whatever the facing: a South bed covered exactly the
        /// cells a North bed did, and a West one the cells an East one did. Nothing noticed while no shipped
        /// def set a size; <c>Bed</c> is 1x2, as in RimWorld, and the host draws the rect this returns.
        /// </summary>
        public static CellRect OccupiedRect(IntVec3 center, Rot4 rot, IntVec2 size)
        {
            AdjustForRotation(ref center, ref size, rot);
            return new CellRect(center.x - (size.x - 1) / 2, center.z - (size.z - 1) / 2, size.x, size.z);
        }

        /// <summary>
        /// Rotates <paramref name="size"/> to <paramref name="rot"/> and shifts <paramref name="center"/> so
        /// an even span lands on the side the Thing faces (RimWorld: <c>Verse.GenAdj.AdjustForRotation</c>,
        /// ported as it stands): East and West swap the two spans; East moves an even depth down one, South
        /// moves an even width and an even depth down one each, West moves an even width down one. A 1x1
        /// Thing is unchanged whatever its facing.
        /// </summary>
        public static void AdjustForRotation(ref IntVec3 center, ref IntVec2 size, Rot4 rot)
        {
            if (size.x == 1 && size.z == 1) return;
            if (rot.IsHorizontal)
            {
                int x = size.x;
                size.x = size.z;
                size.z = x;
            }
            switch (rot.AsInt)
            {
                case Rot4.EastInt:
                    if (size.z % 2 == 0) center.z--;
                    break;
                case Rot4.SouthInt:
                    if (size.x % 2 == 0) center.x--;
                    if (size.z % 2 == 0) center.z--;
                    break;
                case Rot4.WestInt:
                    if (size.x % 2 == 0) center.x--;
                    break;
            }
        }
    }
}
