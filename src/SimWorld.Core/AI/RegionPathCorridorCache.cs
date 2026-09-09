using System;
using System.Collections.Generic;
using SimWorld.Map;

namespace SimWorld.AI
{
    /// <summary>
    /// Path sharing for <see cref="PathFinder"/> (system 9's <c>ai.pathing.sharing</c> pass, translated —
    /// RimWorld's own colony scale never needed this). The problem this exists to fix: every pawn's
    /// <see cref="PathFinder.FindPath"/> call pays for its own full-map A* even when a hundred other pawns
    /// are, this same tick, walking to the same well or the same door — full-agent civilization-scale
    /// populations cannot afford that.
    ///
    /// <b>Why the region graph:</b> <see cref="Map.RegionGrid"/> already exists, is already incrementally
    /// maintained by <see cref="RegionAndRoomUpdater"/>, and is already the level <see cref="Reachability"/>
    /// answers "can reach" at — reusing it means no second coarse graph to build or keep in sync with the
    /// path grid.
    ///
    /// <b>What gets shared:</b> for a destination, this builds one unweighted BFS over the region graph
    /// rooted at the destination's region(s) — a distance/parent tree recording, for every region reachable
    /// from the destination, which neighbouring region is one hop closer. That tree is the expensive part
    /// (touches every region reachable from the destination) and is paid exactly once per unique destination,
    /// cached, and reused verbatim by every pawn walking there afterwards regardless of which room each one
    /// starts in. Reading a specific pawn's own corridor back out of an already-built tree costs only
    /// O(hops from that pawn's region to the destination) — a handful of dictionary lookups, not a search.
    /// <see cref="PathFinder"/> then constrains that pawn's own cell-level A* to the cells inside the
    /// corridor's regions, so its search is short and local instead of map-wide.
    ///
    /// <b>Cache key is a region set, not a cell:</b> <see cref="PathFinder.BuildGoals"/> can produce more than
    /// one goal cell (<see cref="PathEndMode.Touch"/> and friends path to any cell touching the target, and
    /// those cells can straddle a doorway into two different regions), so the key is the *set* of distinct
    /// goal regions, not the destination cell. Two different destination cells in the same region — pathing
    /// to two different rocks in the same big open quarry, say — share the identical corridor: that is
    /// exactly the sharing this class exists to deliver, not a coincidence to work around.
    ///
    /// <b>Correctness over optimality:</b> two regions are only ever linked
    /// (<see cref="RegionMaker.TryGenerateRegionFrom"/>) when they truly share a walkable cell-to-cell border,
    /// so any chain this class hands back is a real corridor — it can never route a pawn through a wall, and
    /// if the destination is reachable at all the corridor is guaranteed connected end to end. What it does
    /// <i>not</i> guarantee is the shortest cell path: the tree minimizes region <i>hop count</i>, which is
    /// not the same thing as cell distance (a two-hop corridor through tiny rooms can be geometrically longer
    /// than a one-hop corridor through one huge room), so a corridor-constrained path can come out longer than
    /// <see cref="PathFinder"/>'s own unconstrained optimum. That is disclosed and bounded by
    /// <c>PathSharingTests</c>, not hidden — see <see cref="PathFinder"/>'s own remarks for how it protects
    /// against a corridor being wrong outright: any attempt this cache cannot complete (no region at the
    /// start, no region at any goal cell, or the tree saying the start cannot reach a goal region) returns
    /// <c>false</c>, and <see cref="PathFinder"/> reruns the search unconstrained rather than trusting that
    /// absence of an answer.
    ///
    /// <b>Invalidation, one sentence:</b> the whole cache is discarded the moment
    /// <see cref="RegionAndRoomUpdater.Version"/> moves, because a rebuild always replaces whichever
    /// <see cref="Region"/> objects it touches rather than patching them in place, so a tree built before a
    /// rebuild can hold parent pointers through regions that no longer exist or no longer link the way they
    /// used to. That is coarser than strictly necessary — most rebuilds are local (see
    /// <c>RegionAndRoomUpdater</c>'s own remarks) and leave most cached trees perfectly valid — but it is
    /// simple enough to state and test in one sentence, and cheap relative to what it protects: the next
    /// query after any rebuild pays for one fresh BFS over however many regions are reachable from its
    /// destination, not a per-cell pass over the map.
    /// </summary>
    internal sealed class RegionPathCorridorCache
    {
        private readonly Map.Map map;

