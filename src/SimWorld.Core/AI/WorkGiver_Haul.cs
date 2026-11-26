using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a spawned, not-yet-stored haulable item and carries it to the nearest stockpile cell with room
    /// for it (RimWorld: <c>RimWorld.WorkGiver_Haul</c>, trimmed to this port's single storage kind — see
    /// <see cref="HaulAIUtility"/>'s own remarks for what "haulable" and "nowhere to put it" mean here).
    /// <c>HaulGeneral</c>'s <c>giverClass</c> in <c>WorkGivers.xml</c>.
    /// </summary>
    public sealed class WorkGiver_Haul : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            // Only what NeedsHauling keeps (RimWorld walks ListerHaulables, the haulables not already in valid
            // storage, not every haulable thing): the side-effect-free tests HasJobOnThing starts with, so the
            // survivors keep their order and the scan takes the same stack.
            IReadOnlyList<Thing> haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < haulables.Count; i++)
            {
                if (NeedsHauling(haulables[i])) yield return haulables[i];
            }
        }

        private static bool NeedsHauling(Thing thing) =>
            thing.Spawned && thing.stackCount > 0 && !HaulAIUtility.IsInValidStorage(thing);

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!NeedsHauling(thing)) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            return HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!HaulAIUtility.TryFindBestStockpileCell(pawn, thing, out IntVec3 cell)) return null;
            return new Job(JobDefOf.HaulToCell, thing, cell) { haulMode = HaulMode.ToCellStorage };
        }
    }
}
