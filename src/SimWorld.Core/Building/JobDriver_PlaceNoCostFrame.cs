using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>The job a blueprint that costs nothing is turned into its frame by (RimWorld:
    /// <c>JobDefOf.PlaceNoCostFrame</c>), in its own <c>[DefOf]</c> class rather than appended to the shared
    /// <see cref="JobDefOf"/> (CLAUDE.md).</summary>
    [DefOf]
    public static class NoCostFrameJobDefOf
    {
        public static JobDef PlaceNoCostFrame = null!;
    }

    /// <summary>
    /// Walks to a blueprint whose building has no cost list and puts its frame down (RimWorld:
    /// <c>JobDriver_PlaceNoCostFrame</c>, issued by <c>WorkGiver_ConstructDeliverResourcesToBlueprints</c> when
    /// <c>TotalMaterialCost</c> is empty). A blueprint otherwise becomes a frame only when the first material
    /// is delivered to it (<see cref="JobDriver_HaulToBuildingSite"/>), so without this a building that needs
    /// no material was never built at all: every workbench this port ships without a cost list (the stove,
    /// the stonecutter's, tailor's and butcher's tables, the smithy) stayed a blueprint for good. The frame
    /// is then finished by <see cref="WorkGiver_ConstructFinishFrame"/> like any other.
    /// </summary>
    public sealed class JobDriver_PlaceNoCostFrame : JobDriver
    {
        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                // Somebody stepped onto the blueprint of an impassable building on the way: its frame would
                // wall them in, as JobDriver_HaulToBuildingSite already refuses.
                if (job.GetTarget(TargetIndex.A).Thing is not Blueprint blueprint || !blueprint.Spawned
                    || GenConstruct.FirstBlockingPawn(blueprint, pawn) != null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                blueprint.ReplaceWithFrame();
                GenConstruct.StepOffUnwalkableCell(pawn);
            });
        }
    }
}