        /// <summary>The <see cref="RegionAndRoomUpdater.Version"/> the cached trees below were built against;
        /// a mismatch means the whole cache is stale and gets thrown away before the next lookup.</summary>
        private int builtAtVersion = -1;

        private readonly Dictionary<GoalRegionSetKey, CorridorTree> trees = new Dictionary<GoalRegionSetKey, CorridorTree>();

        // ---- reused scratch state: a corridor-eligible FindPath call allocates nothing here ----
        private readonly Queue<Region> bfsQueue = new Queue<Region>();
        private readonly Region?[] goalRegionScratch = new Region?[PathFinder.MaxGoals];
        private readonly int[] keyScratch = new int[PathFinder.MaxGoals];

        public RegionPathCorridorCache(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>
        /// Clears <paramref name="corridor"/> and, if a corridor from <paramref name="start"/> to one of the
        /// first <paramref name="goalCount"/> cells of <paramref name="goalCells"/> can be produced, refills it
        /// with that chain of regions and returns <c>true</c>. Returns <c>false</c> — with <paramref name="corridor"/>
        /// left empty — when it cannot: no region at <paramref name="start"/>, no region at any goal cell, or
        /// the coarse graph says <paramref name="start"/>'s region cannot reach any goal region. <c>false</c> is
        /// never a claim that no path exists — see this class's own remarks — only that the caller should fall
        /// back to an unconstrained search to find out.
        /// </summary>
        public bool TryBuildCorridor(IntVec3 start, IntVec3[] goalCells, int goalCount, HashSet<Region> corridor)
        {
            corridor.Clear();

            // Same lazy rebuild Reachability already does before reading the graph (Reachability.cs) — a
            // no-op when nothing is dirty, so a corridor-eligible FindPath call pays no more for this than
            // a reachability query already would.
            map.regionAndRoomUpdater.RebuildIfNeeded();

            Region? startRegion = map.regionGrid.RegionAt(start);
            if (startRegion == null) return false;

            int distinctGoalRegions = CollectDistinctGoalRegions(goalCells, goalCount);
            if (distinctGoalRegions == 0) return false;

            if (map.regionAndRoomUpdater.Version != builtAtVersion)
            {
                trees.Clear();
                builtAtVersion = map.regionAndRoomUpdater.Version;
            }

            GoalRegionSetKey key = MakeKey(distinctGoalRegions);
            if (!trees.TryGetValue(key, out CorridorTree? tree))
            {
                tree = BuildTree(distinctGoalRegions);
                trees[key] = tree;
            }

            return tree.TryExtractCorridor(startRegion, corridor);
        }

        /// <summary>Fills <see cref="goalRegionScratch"/> with the distinct (by reference), non-null regions
        /// among the first <paramref name="goalCount"/> goal cells and returns how many there are. A cell with
        /// no region (unwalkable — the common case for e.g. the impassable rock a Touch-mode path is aimed
        /// at) contributes nothing; its walkable neighbours, also present among the goal cells, do.</summary>
        private int CollectDistinctGoalRegions(IntVec3[] goalCells, int goalCount)
        {
            int distinct = 0;
            for (int i = 0; i < goalCount; i++)
            {
                Region? r = map.regionGrid.RegionAt(goalCells[i]);
                if (r == null) continue;

                bool already = false;
                for (int j = 0; j < distinct; j++)
                {
                    if (ReferenceEquals(goalRegionScratch[j], r)) { already = true; break; }
                }
                if (already) continue;

                goalRegionScratch[distinct] = r;
                distinct++;
            }
            return distinct;
        }

        private GoalRegionSetKey MakeKey(int distinctGoalRegions)
        {
            for (int i = 0; i < distinctGoalRegions; i++) keyScratch[i] = goalRegionScratch[i]!.id;
            Array.Sort(keyScratch, 0, distinctGoalRegions);
            return GoalRegionSetKey.FromSorted(keyScratch, distinctGoalRegions);
        }

        /// <summary>Multi-source BFS rooted at the <paramref name="distinctGoalRegions"/> regions in
        /// <see cref="goalRegionScratch"/>: every region gets, at most once, a "next hop toward a goal region"
        /// parent pointer (<c>null</c> for a goal region itself — "arrived"). Goal regions are enqueued in
        /// <see cref="PathFinder.BuildGoals"/>'s own fixed cell order and every region's <see cref="Region.Links"/>
        /// list is itself populated in a fixed order by <see cref="RegionMaker"/>, so this tree — and every
        /// corridor read back out of it — is deterministic for the same region graph and the same query.</summary>
        private CorridorTree BuildTree(int distinctGoalRegions)
        {
            var next = new Dictionary<Region, Region?>();
            bfsQueue.Clear();
            for (int i = 0; i < distinctGoalRegions; i++)
            {
                Region g = goalRegionScratch[i]!;
                if (next.ContainsKey(g)) continue; // two goal cells sharing a region: seed it once
                next[g] = null;
                bfsQueue.Enqueue(g);
            }

            while (bfsQueue.Count > 0)
            {
                Region cur = bfsQueue.Dequeue();
                IReadOnlyList<RegionLink> links = cur.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    Region? other = links[i].GetOtherRegion(cur);
                    if (other == null || next.ContainsKey(other)) continue;
                    if ((other.type & RegionType.Set_Passable) == 0) continue;
                    next[other] = cur;
                    bfsQueue.Enqueue(other);
                }
            }

            return new CorridorTree(next);
        }

