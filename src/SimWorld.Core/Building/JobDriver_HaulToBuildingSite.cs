using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Carries one resource stack to a Blueprint or Frame and delivers it (RimWorld:
    /// <c>RimWorld.JobDriver_HaulToContainer</c>, specialised to a construction site — no
    /// <c>ThingOwner</c>/carry-tracker exists in this codebase yet, so "carrying" is modelled abstractly:
    /// the source stack's <see cref="Thing.stackCount"/> drops the moment the pawn reaches it, with nothing
    /// visibly following the pawn to the site — see this module's report). Target A is the resource stack,
    /// target B the Blueprint/Frame.
    /// </summary>
    public sealed class JobDriver_HaulToBuildingSite : JobDriver
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

            Toil gotoResource = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            gotoResource.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoResource;

            yield return Toils_General.Do(() =>
            {
                Thing resource = job.GetTarget(TargetIndex.A).Thing!;
                Thing site = job.GetTarget(TargetIndex.B).Thing!;
                int needed = StillNeededOn(site, resource.def);
                int carried = Math.Min(resource.stackCount, needed);
                if (carried <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                resource.stackCount -= carried;
                if (resource.stackCount <= 0) resource.Destroy(DestroyMode.Vanish);
                job.count = carried;
            });

            Toil gotoSite = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return gotoSite;

            yield return Toils_General.Do(() =>
            {
                Thing site = job.GetTarget(TargetIndex.B).Thing!;
                Frame frame = site as Frame ?? ((Blueprint)site).ReplaceWithFrame();
                ThingDef material = job.GetTarget(TargetIndex.A).Thing!.def;
                int stillNeeded = frame.MaterialStillNeeded(material);
                frame.AddMaterial(material, Math.Min(job.count, stillNeeded));
            });
        }

        private static int StillNeededOn(Thing site, ThingDef material)
        {
            return site switch
            {
                Frame frame => frame.MaterialStillNeeded(material),
                Blueprint blueprint => blueprint.EntityToBuild.CostListCountFor(material),
                _ => 0,
            };
        }
    }
}
