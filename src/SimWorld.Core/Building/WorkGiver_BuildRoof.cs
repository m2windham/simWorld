using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Builds a roof over every cell of <see cref="AreaManager.BuildRoof"/> that does not have one yet
    /// (RimWorld: <c>RimWorld.WorkGiver_BuildRoof</c>). Cell-scanning (<c>BuildRoof</c>'s own <c>scanCells</c>
    /// in <c>WorkGivers_Roof.xml</c>): a roof cell with nothing built yet has no Thing of its own to scan for,
    /// the same shape <see cref="WorkGiver_GrowerSow"/> already established for a cell-target job.
    /// <b>Not ported:</b> RimWorld's own <c>c.IsForbidden(pawn)</c> check — this port has no designation or
    /// forbid layer at all (the same gap every other translated <c>WorkGiver</c> in this codebase already
    /// records; see <see cref="AI.WorkGiver_Miner"/>'s own doc for the fullest account of it).
    /// </summary>
    public sealed class WorkGiver_BuildRoof : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (IntVec3 c in map.areaManager.BuildRoof.ActiveCells) yield return c;
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (!map.areaManager.BuildRoof[c]) return false;
            if (map.roofGrid.Roofed(c)) return false;
            if (!map.reservationManager.CanReserve(pawn, c)) return false;

            bool reachableDirect = Reachability.CanReach(pawn, c, PathEndMode);
            if (!reachableDirect && BuildingToTouchToBeAbleToBuildRoof(c, pawn) == null) return false;

            if (!RoofCollapseUtility.WithinRangeOfRoofHolder(c, map)) return false;
            if (!RoofCollapseUtility.ConnectedToRoofHolder(c, map)) return false;

            Thing? blocker = RoofUtility.FirstBlockingThing(c, map);
            return blocker == null || RoofUtility.CanHandleBlockingThing(blocker, pawn, forced);
        }

        /// <summary>
        /// The edifice at <paramref name="c"/> a pawn should touch instead, when <paramref name="c"/> itself
        /// cannot be stood on (RimWorld: <c>WorkGiver_BuildRoof.BuildingToTouchToBeAbleToBuildRoof</c>) — a
        /// roof cell that is itself a wall or door cell rather than open floor. Null (no substitute needed)
        /// whenever <paramref name="c"/> is standable at all, or carries no edifice to touch instead.
        /// </summary>
        private static Thing? BuildingToTouchToBeAbleToBuildRoof(IntVec3 c, Pawn pawn)
        {
            if (GenGrid.Standable(c, pawn.Map!)) return null;
            Thing? edifice = pawn.Map!.edificeGrid[c];
            if (edifice == null) return null;
            return Reachability.CanReach(pawn, edifice, PathEndMode.Touch) ? edifice : null;
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Map.Map map = pawn.Map!;
            Thing? blocker = RoofUtility.FirstBlockingThing(c, map);
            if (blocker != null) return RoofUtility.HandleBlockingThingJob(blocker, pawn, forced);

            LocalTargetInfo targetB = c;
            if (!Reachability.CanReach(pawn, c, PathEndMode))
            {
                Thing? touch = BuildingToTouchToBeAbleToBuildRoof(c, pawn);
                if (touch != null) targetB = touch;
            }
            return new Job(RoofJobDefOf.BuildRoof, c, targetB);
        }
    }
}
