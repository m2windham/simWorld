using System;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;

namespace SimWorld.AI
{
    /// <summary>
    /// Moves a pawn cell to cell along a <see cref="PawnPath"/> (RimWorld: <c>Verse.AI.Pawn_PathFollower</c>).
    /// Ticked once per pawn per tick from <see cref="Pawn_JobTracker.JobTrackerTick"/> (see that class for why
    /// pathing is folded into the job tick rather than ticked separately — nothing else in this pass drives
    /// movement). Holds no per-tick-allocating state: a countdown int and a pooled <see cref="PawnPath"/>.
    /// </summary>
    public sealed class Pawn_PathFollower : IExposable
    {
        /// <summary>√2, RimWorld's own scaling from a cardinal move cost to a diagonal one.</summary>
        private const float DiagonalMoveFactor = 1.41421356f;

        /// <summary>The most one cell's move can take (RimWorld: the 450 clamp in
        /// <c>Pawn_PathFollower.CostToMoveIntoCell</c>).</summary>
        private const int MaxTicksPerCell = 450;

        /// <summary>How many cells ahead <see cref="NeedNewPath"/> looks for something that has gone solid since
        /// the path was planned (RimWorld: the 20 in <c>Pawn_PathFollower.NeedNewPath</c>'s look-ahead loop;
        /// recalled, not sourced).</summary>
        private const int NeedNewPathLookAheadCells = 20;

        private readonly Pawn pawn;
        private PawnPath? curPath;
        private LocalTargetInfo destination = LocalTargetInfo.Invalid;
        private PathEndMode peMode;
        private int ticksLeftThisCell;
        private bool moving;

        public Pawn_PathFollower(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public bool Moving => moving;

        public LocalTargetInfo Destination => destination;

        /// <summary>Set when the path was abandoned mid-flight (something made the next cell unwalkable),
        /// as opposed to <see cref="Moving"/> simply going false on ordinary arrival.</summary>
        public bool Failed { get; private set; }

        /// <summary>Ticks to move one cardinal cell at the pawn's current <see cref="StatDefOf.MoveSpeed"/>
        /// (RimWorld: <c>Pawn_PathFollower.TicksPerMoveCardinal</c> — 60 ticks/second ÷ cells/second), scaled
        /// by the weather when the pawn is out under it.</summary>
        public int TicksPerMoveCardinal
        {
            get
            {
                float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);

                // Weather slows a pawn only under open sky (RimWorld: Pawn.TicksPerMove divides by
                // Map.weatherManager.CurMoveSpeedMultiplier for an unroofed pawn — the one place that
                // multiplier is read). Clear weather multiplies by exactly 1, so a map that never sees bad
                // weather moves at precisely the speed it always did.
                Map.Map? weatherMap = pawn.Map;
                if (weatherMap != null && !GenGrid.Roofed(pawn.Position, weatherMap))
                {
                    speed *= weatherMap.weatherManager.CurMoveSpeedMultiplier;
                }

                if (speed <= 0f) speed = 0.01f;
                return Math.Max(1, (int)Math.Round(GenTicks.TicksPerRealSecond / speed, MidpointRounding.AwayFromZero));
            }
        }

        public int TicksPerMoveDiagonal =>
            Math.Max(1, (int)Math.Round(TicksPerMoveCardinal * DiagonalMoveFactor, MidpointRounding.AwayFromZero));

        /// <summary>Starts (or, if already headed to the same target the same way, continues) a path.</summary>
        public void StartPath(LocalTargetInfo dest, PathEndMode mode)
        {
            Failed = false;
            Map.Map? map = pawn.Map;
            if (map == null || !dest.IsValid) return;

            if (moving && destination.Equals(dest) && peMode == mode && curPath != null && !curPath.Finished) return;

            StopDead();

            PawnPath path = map.pathFinder.FindPath(pawn, pawn.Position, dest, mode);
            if (!path.Found)
            {
                path.ReleaseToPool();
                return;
            }

            destination = dest;
            peMode = mode;
            if (path.Finished)
            {
                // Already standing on an acceptable cell.
                path.ReleaseToPool();
                return;
            }

            curPath = path;
            moving = true;
            ticksLeftThisCell = CostToNextCell();
        }

        /// <summary>Abandons the current path without failing it (arrival, or an external interrupt).</summary>
        public void StopDead()
        {
            curPath?.ReleaseToPool();
            curPath = null;
            moving = false;
            destination = LocalTargetInfo.Invalid;
        }

