using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Filth
{
    /// <summary>
    /// Finds a piece of filth inside the region colonists actually clean and sends someone to scrub it
    /// (RimWorld: <c>RimWorld.WorkGiver_CleanFilth</c>). This is the worker behind the <c>CleanFilth</c>
    /// <see cref="WorkGiverDef"/>, which shipped in <c>WorkGivers.xml</c> pointing at
    /// <see cref="WorkGiver_Pending"/> — a whole work type (<c>Cleaning</c>) with nothing it could ever do,
    /// because no <see cref="Filth"/> class existed to target.
    /// <para/>
    /// <b>What bounds it</b> is <see cref="CleaningBounds"/>; that class carries the argument and the
    /// translation, because it is the decision that keeps this job from running forever.
    /// <para/>
    /// <b>Trim — no <c>ListerFilthInHomeArea</c>.</b> RimWorld keeps a second, pre-filtered list of the filth
    /// inside the home area so this scan does not walk every pile on the map. This port filters
    /// <see cref="FilthMaker.AllFilthOn"/> — the map's existing <c>ThingRequestGroup.Filth</c> index — at the
    /// point of use instead. That keeps a field off <c>Map.Map</c> (which another lane is in this batch), and
    /// the cost is the same order as the other scanning givers already shipped here, all of which walk a
    /// whole <c>listerThings</c> group per scan. If filth counts ever make that hurt, the cached lister is
    /// the fix and it is a drop-in.
    /// <para/>
    /// <b>Trim — one pile per job.</b> RimWorld's giver packs up to 15 nearby pieces of filth from the same
    /// room into <c>job.targetQueueA</c> so the pawn cleans a whole patch without re-deciding. This issues one
    /// job per pile and lets <see cref="WorkGiverScanUtility"/> pick the next nearest immediately afterwards,
    /// which lands the pawn in the same place by the same route with more job churn and no queue-draining
    /// toils to get wrong. Noted as a deviation rather than pretended away.
    /// </summary>
    public sealed class WorkGiver_CleanFilth : WorkGiver_Scanner
    {
        /// <summary>
        /// How long a pile must sit untouched before anyone will clean it (RimWorld:
        /// <c>WorkGiver_CleanFilth.MinTicksSinceThickened</c>, 600). It stops colonists chasing filth that is
        /// still actively being made — the doorway somebody is walking through right now — which would have
        /// them scrub the same cell forever. RimWorld's own literal, recalled rather than sourced; the test
        /// pins the behaviour (freshly made filth is not immediately work, and it becomes work later) instead
        /// of this number.
        /// </summary>
        public const int MinTicksSinceThickened = 600;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            return map.listerThings.ThingsInGroup(Map.ThingRequestGroup.Filth).Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Filth f in FilthMaker.AllFilthOn(map)) yield return f;
        }

        /// <summary>The whole eligibility test, in RimWorld's own order: it is filth, it is somewhere we
        /// clean, it has settled, we can get to it, and nobody else has claimed it.</summary>
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Filth filth) || !filth.Spawned || filth.Destroyed) return false;
            if (!CleaningBounds.IsCleanable(filth)) return false;
            if (filth.TicksSinceThickened < MinTicksSinceThickened) return false;
            if (!Reachability.CanReach(pawn, filth, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, filth);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(CleaningJobDefOf.Clean, thing);
    }
}
