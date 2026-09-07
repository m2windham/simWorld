using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.World.Siting
{
    /// <summary>
    /// Scores tiles by how many cheap routes would pass through them (spec §5b.2: "the road generator
    /// already paths by terrain cost, so scoring a tile by how many cheap routes would pass through it is
    /// the same computation, inverted"). Samples pairs from <c>candidateSites</c>, runs
    /// <see cref="Gen.TilePathfinder"/>'s exact cost function and search — the same one
    /// <c>Gen.WorldGenStep_Roads</c> links real settlements with — and counts per-tile traversals, normalized
    /// to the busiest tile actually sampled.
    /// </summary>
    public static class TradePositionScorer
    {
        /// <summary>Pairs sampled per call by default. RimWorld has no equivalent computation to size this
        /// from; this port's own judgement call, generous enough to smooth out sampling noise on a
        /// test-sized grid without costing an all-pairs search on a full planet.</summary>
        public const int DefaultSamplePairs = 40;

        /// <summary>
        /// Returns a tile id → 0..1 traversal score map. Only tiles a sampled shortest path actually crossed
        /// appear; every other tile implicitly scores 0. Empty (never null) when fewer than two candidates
        /// are given or no sampled pair had a passable route.
        /// </summary>
        public static IReadOnlyDictionary<int, float> Score(
            WorldGrid grid,
            IReadOnlyList<int> candidateSites,
            RandomStream rand,
            int samplePairs = DefaultSamplePairs,
            int? maxPathLength = null)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (candidateSites == null) throw new ArgumentNullException(nameof(candidateSites));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            var scores = new Dictionary<int, float>();
            if (candidateSites.Count < 2) return scores;

            int maxLength = maxPathLength ?? Math.Max(8, grid.TilesCount / 50);
            int maxDistinctPairs = candidateSites.Count * (candidateSites.Count - 1) / 2;
            int pairs = Math.Min(samplePairs, maxDistinctPairs);

            var traversals = new Dictionary<int, int>();
            var seenPairs = new HashSet<(int, int)>();
            int attempts = 0;
            int maxAttempts = pairs * 20 + 20;
            while (seenPairs.Count < pairs && attempts < maxAttempts)
            {
                attempts++;
                int indexA = rand.Range(0, candidateSites.Count);
                int indexB = rand.Range(0, candidateSites.Count);
                if (indexA == indexB) continue;

                int siteA = candidateSites[indexA];
                int siteB = candidateSites[indexB];
                (int, int) key = siteA < siteB ? (siteA, siteB) : (siteB, siteA);
                if (!seenPairs.Add(key)) continue;

                List<int>? path = Gen.TilePathfinder.ShortestPath(grid, key.Item1, key.Item2, maxLength);
                if (path == null) continue;

                foreach (int tile in path)
                {
                    traversals.TryGetValue(tile, out int count);
                    traversals[tile] = count + 1;
                }
            }

            int maxCount = 0;
            foreach (int count in traversals.Values)
            {
                if (count > maxCount) maxCount = count;
            }
            if (maxCount <= 0) return scores;

            foreach (KeyValuePair<int, int> kv in traversals)
            {
                scores[kv.Key] = kv.Value / (float)maxCount;
            }
            return scores;
        }
    }
}
