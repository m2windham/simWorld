using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// A* over a map's <see cref="Map.PathGrid"/> (RimWorld: <c>Verse.AI.PathFinder</c>). One instance per
    /// map, its working arrays sized once to the map's cell count and reused for every search — a search
    /// only touches cells it actually visits (a per-cell <see cref="generation"/> stamp distinguishes "not
    /// looked at this search" from "looked at an earlier search" without clearing the arrays), and the open
    /// list is an array-backed binary min-heap that only grows, never reallocates per search. Steady-state
    /// pathfinding therefore allocates nothing once the heap has grown to its working size.
    ///
    /// <b>Open-list shape:</b> lazy-deletion (stale, cheaper-superseded entries are pushed again on
    /// relaxation and skipped when popped) rather than a decrease-key indexed heap — simpler to get right,
    /// at the cost of the heap holding more entries than reachable cells in the worst case. Correct for this
    /// graph (all edge costs are non-negative) but a real decrease-key heap would use less memory on a huge,
    /// densely-connected map; noted here rather than silently assumed.
    ///
    /// <b>Path sharing</b> (system 9's <c>ai.pathing.sharing</c> pass): before running the full-map search,
    /// <see cref="FindPath"/> asks <see cref="corridorCache"/> for a corridor — a chain of <see cref="Region"/>s
    /// from the pawn's start region toward the destination, built once per unique destination and shared by
    /// every pawn walking there. When one is available the search first runs constrained to that corridor's
    /// cells only, so most pawns' own search touches a handful of rooms instead of the whole map; see
    /// <see cref="RegionPathCorridorCache"/> for why that is safe. Either way — no corridor available, or a
    /// corridor that comes back without reaching a goal — <see cref="FindPath"/> falls through to exactly the
    /// unconstrained search this class always ran, so the corridor can only ever make a search cheaper, never
    /// wrong.
    /// </summary>
    public sealed class PathFinder
    {
        /// <summary>Capacity of the fixed-size goal arrays below (a job's <see cref="PathEndMode.Touch"/> et
        /// al. target at most its 8 neighbours plus itself). Also <see cref="RegionPathCorridorCache"/>'s own
        /// scratch-array size, so the two stay in lockstep without a second magic number.</summary>
        internal const int MaxGoals = 8;

        private struct NodeInfo
        {
            public int knownCost;
            public int parentIndex;
            public int generation;
            public bool closed;
        }

        /// <summary>Base tick cost of one cardinal step, before terrain/thing path cost — RimWorld's real
        /// <c>Pawn_PathFollower</c> uses tick-based costs derived from move speed directly; this A* instead
        /// works in the conventional octile-grid unit (10 cardinal / 14 diagonal) so terrain's additive
        /// pathCost values (tuned in <c>TerrainDefs/*.xml</c> against a 0-baseline) stay meaningfully
        /// proportioned regardless of a pawn's own speed, which only enters at the <see cref="Pawn_PathFollower"/>
        /// stage. See this module's report for the tradeoff.</summary>
        private const int CostCardinal = 10;
        private const int CostDiagonal = 14;

        private readonly Map.Map map;
        private NodeInfo[] nodes;
        private int generation;

        private int[] heapCell;
        private int[] heapPriority;
        private int heapCount;

        private readonly int[] goalIndices = new int[MaxGoals];
        private readonly IntVec3[] goalCells = new IntVec3[MaxGoals];
        private int goalCount;

        private readonly List<int> retraceBuffer = new List<int>();

        private readonly RegionPathCorridorCache corridorCache;
        private readonly HashSet<Region> corridorScratch = new HashSet<Region>();

        /// <summary>Bench/test-only escape hatch for A/B-measuring the effect of path sharing on its own —
        /// see <c>docs/perf/baseline.md</c> §9 and <c>tools/bench</c>'s pathing suite. Defaults to
        /// <c>false</c> (sharing on) for every ordinary caller; nothing in the simulation core itself ever
        /// sets it.</summary>
        public bool DisableRegionCorridor { get; set; }

        public PathFinder(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            int n = map.cellIndices.NumGridCells;
            nodes = new NodeInfo[n];
            int initialHeapSize = Math.Max(64, n / 4) + 1;
            heapCell = new int[initialHeapSize];
            heapPriority = new int[initialHeapSize];
            corridorCache = new RegionPathCorridorCache(this.map);
        }

        public PawnPath FindPath(Pawn pawn, IntVec3 start, LocalTargetInfo dest, PathEndMode mode)
        {
            if (!GenGrid.InBounds(start, map)) return PawnPath.NotFound;
            if (nodes.Length != map.cellIndices.NumGridCells) nodes = new NodeInfo[map.cellIndices.NumGridCells];

            BuildGoals(dest, mode);
            if (goalCount == 0) return PawnPath.NotFound;

            // Path sharing: a corridor only ever narrows which cells the search below is willing to expand
            // into — it adds no cell, cost or goal the unconstrained search wouldn't already have accepted —
            // so trying it first can only make this call cheaper, never wrong. See
            // RegionPathCorridorCache's own remarks and PathFinder's class doc for the full argument.
            if (!DisableRegionCorridor && corridorCache.TryBuildCorridor(start, goalCells, goalCount, corridorScratch))
            {
                PawnPath corridorResult = RunSearch(start, corridorScratch);
                if (corridorResult.Found) return corridorResult;
                // The corridor came back without reaching a goal — a stale tree, region-graph edge cases
                // around a target with no region of its own, or (rarest) a genuinely unreachable target that
                // the unconstrained search below is about to also, correctly, fail to reach. Never trust the
                // corridor's silence as an answer; only the unconstrained search gets to say "not found".
            }

            return RunSearch(start, null);
        }

        /// <summary>The A* search itself. <paramref name="corridor"/> is <c>null</c> for the plain,
        /// always-correct unconstrained search this class has always run; when non-null, a neighbour cell is
        /// only expanded if its own region is in the corridor (see <see cref="FindPath"/>) — every other rule
        /// (walkability, corner-cutting, cost) is unchanged, so a corridor search that does succeed found a
        /// path exactly as valid as an unconstrained one would have.</summary>
        private PawnPath RunSearch(IntVec3 start, HashSet<Region>? corridor)
        {
            generation++;
            heapCount = 0;
            int startIndex = map.cellIndices.CellToIndex(start);
            nodes[startIndex] = new NodeInfo { knownCost = 0, parentIndex = -1, generation = generation, closed = false };
            HeapPush(startIndex, Heuristic(startIndex));

            int mapSizeX = map.Size.x, mapSizeZ = map.Size.z;

            while (heapCount > 0)
            {
                int curIndex = HeapPopMin();
                if (nodes[curIndex].generation != generation || nodes[curIndex].closed) continue;
                nodes[curIndex].closed = true;

                if (IsGoal(curIndex)) return Retrace(curIndex, startIndex);

                int curCost = nodes[curIndex].knownCost;
                IntVec3 curCell = map.cellIndices.IndexToCell(curIndex);

                for (int dir = 0; dir < GenAdj.AdjacentCells.Length; dir++)
                {
                    IntVec3 offset = GenAdj.AdjacentCells[dir];
                    int nx = curCell.x + offset.x, nz = curCell.z + offset.z;
                    if (nx < 0 || nx >= mapSizeX || nz < 0 || nz >= mapSizeZ) continue;

                    var nbCell = new IntVec3(nx, 0, nz);
                    int nbIndex = map.cellIndices.CellToIndex(nbCell);
                    if (!map.pathGrid.WalkableFast(nbIndex)) continue;
                    if (corridor != null)
                    {
                        Region? nbRegion = map.regionGrid.RegionAtIndex(nbIndex);
                        if (nbRegion == null || !corridor.Contains(nbRegion)) continue;
                    }

                    bool diagonal = offset.x != 0 && offset.z != 0;
                    if (diagonal)
                    {
                        // No cutting between two solid cardinal neighbours.
                        if (!map.pathGrid.Walkable(new IntVec3(nx, 0, curCell.z))) continue;
                        if (!map.pathGrid.Walkable(new IntVec3(curCell.x, 0, nz))) continue;
                    }

                    int stepCost = (diagonal ? CostDiagonal : CostCardinal) + map.pathGrid.PerceivedPathCostAt(nbCell);
                    int newCost = curCost + stepCost;

                    bool freshOrBetter = nodes[nbIndex].generation != generation
                        || (!nodes[nbIndex].closed && newCost < nodes[nbIndex].knownCost);
                    if (!freshOrBetter) continue;

                    nodes[nbIndex] = new NodeInfo { knownCost = newCost, parentIndex = curIndex, generation = generation, closed = false };
                    HeapPush(nbIndex, newCost + Heuristic(nbIndex));
                }
            }

            return PawnPath.NotFound;
        }

        private void BuildGoals(LocalTargetInfo dest, PathEndMode mode)
        {
            goalCount = 0;
            IntVec3 destCell = dest.Cell;
            bool destWalkable = GenGrid.InBounds(destCell, map) && map.pathGrid.Walkable(destCell);

            if (mode == PathEndMode.OnCell || mode == PathEndMode.None)
            {
                if (destWalkable) AddGoal(destCell);
                return;
            }

            for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
            {
                IntVec3 nb = destCell + GenAdj.AdjacentCells[d];
                if (GenGrid.InBounds(nb, map) && map.pathGrid.Walkable(nb)) AddGoal(nb);
            }
            if (destWalkable) AddGoal(destCell);
        }

        private void AddGoal(IntVec3 c)
        {
            if (goalCount >= goalIndices.Length) return;
            goalIndices[goalCount] = map.cellIndices.CellToIndex(c);
            goalCells[goalCount] = c;
            goalCount++;
        }

        private bool IsGoal(int index)
        {
            for (int i = 0; i < goalCount; i++)
            {
                if (goalIndices[i] == index) return true;
            }
            return false;
        }

        /// <summary>Octile distance (10 cardinal / 14 diagonal, matching the search's own step costs) to the
        /// nearest goal cell — admissible since every step cost is at least the base cardinal/diagonal cost.</summary>
        private int Heuristic(int index)
        {
            IntVec3 c = map.cellIndices.IndexToCell(index);
            int best = int.MaxValue;
            for (int i = 0; i < goalCount; i++)
            {
                IntVec3 g = goalCells[i];
                int dx = Math.Abs(c.x - g.x), dz = Math.Abs(c.z - g.z);
                int h = CostCardinal * Math.Max(dx, dz) + (CostDiagonal - CostCardinal) * Math.Min(dx, dz);
                if (h < best) best = h;
            }
            return best == int.MaxValue ? 0 : best;
        }

        private PawnPath Retrace(int goalIndex, int startIndex)
        {
            retraceBuffer.Clear();
            int cur = goalIndex;
            while (cur != startIndex)
            {
                retraceBuffer.Add(cur);
                cur = nodes[cur].parentIndex;
            }
            PawnPath path = PawnPath.Get();
            for (int i = retraceBuffer.Count - 1; i >= 0; i--)
            {
                path.AddNode(map.cellIndices.IndexToCell(retraceBuffer[i]));
            }
            return path;
        }

        // ---- array-backed binary min-heap, 1-indexed ----

        private void HeapPush(int cellIndex, int priority)
        {
            heapCount++;
            if (heapCount >= heapCell.Length)
            {
                Array.Resize(ref heapCell, heapCell.Length * 2);
                Array.Resize(ref heapPriority, heapPriority.Length * 2);
            }
            int i = heapCount;
            heapCell[i] = cellIndex;
            heapPriority[i] = priority;
            while (i > 1)
            {
                int parent = i / 2;
                if (heapPriority[parent] <= heapPriority[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private int HeapPopMin()
        {
            int result = heapCell[1];
            heapCell[1] = heapCell[heapCount];
            heapPriority[1] = heapPriority[heapCount];
            heapCount--;

            int i = 1;
            while (true)
            {
                int left = i * 2, right = i * 2 + 1, smallest = i;
                if (left <= heapCount && heapPriority[left] < heapPriority[smallest]) smallest = left;
                if (right <= heapCount && heapPriority[right] < heapPriority[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(smallest, i);
                i = smallest;
            }
            return result;
        }

        private void Swap(int a, int b)
        {
            (heapCell[a], heapCell[b]) = (heapCell[b], heapCell[a]);
            (heapPriority[a], heapPriority[b]) = (heapPriority[b], heapPriority[a]);
        }
    }
}
