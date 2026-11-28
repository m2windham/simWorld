using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a downed patient of the rescuer's own faction (or a prisoner that faction holds) and carries them
    /// to a bed found for the patient (<see cref="BedFor"/>; RimWorld: a trim of <c>RimWorld.WorkGiver_Rescuer</c> — real RimWorld
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
            // One exception, the household's own bed: a patient lying in a bed that is not its household's is
            // carried home when home has a bed for it (below).
            // ... unless it is a baby and home has gone cold with nothing to burn (BabySafetyUtility): then it is
            // brought to safety, as RimWorld moves a baby out of a crib whose room is too cold.
            // So is anybody downed in a home the cold has got into (exposure4 lane): BedFor then finds a bed out of
            // the cold, or none and there is no job.
            if (patient.InBed() && Building.DwellingUtility.IsOwnHomeBed(patient, patient.CurrentBed()!)
                && !BabySafetyUtility.NeedsMovingForTemperature(patient)
                && (BabySafetyUtility.IsBabyOnMap(patient) || RestUtility.IsSafeTemperatureBed(patient, patient.CurrentBed()!))) return false;
            if (!DoctorUtility.IsCaredForBy(pawn, patient)) return false;
            if (BedFor(patient, pawn) == null) return false;
            if (!Reachability.CanReach(pawn, patient, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, patient);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn patient)) return null;
            Thing? bed = BedFor(patient, pawn);
            return bed != null ? new Job(JobDefOf.Rescue, thing, bed) : null;
        }

        /// <summary>
        /// The bed <paramref name="rescuer"/> carries <paramref name="patient"/> to: found for the patient, not the
        /// rescuer (RimWorld: <c>RestUtility.FindBedFor(pawn2, pawn, ...)</c>, patient as sleeper, rescuer as
        /// traveler). The patient's own household's bed first, as RimWorld's search tries the owned bed first and
        /// as <see cref="JobGiver_GetRest"/> already does for a pawn's own sleep; otherwise the nearest bed that is
        /// no other household's. A patient already in some bed is only moved to its own household's: from one
        /// stranger's bed to another is no rescue.
        /// <para/>
        /// It matters most for a baby (<see cref="Pawns.LifeStageDef.alwaysDowned"/>), which is "downed" from
        /// birth and only ever moves by rescue. Found for the rescuer, a baby picked up by a neighbour went to the
        /// neighbour's house; the hearth that would have kept it alive is lit in its own (<see
        /// cref="Building.SettlementClimate"/>), for it, and it froze in a room heated for robed adults or in none.
        /// <para/>
        /// A baby is also brought to safety from the cold (RimWorld: anyone carries a baby somewhere safe, and moves it
        /// when its crib's room is too cold; see <see cref="BabySafetyUtility"/>). Home still comes first while home is
        /// safe for it. Lying in the cold (<see cref="BabySafetyUtility.NeedsMovingForTemperature"/>), it goes to the
        /// nearest bed that is safe: from the ground, from the unheated dormitory bed of a household still waiting
        /// for a house, or from its own bed when its house has gone cold with an empty hearth. Lying safe somewhere,
        /// it is not carried home to an unsafe house. The 600-day 40/201 arc lost babies both ways: left in the
        /// dormitory for days, and left at home beside a hearth with no fuel in a hard frost.
        /// </summary>
        internal static Thing? BedFor(Pawn patient, Pawn rescuer)
        {
            Thing? home = Building.DwellingUtility.FindDwellingBedFor(patient, rescuer);
            if (!BabySafetyUtility.IsBabyOnMap(patient)) return PatientBedFor(patient, rescuer, home);

            if (home != null && BabySafetyUtility.IsSafeBedFor(patient, home)) return home;
            if (BabySafetyUtility.NeedsMovingForTemperature(patient))
            {
                Thing? safe = BabySafetyUtility.SafeBedFor(patient, rescuer);
                if (safe != null) return safe;
            }
            else if (patient.InBed())
            {
                return null;
            }
            // Nowhere safer: a bed at home, or, with none, the nearest that is nobody's, as before; never from one
            // bed of its own house to another.
            Thing? lying = patient.CurrentBed();
            if (lying != null && Building.DwellingUtility.IsOwnHomeBed(patient, lying)) return null;
            if (home != null) return home;
            return lying != null ? null : RestUtility.FindBedFor(patient, rescuer);
        }

        /// <summary>
        /// The bed for a downed patient who is not a baby: home while home is safe from the cold, as before; else a bed
        /// that is (<see cref="RestUtility.IsSafeTemperatureBed"/>), RimWorld's <c>Danger.None</c> pass of
        /// <c>FindBedFor</c>; else home anyway, or with no home the nearest. A patient already lying somewhere safe is
        /// not carried to an unsafe home, nor moved from one bed of its own house to another. Exposure4 lane: in the
        /// 600-day 40/201 arc children downed by the frost in the dormitory were carried home to houses a quake had
        /// opened, at -8 C, and died there.
        /// </summary>
        private static Thing? PatientBedFor(Pawn patient, Pawn rescuer, Thing? home)
        {
            if (home != null && RestUtility.IsSafeTemperatureBed(patient, home)) return home;
            Thing? lying = patient.CurrentBed();
            if (lying != null && RestUtility.IsSafeTemperatureBed(patient, lying)) return null;
            Thing? safe = RestUtility.FindBedFor(patient, rescuer, safeOnly: true);
            if (safe != null) return safe;
            if (lying != null && Building.DwellingUtility.IsOwnHomeBed(patient, lying)) return null;
            if (home != null) return home;
            return lying != null ? null : RestUtility.FindBedFor(patient, rescuer, safeOnly: false);
        }
    }
}