        /// <summary>One destination's shared BFS tree: for every region reachable from that destination, the
        /// neighbouring region one hop closer to it.</summary>
        private sealed class CorridorTree
        {
            private readonly Dictionary<Region, Region?> next;

            public CorridorTree(Dictionary<Region, Region?> next)
            {
                this.next = next;
            }

            /// <summary>Walks parent pointers from <paramref name="start"/> to a goal region, adding every
            /// region visited along the way to <paramref name="corridor"/>. Returns <c>false</c> without
            /// mutating anything further if <paramref name="start"/> never entered the tree (unreachable from
            /// every goal region this tree was built for).</summary>
            public bool TryExtractCorridor(Region start, HashSet<Region> corridor)
            {
                if (!next.ContainsKey(start)) return false;

                Region cur = start;
                while (true)
                {
                    // A BFS parent tree cannot cycle; corridor.Add returning false would mean it did, which
                    // would be a bug in BuildTree rather than a real graph shape — stop rather than loop
                    // forever either way.
                    if (!corridor.Add(cur)) break;
                    if (!next.TryGetValue(cur, out Region? nxt)) return false;
                    if (nxt == null) break; // cur is itself a goal region: arrived
                    cur = nxt;
                }
                return true;
            }
        }

        /// <summary>Up to <see cref="PathFinder.MaxGoals"/> region ids, sorted ascending and padded with
        /// <c>-1</c> (never a real <see cref="Region.id"/>) — the exact goal-region set as a value-type
        /// dictionary key, so a repeat query for the same destination costs a handful of int comparisons and
        /// no heap allocation, whether it hits or misses.</summary>
        private readonly struct GoalRegionSetKey : IEquatable<GoalRegionSetKey>
        {
            private readonly int id0, id1, id2, id3, id4, id5, id6, id7;

            private GoalRegionSetKey(int id0, int id1, int id2, int id3, int id4, int id5, int id6, int id7)
            {
                this.id0 = id0; this.id1 = id1; this.id2 = id2; this.id3 = id3;
                this.id4 = id4; this.id5 = id5; this.id6 = id6; this.id7 = id7;
            }

            public static GoalRegionSetKey FromSorted(int[] sortedIds, int count)
            {
                int At(int i) => i < count ? sortedIds[i] : -1;
                return new GoalRegionSetKey(At(0), At(1), At(2), At(3), At(4), At(5), At(6), At(7));
            }

            public bool Equals(GoalRegionSetKey other) =>
                id0 == other.id0 && id1 == other.id1 && id2 == other.id2 && id3 == other.id3 &&
                id4 == other.id4 && id5 == other.id5 && id6 == other.id6 && id7 == other.id7;

            public override bool Equals(object? obj) => obj is GoalRegionSetKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(id0, id1, id2, id3, id4, id5, id6, id7);
        }
    }
}
