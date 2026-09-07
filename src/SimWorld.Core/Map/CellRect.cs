using System;
using System.Collections.Generic;

namespace SimWorld.Map
{
    /// <summary>An axis-aligned rectangle of cells (RimWorld: <c>Verse.CellRect</c>).</summary>
    public struct CellRect : IEquatable<CellRect>
    {
        public int minX;
        public int minZ;
        public int width;
        public int height;

        public CellRect(int minX, int minZ, int width, int height)
        {
            this.minX = minX;
            this.minZ = minZ;
            this.width = width;
            this.height = height;
        }

        public int maxX => minX + width - 1;
        public int maxZ => minZ + height - 1;

        public int Width => width;
        public int Height => height;
        public int Area => width * height;
        public bool IsEmpty => width <= 0 || height <= 0;

        public static CellRect Empty => new CellRect(0, 0, 0, 0);

        public static CellRect SingleCell(IntVec3 cell) => new CellRect(cell.x, cell.z, 1, 1);

        public static CellRect FromLimits(int minX, int minZ, int maxX, int maxZ) =>
            new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);

        public static CellRect CenteredOn(IntVec3 center, int radius) => CenteredOn(center, radius, radius);

        public static CellRect CenteredOn(IntVec3 center, int radiusX, int radiusZ) =>
            new CellRect(center.x - radiusX, center.z - radiusZ, radiusX * 2 + 1, radiusZ * 2 + 1);

        public bool Contains(IntVec3 c) => c.x >= minX && c.x <= maxX && c.z >= minZ && c.z <= maxZ;

        public CellRect ExpandedBy(int dist) => new CellRect(minX - dist, minZ - dist, width + dist * 2, height + dist * 2);

        public CellRect ContractedBy(int dist) => ExpandedBy(-dist);

        /// <summary>Intersection with another rect; empty (non-positive width or height) when they do not overlap.</summary>
        public CellRect ClipInsideRect(CellRect within)
        {
            int newMinX = Math.Max(minX, within.minX);
            int newMinZ = Math.Max(minZ, within.minZ);
            int newMaxX = Math.Min(maxX, within.maxX);
            int newMaxZ = Math.Min(maxZ, within.maxZ);
            return FromLimits(newMinX, newMinZ, newMaxX, newMaxZ);
        }

        public CellRect ClipInsideMap(Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return ClipInsideRect(new CellRect(0, 0, map.Size.x, map.Size.z));
        }

        public IEnumerable<IntVec3> Cells
        {
            get
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }

        /// <summary>Cells on the rect's border only; interior cells are skipped.</summary>
        public IEnumerable<IntVec3> EdgeCells
        {
            get
            {
                if (IsEmpty) yield break;
                for (int x = minX; x <= maxX; x++)
                {
                    yield return new IntVec3(x, 0, minZ);
                    if (height > 1) yield return new IntVec3(x, 0, maxZ);
                }
                for (int z = minZ + 1; z <= maxZ - 1; z++)
                {
                    yield return new IntVec3(minX, 0, z);
                    if (width > 1) yield return new IntVec3(maxX, 0, z);
                }
            }
        }

        public bool Equals(CellRect other) => minX == other.minX && minZ == other.minZ && width == other.width && height == other.height;
        public override bool Equals(object? obj) => obj is CellRect other && Equals(other);
        public override int GetHashCode() => unchecked(((minX * 397) ^ minZ) * 397 ^ width) * 397 ^ height;
        public static bool operator ==(CellRect a, CellRect b) => a.Equals(b);
        public static bool operator !=(CellRect a, CellRect b) => !a.Equals(b);

        public override string ToString() => "(" + minX + ", " + minZ + ", " + width + ", " + height + ")";
    }
}