        public void PatherTick()
        {
            if (!moving || curPath == null) return;
            if (pawn.Map == null)
            {
                StopDead();
                return;
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return;

            ticksLeftThisCell--;
            if (ticksLeftThisCell > 0) return;

            IntVec3 next = curPath.Peek(0);
            if (!pawn.Map.pathGrid.Walkable(next))
            {
                PatherFailed();
                return;
            }

            IntVec3 previous = pawn.Position;
            pawn.Position = next;
            // system: filth — the only place in this codebase a spawned pawn walks cell to cell, and so
            // RimWorld's own call site for this (Pawn_PathFollower.TryEnterNextPathCell ->
            // pawn.filth.Notify_EnteredNewCell). Dirties bare ground underfoot and tracks dirt in off it.
            SimWorld.Filth.Pawn_FilthTracker.Notify_EnteredNewCell(pawn, previous, next);
            curPath.ConsumeNextNode();
            if (curPath.Finished)
            {
                PatherArrived();
                return;
            }

            // RimWorld: TryEnterNextPathCell asks NeedNewPath on every cell it enters and replans through
            // TrySetNewPath (which fails the pather itself when no route is left). Without it a wall finished
            // a few cells ahead was only found by walking up to it.
            if (NeedNewPath() && !TrySetNewPath()) return;
            if (curPath == null) return; // the new path started on an acceptable cell: arrived
            ticksLeftThisCell = CostToNextCell();
        }

        /// <summary>
        /// Whether the path ahead has stopped being walkable since it was planned (RimWorld:
        /// <c>Pawn_PathFollower.NeedNewPath</c>'s look-ahead over the next
        /// <see cref="NeedNewPathLookAheadCells"/> nodes: an unwalkable cell, or a diagonal step whose corner
        /// has since been walled). RimWorld's other reasons are not ported here: pawn collision, known
        /// danger and forbidden or locked doors have no counterpart in this core (doors here are always
        /// open), and its destination checks (target moved, target gone, region-heuristic paths) are left
        /// to the job drivers that already handle them.
        /// </summary>
        private bool NeedNewPath()
        {
            if (curPath == null || !curPath.Found || curPath.Finished) return true;

            PathGrid grid = pawn.Map!.pathGrid;
            IntVec3 previous = pawn.Position;
            int ahead = Math.Min(NeedNewPathLookAheadCells, curPath.NodesLeftCount);
            for (int i = 0; i < ahead; i++)
            {
                IntVec3 c = curPath.Peek(i);
                if (!grid.Walkable(c)) return true;
                // The PathFinder's own corner rule (RimWorld: PathFinder.BlocksDiagonalMovement on both
                // corners): a diagonal step needs both cardinal neighbours open.
                if (c.x != previous.x && c.z != previous.z
                    && (!grid.Walkable(new IntVec3(c.x, 0, previous.z)) || !grid.Walkable(new IntVec3(previous.x, 0, c.z))))
                {
                    return true;
                }
                previous = c;
            }
            return false;
        }

        /// <summary>Replans from where the pawn stands to the same destination (RimWorld:
        /// <c>Pawn_PathFollower.TrySetNewPath</c>), failing the pather when there is no route left.</summary>
        private bool TrySetNewPath()
        {
            PawnPath path = pawn.Map!.pathFinder.FindPath(pawn, pawn.Position, destination, peMode);
            if (!path.Found)
            {
                PatherFailed();
                return false;
            }

            curPath?.ReleaseToPool();
            curPath = path;
            if (path.Finished) PatherArrived();
            return true;
        }

        /// <summary>RimWorld: <c>Pawn_PathFollower.PatherArrived</c> — stop on reaching an acceptable cell.</summary>
        private void PatherArrived()
        {
            curPath?.ReleaseToPool();
            curPath = null;
            moving = false;
        }

        /// <summary>RimWorld: <c>Pawn_PathFollower.PatherFailed</c> — stop, and tell the job (through
        /// <see cref="Failed"/>, which the goto toils fail on) that the route is gone.</summary>
        private void PatherFailed()
        {
            Failed = true;
            StopDead();
        }

        private int CostToNextCell()
        {
            IntVec3 cur = pawn.Position;
            IntVec3 next = curPath!.Peek(0);
            bool diagonal = cur.x != next.x && cur.z != next.z;
            int baseCost = diagonal ? TicksPerMoveDiagonal : TicksPerMoveCardinal;
            // The real cost, not the perceived one (RimWorld: CostToMoveIntoCell reads
            // CalculatedCostAt(c, perceivedStatic: false)) — a fire is something to route round, not mud.
            // Capped at one step's worth, as RimWorld caps it (CostToMoveIntoCell: num > 450 -> 450). Uncapped,
            // a wall raised on the next cell after the path was planned charged its impassable cost (10000)
            // as walking time, and the walker stood frozen most of a day before PatherTick saw the wall.
            return Math.Min(MaxTicksPerCell, baseCost + pawn.Map!.pathGrid.CalculatedCostAt(next));
        }

        public void ExposeData()
        {
            // Movement state is small and cheap to just recompute: a loaded pawn that was mid-path simply
            // starts a fresh path toward the same destination once its job resumes (see Pawn_JobTracker's
            // own load comment) — nothing here needs saving beyond nothing.
        }
    }
}
