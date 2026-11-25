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
    /// <para/>
    /// <b>The grower clears its own field.</b> A cell holding a plant that is not the zone's crop (wild grass, a
    /// bush, a tree the field was drawn over) is work here too: the job is to cut that plant, as RimWorld's
    /// <c>WorkGiver_GrowerSow.JobOnCell</c> hands back a <c>CutPlant</c> job for a sowing blocker (recalled from
    /// RimWorld's source, not checked here). Without it a field drawn over wild growth waited on the settlement's
    /// PlantCutting work (<see cref="AI.WorkGiver_PlantsCut"/>), which an edict for the fields does not name, so
    /// the blocked cells were no work of Break the sod's hands (third playtest).
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

            // RimWorld: WorkGiver_GrowerSow refuses a grower below the plant's sowMinSkill (healroot needs 8).
            if (toSow.plant.sowMinSkill > 0 && !CanSow(pawn, toSow)) return false;

            // Nothing planned or built here.
            if (map.thingGrid.CellContains(cell, ThingCategory.Blueprint)) return false;
            if (map.thingGrid.CellContains(cell, ThingCategory.Frame)) return false;
            if (map.edificeGrid[cell] != null) return false;

            // A plant already here: the crop itself is done with, anything else is in the way and is cut first.
            Plant? blocker = BlockerAt(map, cell, toSow);
            if (blocker != null)
            {
                return Reachability.CanReach(pawn, blocker, PathEndMode.Touch) && map.reservationManager.CanReserve(pawn, blocker);
            }
            if (map.thingGrid.CellContains(cell, ThingCategory.Plant)) return false;
            if (!GenGrid.Standable(cell, map)) return false;
            if (!PlantUtility.SnowAllowsPlanting(cell, map)) return false;

            if (!Reachability.CanReach(pawn, cell, PathEndMode)) return false;
            return map.reservationManager.CanReserve(pawn, cell);
        }

        /// <summary>Whether <paramref name="pawn"/> is skilled enough to sow <paramref name="plant"/>.</summary>
        public static bool CanSow(Pawn pawn, ThingDef plant)
        {
            int min = plant.plant?.sowMinSkill ?? 0;
            if (min <= 0) return true;
            return (pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0) >= min;
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            ThingDef? toSow = map != null && map.zoneManager.ZoneAt(cell) is Zone_Growing growing ? growing.plantDefToGrow : null;
            Plant? blocker = map != null && toSow != null ? BlockerAt(map, cell, toSow) : null;
            return blocker != null ? new Job(PlantCuttingJobDefOf.CutPlant, blocker) : new Job(BuildingJobDefOf.Sow, cell);
        }

        /// <summary>A plant on <paramref name="cell"/> that is not <paramref name="crop"/>, so the cell cannot be
        /// sown while it stands; null when the cell is clear or already holds the crop.</summary>
        private static Plant? BlockerAt(Map.Map map, IntVec3 cell, ThingDef crop)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant plant && plant.Spawned && !ReferenceEquals(plant.def, crop)) return plant;
            }
            return null;
        }
    }
}
