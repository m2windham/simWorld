using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Harvests every reachable, unreserved, harvestable-now <see cref="Plant"/> on the map (RimWorld:
    /// <c>RimWorld.WorkGiver_GrowerHarvest</c>). Thing-scanning, like <c>WorkGiver_Miner</c>. <b>Deviation
    /// from RimWorld, deliberately kept:</b> not restricted to plants inside a growing zone — RimWorld's own
    /// harvester scans every harvestable plant on the map, zones only control where sowing happens.
    /// </summary>
    public sealed class WorkGiver_GrowerHarvest : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)) yield return t;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Plant plant) || !plant.Spawned || !plant.HarvestableNow) return false;
            if (!Reachability.CanReach(pawn, plant, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, plant);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(BuildingJobDefOf.Harvest, thing);
    }
}
