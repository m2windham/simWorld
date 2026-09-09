using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Sows every empty, eligible cell of every active <see cref="Zone_Growing"/> on the map (RimWorld:
    /// <c>RimWorld.WorkGiver_GrowerSow</c>). Cell-scanning (<c>GrowerSow</c>'s <c>scanCells</c> in
    /// <c>WorkGivers.xml</c>), not thing-scanning — an empty, sowable cell has no Thing of its own to find.
    /// </summary>
    public sealed class WorkGiver_GrowerSow : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;

            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!(zones[i] is Zone_Growing growing) || !growing.allowSow || growing.plantDefToGrow == null) continue;
                for (int c = 0; c < growing.Cells.Count; c++) yield return growing.Cells[c];
            }
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (!(map.zoneManager.ZoneAt(cell) is Zone_Growing growing) || !growing.allowSow) return false;

            ThingDef? toSow = growing.plantDefToGrow;
            if (toSow?.plant == null) return false;
            if (map.terrainGrid.TerrainAt(cell).fertility < toSow.plant.sowMinFertility) return false;

            // Nothing already growing, planned, or built here.
            if (map.thingGrid.CellContains(cell, ThingCategory.Plant)) return false;
            if (map.thingGrid.CellContains(cell, ThingCategory.Blueprint)) return false;
            if (map.thingGrid.CellContains(cell, ThingCategory.Frame)) return false;
            if (map.edificeGrid[cell] != null) return false;
            if (!GenGrid.Standable(cell, map)) return false;

            if (!Reachability.CanReach(pawn, cell, PathEndMode)) return false;
            return map.reservationManager.CanReserve(pawn, cell);
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) =>
            new Job(BuildingJobDefOf.Sow, cell);
    }
}
