using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a downed patient of the rescuer's own faction (or a prisoner that faction holds) and carries them
    /// to the nearest reachable bed (RimWorld: a trim of <c>RimWorld.WorkGiver_Rescuer</c> — real RimWorld
    /// scores medical-versus-ordinary beds and a prisoner/temperature preference across the whole map; this
    /// port's own <see cref="RestUtility.FindBedFor"/> already trims that down to "nearest reachable,
    /// unclaimed bed" — the same trim <see cref="JobGiver_GetRest"/> uses for a pawn fetching its own rest —
    /// reused here rather than a second bed search built for this giver alone.
    /// <para/>
    /// <b>No bed anywhere on the map:</b> <see cref="RestUtility.FindBedFor"/> returns null and this giver
    /// simply produces no job, the honest answer rather than inventing a ground-cell fallback RimWorld's own
    /// rescue job does not have either. A downed pawn with no bed to be carried to stays exactly where they
    /// fell — <see cref="WorkGiver_Tend"/> and <see cref="WorkGiver_FeedPatient"/> both still reach them there
    /// (neither needs a bed), so they are tended and fed in place for as long as no bed exists, never rescued.
    /// </summary>
    public sealed class WorkGiver_RescueDowned : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++) yield return pawns[i];
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn patient) || ReferenceEquals(patient, pawn)) return false;
            if (patient.Dead || !patient.Downed || !patient.Spawned || patient.Map != pawn.Map) return false;
            if (!DoctorUtility.IsCaredForBy(pawn, patient)) return false;
            if (RestUtility.FindBedFor(pawn) == null) return false;
            if (!Reachability.CanReach(pawn, patient, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, patient);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Thing? bed = RestUtility.FindBedFor(pawn);
            return bed != null ? new Job(JobDefOf.Rescue, thing, bed) : null;
        }
    }
}
