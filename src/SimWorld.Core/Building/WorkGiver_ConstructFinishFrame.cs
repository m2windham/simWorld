using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>Works a fully-materialed Frame until it finishes (RimWorld: <c>RimWorld.WorkGiver_ConstructFinishFrame</c>).</summary>
    public sealed class WorkGiver_ConstructFinishFrame : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)) yield return t;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Frame frame) || !frame.Spawned || !frame.MaterialsFullySatisfied()) return false;
            if (!Reachability.CanReach(pawn, frame, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, frame);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(JobDefOf.ConstructFinishFrame, thing);
    }
}
