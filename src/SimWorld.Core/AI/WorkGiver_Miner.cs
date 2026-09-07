using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Mines reachable, unreserved mineable edifices (RimWorld: <c>RimWorld.WorkGiver_Miner</c>). The one
    /// concrete <see cref="WorkGiver_Scanner"/> this pass wires to real content (<c>Mine</c>'s <c>giverClass</c>
    /// in <c>WorkGivers.xml</c>) — every other <c>WorkGiverDef</c> keeps its <c>WorkGiver_Pending</c> default,
    /// which is fine: <see cref="JobGiver_Work"/> just finds no job from those and moves on.
    /// </summary>
    public sealed class WorkGiver_Miner : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].def.mineable) yield return buildings[i];
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.def.mineable || thing.Destroyed || !thing.Spawned) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, thing);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) => new Job(JobDefOf.Mine, thing);
    }
}
