using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Strips the roof off every cell of <see cref="AreaManager.NoRoof"/> that still has one (RimWorld:
    /// <c>RimWorld.WorkGiver_RemoveRoof</c>). Cell-scanning, the same shape as <see cref="WorkGiver_BuildRoof"/>.
    ///
    /// <para/><b>Not ported: <c>GetPriority</c> and <c>Prioritized</c>.</b> RimWorld's own giver ranks its
    /// candidate cells by a continuous priority — never a hard refusal, only an ordering: a cell right next to
    /// something that still holds a roof up scores far below everything else (so it is picked dead last, not
    /// skipped), and among the rest, fewer already-roofed neighbours score higher (so an edge of the patch
    /// clears before its middle). This port's <see cref="AI.WorkGiverScanUtility"/> has no ranking hook at
    /// all — every cell-scanning giver it drives, <see cref="WorkGiver_GrowerSow"/> and
    /// <see cref="WorkGiver_BuildRoof"/> included, picks the nearest reachable candidate with a job on it,
    /// full stop — and <see cref="Work.WorkGiver_Scanner.Prioritized"/> is itself already unread by that scan
    /// (every shipped giver's override of it is inert), so overriding either here would do nothing a test
    /// could tell apart from not overriding it. The one thing this drops in practice: a patch of NoRoof cells
    /// clears in whatever order is nearest to the worker, not from its edges inward.
    /// </summary>
    public sealed class WorkGiver_RemoveRoof : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (IntVec3 c in map.areaManager.NoRoof.ActiveCells) yield return c;
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (!map.areaManager.NoRoof[c]) return false;
            if (!map.roofGrid.Roofed(c)) return false;
            if (!Reachability.CanReach(pawn, c, PathEndMode)) return false;
            return map.reservationManager.CanReserve(pawn, c);
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            new Job(RoofJobDefOf.RemoveRoof, c, c);
    }
}
