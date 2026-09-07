using System;
using System.Collections.Generic;

namespace SimWorld.World
{
    /// <summary>
    /// Builds a subdivided icosahedron on the unit sphere (RimWorld: <c>Verse.PlanetShapeGenerator</c>,
    /// simplified to a fixed subdivision level instead of a target tile count). Vertices become world tiles;
    /// each subdivision quadruples face count and gives <c>10·4^level + 2</c> vertices, of which exactly 12
    /// (the original icosahedron's corners) keep 5 neighbours — everything else added by subdivision has 6.
    /// Purely geometric: no randomness, so the same level always produces the same grid.
    /// </summary>
    public static class IcosphereBuilder
    {
        public static void Build(int subdivisionLevel, out List<Vector3> vertices, out List<List<int>> neighbors)
        {
            if (subdivisionLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(subdivisionLevel), "Subdivision level must be >= 0.");
            }

            vertices = new List<Vector3>();
            var midpointCache = new Dictionary<long, int>();

            double t = (1.0 + Math.Sqrt(5.0)) / 2.0;
            AddVertex(vertices, -1, t, 0);
            AddVertex(vertices, 1, t, 0);
            AddVertex(vertices, -1, -t, 0);
            AddVertex(vertices, 1, -t, 0);
            AddVertex(vertices, 0, -1, t);
            AddVertex(vertices, 0, 1, t);
            AddVertex(vertices, 0, -1, -t);
            AddVertex(vertices, 0, 1, -t);
            AddVertex(vertices, t, 0, -1);
            AddVertex(vertices, t, 0, 1);
            AddVertex(vertices, -t, 0, -1);
            AddVertex(vertices, -t, 0, 1);

            var faces = new List<(int a, int b, int c)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
                (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
                (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
            };

            for (int level = 0; level < subdivisionLevel; level++)
            {
                var nextFaces = new List<(int a, int b, int c)>(faces.Count * 4);
                midpointCache.Clear();
                foreach ((int a, int b, int c) in faces)
                {
                    int ab = MidpointIndex(vertices, midpointCache, a, b);
                    int bc = MidpointIndex(vertices, midpointCache, b, c);
                    int ca = MidpointIndex(vertices, midpointCache, c, a);
                    nextFaces.Add((a, ab, ca));
                    nextFaces.Add((b, bc, ab));
                    nextFaces.Add((c, ca, bc));
                    nextFaces.Add((ab, bc, ca));
                }
                faces = nextFaces;
            }

            var neighborSets = new List<HashSet<int>>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                neighborSets.Add(new HashSet<int>());
            }
            foreach ((int a, int b, int c) in faces)
            {
                Link(neighborSets, a, b);
                Link(neighborSets, b, c);
                Link(neighborSets, c, a);
            }

            neighbors = new List<List<int>>(vertices.Count);
            foreach (HashSet<int> set in neighborSets)
            {
                var list = new List<int>(set);
                list.Sort();
                neighbors.Add(list);
            }
        }

        private static void Link(List<HashSet<int>> neighborSets, int a, int b)
        {
            neighborSets[a].Add(b);
            neighborSets[b].Add(a);
        }

        private static void AddVertex(List<Vector3> vertices, double x, double y, double z)
        {
            vertices.Add(new Vector3((float)x, (float)y, (float)z).Normalized);
        }

        private static int MidpointIndex(List<Vector3> vertices, Dictionary<long, int> cache, int i0, int i1)
        {
            long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }
            Vector3 mid = ((vertices[i0] + vertices[i1]) * 0.5f).Normalized;
            int index = vertices.Count;
            vertices.Add(mid);
            cache[key] = index;
            return index;
        }
    }
}
