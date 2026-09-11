using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.World.Gen
{
    /// <summary>
    /// Traces rivers downhill from high, wet tiles to the sea (RimWorld: <c>Verse.WorldGenStep_Rivers</c>,
    /// a faithful-in-shape simplification of RimWorld's real flow-accumulation network). Every tile's
    /// downhill neighbour is fixed by elevation alone, so tributaries that share a lower reach automatically
    /// merge and accumulate flow; a source that dead-ends at a local minimum without reaching water
    /// (an inland basin — not modelled as a lake here) is simply discarded.
    /// <para/>
    /// Where the water ends up is the flow network's business; whether a tile may carry a river at all is the
    /// biome's (<see cref="BiomeDef.allowRivers"/> — see <see cref="AllowsRivers"/>).
    /// </summary>
    public class WorldGenStep_Rivers : WorldGenStep
    {
        /// <summary>Only the highest quarter of land tiles by elevation are candidate river sources.</summary>
        public const float SourceElevationPercentile = 0.75f;

        public override void GenerateFresh(string seed, World world)
        {
            WorldGrid grid = world.grid;
            int n = grid.TilesCount;

            int[] downstream = BuildDownstreamPointers(grid);
            List<int> sources = FindSources(grid);

            var flow = new float[n];
            foreach (int source in sources)
            {
                AccumulateFlow(grid, downstream, source, flow);
            }

            IReadOnlyList<RiverDef> riverDefs = SortedBySpawnThreshold();
            if (riverDefs.Count == 0)
            {
                return;
            }

            for (int i = 0; i < n; i++)
            {
                if (flow[i] <= 0f || grid.Tiles[i].WaterCovered)
                {
                    continue;
                }
                int next = downstream[i];
                if (next < 0)
                {
                    continue;
                }
                if (!AllowsRivers(grid.Tiles[i]))
                {
                    continue;
                }
                RiverDef chosen = ChooseRiverDef(riverDefs, flow[i]);
                grid.Tiles[i].potentialRivers.Add(new RiverLink(next, chosen));
                if (AllowsRivers(grid.Tiles[next]))
                {
                    grid.Tiles[next].potentialRivers.Add(new RiverLink(i, chosen));
                }
            }
        }

        /// <summary>
        /// Whether a river may be written <em>onto</em> this tile (RimWorld: <c>BiomeDef.allowRivers</c>,
        /// which its own river generation consults per tile — the exact call site is not sourceable in this
        /// sandbox, so what is ported is the rule, pinned by tests rather than copied).
        ///
        /// <para/><b>Why the two ends of a link are asked separately.</b> The shipped content refuses rivers
        /// on exactly the water biomes — ocean, lake, sea ice — and every river in the world ends by running
        /// into one of them. Dropping the whole link at a river's mouth would leave its last land tile with
        /// no downhill river neighbour at all, which is both wrong on the map and a break of the invariant
        /// that a river tile can always be followed to water. So each end is written only if its own biome
        /// permits a river: the coastal land tile keeps its link out to sea, the ocean tile itself carries no
        /// river. A river crossing sea ice mid-course, by contrast, loses the sea-ice tile entirely and the
        /// network simply ends there.
        ///
        /// <para/>A tile with no biome allows rivers: the Biomes step runs at order 200 and this one at 300,
        /// so in the shipped pipeline every tile already has one, and a caller running this step alone (a
        /// test, a future pipeline) gets the behaviour it had before the biome was consulted.
        ///
        /// <para/>This only ever <em>filters</em> links the flow network already decided on — flow
        /// accumulation, the downhill pointers and the <see cref="RiverDef"/> choice are untouched, and this
        /// step draws no randomness at all — so consulting the biome cannot move the world seed's sequence.
        /// </summary>
        private static bool AllowsRivers(Tile tile) => tile.biome == null || tile.biome.allowRivers;

        /// <summary>For every tile, its single strictly-lower-elevation neighbour (or -1 at a local minimum). A pure function of elevation, so it never depends on which source discovers it first.</summary>
        private static int[] BuildDownstreamPointers(WorldGrid grid)
        {
            int n = grid.TilesCount;
            var downstream = new int[n];
            for (int i = 0; i < n; i++)
            {
                float bestElevation = grid.Tiles[i].elevation;
                int best = -1;
                foreach (int neighbor in grid.NeighborsOf(i))
                {
                    float e = grid.Tiles[neighbor].elevation;
                    if (e < bestElevation)
                    {
                        bestElevation = e;
                        best = neighbor;
                    }
                }
                downstream[i] = best;
            }
            return downstream;
        }

        private static List<int> FindSources(WorldGrid grid)
        {
            var land = new List<int>();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (!grid.Tiles[i].WaterCovered) land.Add(i);
            }
            if (land.Count == 0)
            {
                return new List<int>();
            }

            var elevations = new float[land.Count];
            var rainfalls = new float[land.Count];
            for (int i = 0; i < land.Count; i++)
            {
                elevations[i] = grid.Tiles[land[i]].elevation;
                rainfalls[i] = grid.Tiles[land[i]].rainfall;
            }
            float elevationThreshold = Percentile(elevations, SourceElevationPercentile);
            float rainfallThreshold = Percentile(rainfalls, 0.5f);

            var candidates = new List<int>();
            foreach (int t in land)
            {
                Tile tile = grid.Tiles[t];
                if (tile.elevation >= elevationThreshold && tile.rainfall >= rainfallThreshold)
                {
                    candidates.Add(t);
                }
            }
            candidates.Sort((a, b) =>
            {
                int byElevation = grid.Tiles[b].elevation.CompareTo(grid.Tiles[a].elevation);
                return byElevation != 0 ? byElevation : a.CompareTo(b);
            });

            int cap = Math.Max(3, land.Count / 15);
            if (candidates.Count > cap)
            {
                candidates.RemoveRange(cap, candidates.Count - cap);
            }
            return candidates;
        }

        private static float Percentile(float[] values, float fraction)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            int index = Math.Max(0, Math.Min(sorted.Length - 1, (int)(fraction * (sorted.Length - 1))));
            return sorted[index];
        }

        private static void AccumulateFlow(WorldGrid grid, int[] downstream, int source, float[] flow)
        {
            var path = new List<int>();
            int current = source;
            bool reachedWater = false;
            for (int steps = 0; steps <= grid.TilesCount; steps++)
            {
                path.Add(current);
                if (grid.Tiles[current].WaterCovered)
                {
                    reachedWater = true;
                    break;
                }
                int next = downstream[current];
                if (next < 0)
                {
                    break;
                }
                current = next;
            }
            if (!reachedWater)
            {
                return;
            }
            foreach (int t in path)
            {
                flow[t] += 1f;
            }
        }

        private static IReadOnlyList<RiverDef> SortedBySpawnThreshold()
        {
            var defs = new List<RiverDef>(DefDatabase<RiverDef>.AllDefsListForReading);
            defs.Sort((a, b) => a.spawnFlowThreshold.CompareTo(b.spawnFlowThreshold));
            return defs;
        }

        private static RiverDef ChooseRiverDef(IReadOnlyList<RiverDef> sortedAscending, float flow)
        {
            RiverDef chosen = sortedAscending[0];
            for (int i = 0; i < sortedAscending.Count; i++)
            {
                if (sortedAscending[i].spawnFlowThreshold <= flow)
                {
                    chosen = sortedAscending[i];
                }
                else
                {
                    break;
                }
            }
            return chosen;
        }
    }
}
