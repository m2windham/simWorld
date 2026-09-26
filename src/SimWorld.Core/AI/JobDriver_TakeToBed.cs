using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries a downed patient to a bed (RimWorld: a trim of <c>RimWorld.JobDriver_TakeToBed</c> — this
    /// driver does not carry through <see cref="Pawns.Pawn_CarryTracker"/>, so "carrying" is modelled the same
    /// abstract way <see cref="JobDriver_Warden_Feed"/> already does for food: the rescuer visits the downed
    /// patient, then walks to the bed, and the patient's own <see cref="Thing.Position"/> jumps there the
    /// moment the rescuer arrives — nothing visibly follows the rescuer in between). Target A is the patient,
    /// target B the bed.
    /// </summary>
    public sealed class JobDriver_TakeToBed : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            Toil gotoPatient = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoPatient.FailOnDespawnedOrNull(TargetIndex.B);
            gotoPatient.FailOn(() => job.GetTarget(TargetIndex.A).Thing is Pawn p && p.Dead);
            yield return gotoPatient;

            Toil gotoBed = Toils_Bed.GotoBed(TargetIndex.B);
            gotoBed.FailOnDespawnedOrNull(TargetIndex.A);
            gotoBed.FailOn(() => job.GetTarget(TargetIndex.A).Thing is Pawn p && p.Dead);
            yield return gotoBed;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn patient) || patient.Destroyed || !patient.Spawned || patient.Dead) return;
                Thing? bed = job.GetTarget(TargetIndex.B).Thing;
                if (bed == null || bed.Destroyed || !bed.Spawned) return;
                // Laid in the bed, not on whichever of its cells: RimWorld's TuckIntoBed drops the patient at
                // the bed's sleeping slot.
                patient.Position = bed.GetSleepingSlotPos();
            });
        }
    }
}
