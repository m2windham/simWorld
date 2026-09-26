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
    /// <para/>
    /// <b>Fixed: a rescued patient is no longer re-rescued.</b> <see cref="HasJobOnThing"/> did not check
    /// whether the patient was already in a bed, so once one rescue finished, every other doctor (and the same
    /// one again) kept seeing the same patient as a live target and re-issuing the identical carry — a no-op
    /// job that reserved the patient and a bed for nothing. Harmless-looking while sleep was the only thing
    /// that could interrupt an in-progress rescue; with emergency work now waking sleepers
    /// (<c>JobDriver_LayDown</c>), a settlement with one downed, already-bedded patient could wake every doctor
    /// in the settlement, every <c>LookForOtherJobsIntervalTicks</c>, for a job that does nothing. Ported
    /// RimWorld's own guard, <see cref="RestUtility.InBed"/> (RimWorld: <c>Pawn.InBed()</c>).
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
            // RimWorld: WorkGiver_RescueDowned.HasJobOnThing (1.0 decompile) checks !pawn2.InBed() right
            // alongside Downed — a patient already carried to a bed has nowhere further to go, and without
            // this a rescuer just re-picks the same already-rescued patient as its next target forever. This
            // was silent while sleep was the only thing that could pre-empt a rescue in progress; emergency
            // work now waking sleepers (JobDriver_LayDown) means the rescuer is woken to "rescue" someone who
            // is already in bed, over and over, every LookForOtherJobsIntervalTicks.
            if (patient.InBed()) return false;
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
