using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// The remedy for hypothermia and heatstroke (RimWorld: <c>JobGiver_SeekSafeTemperature</c>, read in
    /// the B18-era decompile). Only a pawn whose temperature injury has reached the serious stage acts. If the
    /// air where it stands is already inside its comfortable range it waits there, re-thinking every
    /// <see cref="WaitTicks"/> ticks, so it stays put while the injury recovers; otherwise it walks to the
    /// nearest place whose temperature is comfortable. With nowhere comfortable to go it gives no job and the
    /// rest of the think tree decides.
    /// <para/><b>Translation.</b> RimWorld breadth-first searches regions out from the pawn and takes the
    /// first non-doorway region whose room is in range. This port keeps rooms rather than per-region
    /// temperatures, so it takes the comfortable room nearest the pawn by straight-line distance to its
    /// closest cell, then a reachable standable cell in it. Same answer whenever the nearest comfortable room
    /// is also the nearest by path; it can differ when a wall puts a closer room farther away on foot.
    /// </summary>
    public sealed class JobGiver_SeekSafeTemperature : ThinkNode_JobGiver
    {
        /// <summary>RimWorld's <c>Wait_SafeTemperature</c> job length, re-thought on expiry.</summary>
        public const int WaitTicks = 500;

        /// <summary>Cells tried in the chosen room before giving up on it.</summary>
        private const int CellTries = 20;

        private static readonly List<Room> tmpCandidates = new List<Room>();

        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!pawn.Spawned || TemperatureDefOf.Hypothermia == null) return null;
            if (!TemperatureInjuryUtility.HasTemperatureInjury(pawn, TemperatureInjuryStage.Serious)) return null;

            Map.Map map = pawn.Map!;
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(pawn);
            if (comfy.Includes(TemperatureInjuryUtility.AmbientTemperature(pawn)))
            {
                return new Job(SafeTemperatureJobDefOf.Wait_SafeTemperature) { expiryInterval = WaitTicks };
            }

            Room? here = map.roomTracker.RoomAt(pawn.Position);
            IReadOnlyList<Room> rooms = map.roomTracker.Rooms;
            tmpCandidates.Clear();
            for (int i = 0; i < rooms.Count; i++)
            {
                Room room = rooms[i];
                if (room == here || room.Cells.Count == 0 || !comfy.Includes(room.Temperature)) continue;
                tmpCandidates.Add(room);
            }
            tmpCandidates.Sort((a, b) => NearestDistSq(a, pawn.Position).CompareTo(NearestDistSq(b, pawn.Position)));
            try
            {
                for (int i = 0; i < tmpCandidates.Count; i++)
                {
                    if (TryFindCellIn(pawn, map, tmpCandidates[i], out IntVec3 cell))
                    {
                        return new Job(SafeTemperatureJobDefOf.GotoSafeTemperature, cell);
                    }
                }
                return null;
            }
            finally
            {
                tmpCandidates.Clear();
            }
        }

        private static int NearestDistSq(Room room, IntVec3 from)
        {
            int best = int.MaxValue;
            IReadOnlyList<IntVec3> cells = room.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                int dx = cells[i].x - from.x, dz = cells[i].z - from.z;
                int d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return best;
        }

        private static bool TryFindCellIn(Pawn pawn, Map.Map map, Room room, out IntVec3 cell)
        {
            IReadOnlyList<IntVec3> cells = room.Cells;
            for (int i = 0; i < CellTries; i++)
            {
                IntVec3 c = cells[Rand.Range(0, cells.Count)];
                if (!GenGrid.Standable(c, map)) continue;
                if (!Reachability.CanReach(pawn, c, PathEndMode.OnCell)) continue;
                cell = c;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }
    }

    /// <summary>Walks onto the chosen cell (RimWorld: <c>GotoSafeTemperature</c>, a plain goto).</summary>
    public sealed class JobDriver_GotoSafeTemperature : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
        }
    }

    /// <summary>Stands still until the job expires (RimWorld: <c>Wait_SafeTemperature</c>, a
    /// <c>JobDriver_Wait</c> re-thought on expiry).</summary>
    public sealed class JobDriver_WaitSafeTemperature : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_General.Wait(JobGiver_SeekSafeTemperature.WaitTicks);
        }
    }

    [DefOf]
    public static class SafeTemperatureJobDefOf
    {
        public static JobDef GotoSafeTemperature = null!;
        public static JobDef Wait_SafeTemperature = null!;
    }
}
