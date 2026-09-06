using System;
using System.Collections.Generic;

namespace SimWorld.World
{
    /// <summary>
    /// The planet's tile mesh: a subdivided icosahedron (RimWorld: <c>RimWorld.Planet.WorldGrid</c>, which
    /// instead subdivides to a target tile count derived from planet coverage). Tiles are addressed by
    /// index (<c>tileId</c>) throughout the world-gen pipeline, matching RimWorld's <c>int tileID</c> convention.
    /// Purely geometric — never seeded — so two grids built at the same <see cref="SubdivisionLevel"/> are
    /// always identical.
    /// </summary>
    public sealed class WorldGrid
    {
        private readonly List<Vector3> vertices;
        private readonly List<List<int>> neighbors;
        private readonly List<Tile> tiles;

        public int SubdivisionLevel { get; }

        public IReadOnlyList<Tile> Tiles => tiles;

        public int TilesCount => tiles.Count;

        private WorldGrid(int subdivisionLevel, List<Vector3> vertices, List<List<int>> neighbors)
        {
            SubdivisionLevel = subdivisionLevel;
            this.vertices = vertices;
            this.neighbors = neighbors;
            tiles = new List<Tile>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                tiles.Add(new Tile());
            }
        }

        public static WorldGrid Generate(int subdivisionLevel)
        {
            IcosphereBuilder.Build(subdivisionLevel, out List<Vector3> vertices, out List<List<int>> neighbors);
            return new WorldGrid(subdivisionLevel, vertices, neighbors);
        }

        public Tile this[int tileId] => tiles[tileId];

        /// <summary>Position on the unit sphere.</summary>
        public Vector3 GetTileCenter(int tileId) => vertices[tileId];

        /// <summary>(latitude, longitude) in degrees; latitude ±90 at the poles, longitude ±180 at the date line.</summary>
        public (double latitude, double longitude) LongLatOf(int tileId)
        {
            Vector3 v = vertices[tileId];
            double lat = Math.Asin(GenMath.Clamp(v.y, -1f, 1f)) * (180.0 / Math.PI);
            double lon = Math.Atan2(v.x, -v.z) * (180.0 / Math.PI);
            return (lat, lon);
        }

        public IReadOnlyList<int> NeighborsOf(int tileId) => neighbors[tileId];

        public void GetTileNeighbors(int tileId, List<int> outNeighbors)
        {
            if (outNeighbors == null) throw new ArgumentNullException(nameof(outNeighbors));
            outNeighbors.Clear();
            outNeighbors.AddRange(neighbors[tileId]);
        }

        public bool IsNeighbor(int a, int b) => neighbors[a].Contains(b);

        /// <summary>Fewest hops from <paramref name="a"/> to <paramref name="b"/> over the tile graph (BFS); -1 when unreachable.</summary>
        public int TraversalDistanceBetween(int a, int b)
        {
            if (a == b) return 0;
            var visited = new HashSet<int> { a };
            var frontier = new Queue<int>();
            frontier.Enqueue(a);
            int distance = 0;
            while (frontier.Count > 0)
            {
                distance++;
                int layerSize = frontier.Count;
                for (int i = 0; i < layerSize; i++)
                {
                    int current = frontier.Dequeue();
                    foreach (int next in neighbors[current])
                    {
                        if (next == b) return distance;
                        if (visited.Add(next))
                        {
                            frontier.Enqueue(next);
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Great-circle angle between two tiles converted to an approximate tile count, using the grid's
        /// average tile angular spacing (RimWorld: <c>WorldGrid.ApproxDistanceInTiles</c>).
        /// </summary>
        public float ApproxDistanceInTiles(int a, int b)
        {
            float angle = Vector3.AngleBetween(vertices[a], vertices[b]);
            return angle / AverageTileAngularSize();
        }

        private float? averageTileAngularSize;

        private float AverageTileAngularSize()
        {
            if (averageTileAngularSize.HasValue) return averageTileAngularSize.Value;
            // Full-sphere solid angle 4π split evenly over every tile, then back to a linear angular size.
            double solidAnglePerTile = 4.0 * Math.PI / tiles.Count;
            float size = (float)Math.Sqrt(solidAnglePerTile);
            averageTileAngularSize = size;
            return size;
        }
    }
}
