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
        /// (RimWorld: <c>Pawn_PathFollower.TicksPerMoveCardinal</c> — 60 ticks/second ÷ cells/second).</summary>
        public int TicksPerMoveCardinal
        {
            get
            {
                float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
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
                Failed = true;
                StopDead();
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
                curPath.ReleaseToPool();
                curPath = null;
                moving = false;
                return;
            }
            ticksLeftThisCell = CostToNextCell();
        }

        private int CostToNextCell()
        {
            IntVec3 cur = pawn.Position;
            IntVec3 next = curPath!.Peek(0);
            bool diagonal = cur.x != next.x && cur.z != next.z;
            int baseCost = diagonal ? TicksPerMoveDiagonal : TicksPerMoveCardinal;
            return baseCost + pawn.Map!.pathGrid.PerceivedPathCostAt(next);
        }

        public void ExposeData()
        {
            // Movement state is small and cheap to just recompute: a loaded pawn that was mid-path simply
            // starts a fresh path toward the same destination once its job resumes (see Pawn_JobTracker's
            // own load comment) — nothing here needs saving beyond nothing.
        }
    }
}
