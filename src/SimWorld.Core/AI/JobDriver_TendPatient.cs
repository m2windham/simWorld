using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Stats;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a patient and tends their most urgent hediff (RimWorld: a trim of
    /// <c>RimWorld.JobDriver_TendPatient</c> — RimWorld's own driver can also carry a medicine item there and
    /// consume it for a quality bonus; no medicine <c>ThingDef</c> exists in this port's content yet (see this
    /// module's report), so every tend this pass ships runs at <see cref="TendUtility.MaxQualityNoMedicine"/>'s
    /// ceiling. Quality itself still comes from the doctor's own skill exactly as RimWorld's does — through
    /// <see cref="StatDefOf.MedicalTendQuality"/> (<c>work.stats</c>), not a second skill-to-quality curve
    /// invented here).
    /// </summary>
    public sealed class JobDriver_TendPatient : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat, matching <see cref="JobDriver_Warden_Feed"/>'s own.</summary>
        public const int TendDurationTicks = 200;

        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoPatient = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoPatient.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoPatient;

            Toil tend = Toils_General.Wait(TendDurationTicks);
            tend.FailOnDespawnedOrNull(TargetIndex.A);
            yield return tend;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn patient) || patient.Destroyed || !patient.Spawned || patient.Dead) return;
                TendUtility.DoTend(patient, pawn.GetStatValue(StatDefOf.MedicalTendQuality), TendUtility.MaxQualityNoMedicine);
            });
        }
    }
}
