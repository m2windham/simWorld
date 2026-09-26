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
    /// mid-visit; this port has no general inventory, so target B travels the same "abstract carry" way
    /// <see cref="JobDriver_FoodDeliver"/>'s and <see cref="JobDriver_Warden_Feed"/>'s single-unit deliveries
    /// already do — despawned off the map the moment it is picked up, respawned at the doctor's feet if the
    /// job ends before it is spent, one unit consumed per visit rather than RimWorld's variable
    /// <c>Medicine.GetMedicineCountToFullyHeal</c> amount). Quality itself comes from
    /// <see cref="TendUtility.CalculateBaseTendQuality"/> — doctor skill, medicine potency and the patient's
    /// own bed, all folded in exactly once, at the moment the tend actually lands.
    /// </summary>
    public sealed class JobDriver_TendPatient : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat, matching <see cref="JobDriver_Warden_Feed"/>'s own.</summary>
        public const int TendDurationTicks = 200;

        /// <summary>Units of medicine one tend spends, whatever the stack size or the patient's actual wounds
        /// call for — this port's own flat simplification (see this class's own doc).</summary>
        public const int MedicinePerTend = 1;

        /// <summary>The medicine in hand, off the map, between pickup and the tend that spends it — null once
        /// spent, or when this job carries none at all. Mirrors <see cref="JobDriver_FoodDeliver"/>'s own
        /// <c>carried</c> field for the same reason: something has to remember it long enough to give it back
        /// if the job is cut short.</summary>
        private Thing? carriedMedicine;

        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))) return false;
            LocalTargetInfo medicineTarget = job.GetTarget(TargetIndex.B);
            if (medicineTarget.HasThing && !pawn.Map.reservationManager.CanReserve(pawn, medicineTarget)) return false;
            return true;
        }

        public override void Notify_Ending()
        {
            base.Notify_Ending();
            Thing? thing = carriedMedicine;
            carriedMedicine = null;
            if (thing == null || thing.Destroyed || thing.Spawned) return;
            if (pawn.Map == null || !pawn.Spawned) return;
            GenSpawn.Spawn(thing, pawn.Position, pawn.Map);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            bool hasMedicine = job.GetTarget(TargetIndex.B).HasThing;
            if (hasMedicine)
            {
                yield return Toils_Reserve.Reserve(TargetIndex.B);

                Toil gotoMedicine = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
                gotoMedicine.FailOnDespawnedOrNull(TargetIndex.B);
                yield return gotoMedicine;

                yield return Toils_General.Do(() =>
                {
                    Thing? medicine = job.GetTarget(TargetIndex.B).Thing;
                    if (medicine == null || medicine.Destroyed || !medicine.Spawned)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (medicine.stackCount <= MedicinePerTend)
                    {
                        carriedMedicine = medicine;
                        medicine.DeSpawn();
                    }
                    else
                    {
                        medicine.stackCount -= MedicinePerTend;
                        Thing split = ThingMaker.MakeThing(medicine.def, medicine.Stuff);
                        split.stackCount = MedicinePerTend;
                        carriedMedicine = split;
                    }
                });
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
                Thing? medicine = carriedMedicine;
                carriedMedicine = null;
                TendUtility.DoTendWithMedicine(pawn, patient, medicine);
            });
        }
    }
}
