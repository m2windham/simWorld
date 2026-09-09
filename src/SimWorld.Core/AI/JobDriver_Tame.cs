using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a wild animal and makes one taming attempt (RimWorld: <c>RimWorld.JobDriver_InteractAnimal</c>
    /// via its "Tame" interaction, folded into one driver since no other animal interaction exists yet). One
    /// roll per completed job via <see cref="TameUtility.TryTame"/> — see that method's own doc for the
    /// chance curve and the failure consequence.
    /// </summary>
    public sealed class JobDriver_Tame : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat (this module's own tests pin
        /// the taming trend, never this literal).</summary>
        public const int TameDurationTicks = 200;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoAnimal = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return gotoAnimal;

            Toil interact = Toils_General.Wait(TameDurationTicks);
            interact.FailOnDespawnedOrNull(TargetIndex.A);
            yield return interact;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn animal) || animal.Destroyed || !animal.Spawned) return;
                TameUtility.TryTame(animal, pawn);
            });
        }
    }
}
