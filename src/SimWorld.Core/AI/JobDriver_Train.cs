using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a tamed animal and runs one training session on whatever <see cref="TrainableDef"/> it is
    /// next eligible for (RimWorld: <c>RimWorld.JobDriver_Train</c>). <b>Deviation:</b> RimWorld's own
    /// training attempts can themselves fail; this port always succeeds the session once completed — the
    /// mechanic this module is pinning is decay (<see cref="Pawn_TrainingTracker.TrainingTrackerTick"/>), not
    /// a second probability roll alongside taming's.
    /// </summary>
    public sealed class JobDriver_Train : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat.</summary>
        public const int TrainDurationTicks = 200;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoAnimal = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return gotoAnimal;

            Toil interact = Toils_General.Wait(TrainDurationTicks);
            interact.FailOnDespawnedOrNull(TargetIndex.A);
            yield return interact;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn animal) || animal.Destroyed || !animal.Spawned) return;
                TrainableDef? next = animal.training.NextToTrain();
                if (next == null) return;
                animal.training.Train(next, pawn);
            });
        }
    }
}
