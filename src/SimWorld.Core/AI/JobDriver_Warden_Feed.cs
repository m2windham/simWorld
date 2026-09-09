using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries one food item to a downed prisoner and feeds them directly (RimWorld: a trim of
    /// <c>RimWorld.JobDriver_FeedPatient</c> — no <c>ThingOwner</c>/carry-tracker exists in this codebase yet,
    /// so "carrying" is modelled the same abstract way <see cref="SimWorld.Building.JobDriver_HaulToBuildingSite"/>
    /// already does: the pawn visits the food, then the prisoner, and the food's stack simply drops at the
    /// end rather than visibly following the pawn in between). Target A is the food, target B the prisoner.
    /// </summary>
    public sealed class JobDriver_Warden_Feed : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat, matching <see cref="JobDriver_Ingest"/>'s own.</summary>
        public const int FeedDurationTicks = 200;

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

            Toil gotoFood = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoFood.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoFood;

            Toil gotoPrisoner = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoPrisoner.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoPrisoner;

            Toil feed = Toils_General.Wait(FeedDurationTicks);
            feed.FailOnDespawnedOrNull(TargetIndex.A);
            feed.FailOnDespawnedOrNull(TargetIndex.B);
            yield return feed;

            yield return Toils_General.Do(() =>
            {
                Thing? food = job.GetTarget(TargetIndex.A).Thing;
                if (!(job.GetTarget(TargetIndex.B).Thing is Pawn prisoner) || prisoner.Destroyed || !prisoner.Spawned) return;
                if (food == null || food.Destroyed) return;
                float nutrition = food.def.ingestible?.nutrition ?? 0f;
                prisoner.needs.food?.Eat(nutrition);
                food.stackCount -= 1;
                if (food.stackCount <= 0) food.Destroy();
            });
        }
    }
}
