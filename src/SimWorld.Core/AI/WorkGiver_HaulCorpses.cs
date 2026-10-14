using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries a <see cref="Corpse"/> to the best place there is for it (RimWorld: <c>RimWorld.WorkGiver_HaulCorpses</c>,
    /// which is its <c>WorkGiver_Haul</c> with the scan narrowed to corpses). <c>HaulCorpses</c>'s <c>giverClass</c>
    /// in <c>WorkGivers.xml</c> — until the corpses module it was the placeholder <see cref="WorkGiver_Pending"/>,
    /// because no <c>Corpse</c> class existed for it to find.
    /// <para/>
    /// <b>Where a body goes is RimWorld's rule, not a nearest cell.</b> <see cref="HaulAIUtility.HaulToStorageJob"/>
    /// asks <see cref="StoreUtility.TryFindBestBetterStorageFor"/>, which compares every place that would take the
    /// body by storage priority: a free grave that accepts it (<see cref="StoragePriority.Important"/>) beats a
    /// dumping stockpile (<see cref="StoragePriority.Normal"/>), and a body already lying in the dump is moved out
    /// of it the day a grave is free for it. A body that nothing wants stays where it fell, which is the honest
    /// answer for a settlement with no grave and no dump, and the reason one of them gets built
    /// (<see cref="GraveyardInitiative"/>).
    /// <para/>
    /// <b>Bodies are this giver's alone.</b> <see cref="WorkGiver_Haul"/> (the general hauler) leaves them to it:
    /// it only knows stockpile cells, and a hauler who laid a citizen's body in the dump while a grave stood
    /// empty would defeat the priority above. <c>HaulCorpses</c> sits above <c>HaulGeneral</c> in
    /// <c>priorityInType</c> so a settlement with a backlog of loose wood still clears its dead first.
    /// <para/>
    /// It does not derive from <see cref="WorkGiver_Haul"/> the way RimWorld's does only because that class
    /// is <c>sealed</c> here, and unsealing it is an edit to a file other lanes may hold (CLAUDE.md). The
    /// shared logic lives in <see cref="HaulAIUtility"/> either way, which is the half worth sharing.
    /// </summary>
    public sealed class WorkGiver_HaulCorpses : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        /// <summary>
        /// Every body on the map, from the map's own corpse group (RimWorld: <c>ThingRequestGroup.Corpse</c>) —
        /// the few things that are bodies, not every item to be sorted through for them.
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> bodies = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < bodies.Count; i++) yield return bodies[i];
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Corpse) || !thing.Spawned || thing.Destroyed) return false;
            if (!Reachability.CanReach(pawn, thing, PathEndMode)) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            return StoreUtility.TryFindBestBetterStorageFor(thing, pawn, StoreUtility.CurrentStoragePriorityOf(thing), out _, out _);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Corpse)) return null;
            return HaulAIUtility.HaulToStorageJob(pawn, thing);
        }
    }
}
