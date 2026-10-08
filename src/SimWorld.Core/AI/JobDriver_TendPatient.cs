using System;
using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a patient and tends their most urgent hediff, carrying medicine there first when
    /// <see cref="WorkGiver_Tend"/> found any (RimWorld: a trim of <c>RimWorld.JobDriver_TendPatient</c> —
    /// RimWorld's own driver carries medicine through <c>Pawn_InventoryTracker</c> and can loop back for more
    /// mid-visit; this port has no general inventory, so target B travels in the doctor's hands, through
    /// <see cref="Pawn_CarryTracker"/> as <see cref="JobDriver_FoodDeliver"/>'s meal does: picked up by
    /// <see cref="Toils_Haul.StartCarryThing"/>, one unit consumed per visit rather than RimWorld's variable
    /// <c>Medicine.GetMedicineCountToFullyHeal</c> amount). Quality itself comes from
    /// <see cref="TendUtility.CalculateBaseTendQuality"/> — doctor skill, medicine potency and the patient's
    /// own bed, all folded in exactly once, at the moment the tend actually lands.
    /// <para/>
    /// <b>Medicine in hand is never lost.</b> Whatever the doctor is still holding when the job ends — cut
    /// short on the way, or carried to a patient who turned out to have nothing left to tend — is put down at
    /// the doctor's feet by <see cref="Pawn_JobTracker.EndCurrentJob"/>. This driver used to keep the unit in a
    /// field of its own, hand it to the tend and forget it, so a tend that found nothing to treat left the
    /// medicine off the map for good, and a save taken mid-carry lost it.
    /// <para/>
    /// A doctor already holding the medicine goes straight to the patient (see
    /// <see cref="Toils_Haul.IsCarryingTarget"/>): a loaded job rebuilds its driver and starts from the first toil.
    /// </summary>
    public sealed class JobDriver_TendPatient : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat, matching <see cref="JobDriver_Warden_Feed"/>'s own.</summary>
        public const int TendDurationTicks = 200;

        /// <summary>Units of medicine one tend spends, whatever the stack size or the patient's actual wounds
        /// call for — this port's own flat simplification (see this class's own doc).</summary>
        public const int MedicinePerTend = 1;

        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))) return false;
            LocalTargetInfo medicineTarget = job.GetTarget(TargetIndex.B);
            if (medicineTarget.HasThing && !pawn.Map.reservationManager.CanReserve(pawn, medicineTarget)) return false;
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            bool hasMedicine = job.GetTarget(TargetIndex.B).HasThing;
            if (hasMedicine)
            {
                yield return Toils_Reserve.Reserve(TargetIndex.B);
                yield return Toils_Haul.GotoThingToCarry(TargetIndex.B, PathEndMode.ClosestTouch);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B, medicine => Math.Min(medicine.stackCount, MedicinePerTend));
            }

            Toil gotoPatient = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoPatient.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoPatient;

            Toil tend = Toils_General.Wait(TendDurationTicks);
            tend.FailOnDespawnedOrNull(TargetIndex.A);
            yield return tend;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn patient) || patient.Destroyed || !patient.Spawned || patient.Dead) return;
                // What is in hand is the medicine this job picked up, and only that: TendUtility spends it when
                // the tend lands, and anything it does not spend is put down when this job ends.
                Thing? medicine = Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.B) ? pawn.carryTracker.CarriedThing : null;
                TendUtility.DoTendWithMedicine(pawn, patient, medicine);
            });
        }
    }
}
