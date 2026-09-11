using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a patient — anyone tendable of the doctor's own faction, or a prisoner that faction holds — and
    /// treats their most urgent hediff (RimWorld: <c>RimWorld.WorkGiver_Tend</c>). One class backs both the
    /// <c>DoctorTendEmergency</c> and <c>DoctorTend</c> content <c>WorkGiverDef</c>s, exactly as RimWorld's own
    /// single <c>WorkGiver_Tend</c> class does for both — <see cref="WorkGiverDef.emergency"/> alone tells them
    /// apart, and <see cref="TendUtility.NeedsEmergencyTend"/> (see that method's own doc) sorts every patient
    /// into exactly one of the two, so a bleeding patient is only ever a candidate for the emergency giver and
    /// an ordinary wound only ever a candidate for the ordinary one — never both, and never neither.
    /// <para/>
    /// <b>Self-tend:</b> a pawn is never its own patient here, matching RimWorld's own <c>WorkGiver_Tend</c>
    /// (which refuses <c>t == pawn</c> the same way) — RimWorld's separate "auto-tend" mechanic, letting a
    /// solitary colonist patch themselves up at reduced quality when no one else can reach them, is a distinct
    /// code path this port does not have at all yet. A lone injured pawn with nobody else around therefore
    /// goes untended through this giver until either a colonist reaches them or that mechanic is built — see
    /// this module's report.
    /// </summary>
    public sealed class WorkGiver_Tend : WorkGiver_Scanner
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
            if (patient.Dead || !patient.Spawned || patient.Map != pawn.Map) return false;
            if (!DoctorUtility.IsCaredForBy(pawn, patient)) return false;
            if (!TendUtility.HasAnythingToTend(patient)) return false;
            if (TendUtility.NeedsEmergencyTend(patient) != def.emergency) return false;
            if (!Reachability.CanReach(pawn, patient, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, patient);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(JobDefOf.TendPatient, thing);
    }
}
