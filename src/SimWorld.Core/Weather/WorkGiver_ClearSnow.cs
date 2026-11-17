using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Weather
{
    /// <summary>
    /// Sends someone to shovel every cell of the snow-clear area that has real snow on it (RimWorld:
    /// <c>RimWorld.WorkGiver_ClearSnow</c>, a Cleaning-type giver). Cell-scanning over
    /// <see cref="Building.AreaManager.SnowClear"/>, which the player paints
    /// (<see cref="Map.View.MapCommands.SetSnowClearArea"/>) — RimWorld has no automatic snow-clear area and
    /// neither does this port.
    /// </summary>
    public sealed class WorkGiver_ClearSnow : WorkGiver_Scanner
    {
        /// <summary>Snow shallower than this is not worth a job (RimWorld: the <c>GetSnowDepth &lt; 0.2f</c>
        /// test in <c>HasJobOnCell</c> — the same threshold as <see cref="Building.PlantUtility.SnowAllowsPlanting"/>).</summary>
        public const float MinClearDepth = 0.2f;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            return map.snowGrid.TotalDepth <= 0f || map.areaManager.SnowClear.TrueCount == 0;
        }

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (IntVec3 c in map.areaManager.SnowClear.ActiveCells) yield return c;
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (map.snowGrid.GetDepth(cell) < MinClearDepth) return false;
            if (!Reachability.CanReach(pawn, cell, PathEndMode)) return false;
            return map.reservationManager.CanReserve(pawn, cell);
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) =>
            new Job(SnowJobDefOf.ClearSnow, cell);
    }
}
