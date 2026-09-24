using System;
using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Carries one resource stack to a Blueprint or Frame and delivers it (RimWorld:
    /// <c>RimWorld.JobDriver_HaulToContainer</c>, specialised to a construction site). Target A is the resource
    /// stack — and, once it has been picked up, the Thing in the pawn's hands — target B the Blueprint/Frame.
    /// <para/>
    /// <b>The materials are carried, not deleted and re-created.</b> Pickup moves them off the stack into the
    /// pawn's <see cref="Pawns.Pawn_CarryTracker"/> (RimWorld: <c>Toils_Haul.StartCarryThing</c>); delivery
    /// moves what the site still needs out of the pawn's hands into the Frame (RimWorld:
    /// <c>Toils_Haul.DepositHauledThingInContainer</c>). Anything still in hand when the job ends — because it
    /// was interrupted before delivery, or because the Frame needed less than was carried — is put down at the
    /// pawn's feet by <see cref="Pawn_JobTracker.EndCurrentJob"/>, exactly as RimWorld's job cleanup does. This
    /// driver used to take the materials off the stack at pickup and hold them nowhere, so a hauler pulled away
    /// before delivery deleted them.
    /// <para/>
    /// <b>A hauler already holding the materials goes straight to the site.</b> No <see cref="JobDriver"/> in
    /// this port is saved: a loaded job rebuilds its driver and starts again from the first toil (see that
    /// class's doc). RimWorld's own driver opens with a jump past the pickup when the pawn is already carrying
    /// its haulable (<c>Toils_Jump.JumpIf(..., pawn.IsCarryingThing(ThingToCarry))</c>); this port has no jump
    /// toil, so the fetch and pickup toils below skip themselves in the same case instead.
    /// </summary>
    public sealed class JobDriver_HaulToBuildingSite : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        /// <summary>True once target A is the Thing in this pawn's hands — after pickup, and after a load that
        /// caught the pawn mid-carry.</summary>
        private bool CarryingTargetA
        {
            get
            {
                Thing? carried = pawn.carryTracker.CarriedThing;
                return carried != null && ReferenceEquals(carried, job.GetTarget(TargetIndex.A).Thing);
            }
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            // Toils_Goto.GotoThing's own shape, less its fail-on-despawned for A: a stack already in hand is off
            // the map by definition, and is exactly the case where there is nothing to walk to.
            var gotoResource = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            gotoResource.initAction = () =>
            {
                if (CarryingTargetA) return;
                pawn.pather.StartPath(job.GetTarget(TargetIndex.A), PathEndMode.ClosestTouch);
            };
            gotoResource.FailOn(() =>
            {
                if (CarryingTargetA) return false;
                Thing? resource = job.GetTarget(TargetIndex.A).Thing;
                return resource == null || resource.Destroyed || !resource.Spawned || pawn.pather.Failed;
            });
            gotoResource.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoResource;

            yield return Toils_General.Do(() =>
            {
                if (CarryingTargetA) return;

                Thing resource = job.GetTarget(TargetIndex.A).Thing!;
                Thing site = job.GetTarget(TargetIndex.B).Thing!;
                int wanted = Math.Min(resource.stackCount, StillNeededOn(site, resource.def));
                int taken = wanted > 0 ? pawn.carryTracker.TryStartCarry(resource, wanted) : 0;
                if (taken <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // RimWorld's StartCarryThing re-points the haulable target at what is now in hand: from here on
                // "target A" is the carried Thing, not the stack it came off.
                job.SetTarget(TargetIndex.A, pawn.carryTracker.CarriedThing);
                job.count = taken;
            });

            Toil gotoSite = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoSite.FailOn(() => !CarryingTargetA);
            yield return gotoSite;

            yield return Toils_General.Do(() =>
            {
                Thing? carried = pawn.carryTracker.CarriedThing;
                if (carried == null || !CarryingTargetA)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Thing site = job.GetTarget(TargetIndex.B).Thing!;
                Frame frame = site as Frame ?? ((Blueprint)site).ReplaceWithFrame();
                int delivered = Math.Min(carried.stackCount, frame.MaterialStillNeeded(carried.def));
                if (delivered <= 0) return; // nothing wanted after all: the job ends and puts the load down

                frame.AddMaterial(carried.def, delivered);
                carried.stackCount -= delivered;
                if (carried.stackCount <= 0) pawn.carryTracker.DestroyCarriedThing();
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
