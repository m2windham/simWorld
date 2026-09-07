using System.Collections.Generic;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a food Thing, walks to it, chews for a while, then eats it (RimWorld: <c>RimWorld.JobDriver_Ingest</c>,
    /// trimmed — no animal-food-in-bowl, no table/social-eating, no toxic-fallout check).
    /// </summary>
    public sealed class JobDriver_Ingest : JobDriver
    {
        /// <summary>RimWorld's real chew duration varies by food and pawn; pinned to a constant here rather
        /// than guessed, per this module's own tests (see <c>AITests</c>).</summary>
        public const int IngestDurationTicks = 200;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoFood = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            yield return gotoFood;

            Toil chew = Toils_General.Wait(IngestDurationTicks);
            chew.FailOnDespawnedOrNull(TargetIndex.A);
            yield return chew;

            yield return Toils_General.Do(() =>
            {
                Thing? food = job.GetTarget(TargetIndex.A).Thing;
                if (food == null || food.Destroyed) return;
                float nutrition = food.def.ingestible?.nutrition ?? 0f;
                pawn.needs.food?.Eat(nutrition);
                food.stackCount -= 1;
                if (food.stackCount <= 0) food.Destroy();
            });
        }
    }
}
