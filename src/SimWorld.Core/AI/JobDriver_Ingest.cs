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

                // RimWorld eats a *sitting*, not a unit: Toils_Ingest takes job.count units in one chew, and
                // job.count is FoodUtility.WillIngestStackCountOf. This port ate one unit per job, which for
                // every raw foodstuff it ships (0.05 nutrition against a 1.0 stomach) meant a citizen could
                // not out-eat its own hunger no matter how much food was in front of it. See that method for
                // the whole argument.
                int count = SimWorld.Crafting.FoodUtility.WillIngestStackCountOf(pawn, food.def);
                if (count > food.stackCount) count = food.stackCount;
                if (count < 1) count = 1;

                float nutrition = food.def.ingestible?.nutrition ?? 0f;
                pawn.needs.food?.Eat(nutrition * count);
                food.stackCount -= count;
                if (food.stackCount <= 0) food.Destroy();
            });
        }
    }
}
