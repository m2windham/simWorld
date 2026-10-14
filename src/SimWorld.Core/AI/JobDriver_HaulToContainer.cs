using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries a Thing to a building and puts it <i>inside</i> (RimWorld: <c>Verse.AI.JobDriver_HaulToContainer</c>
    /// — which RimWorld also uses to carry materials to a construction site; here that is
    /// <see cref="Building.JobDriver_HaulToBuildingSite"/>, so this is the storage half only). Target A is the
    /// Thing — and, once picked up, the Thing in the hauler's hands — target B the <see cref="Building_Casket"/>
    /// that will take it. The first use is a grave and a body.
    /// <para/>
    /// <b>The same carry as every other haul.</b> Pickup goes through the pawn's
    /// <see cref="Pawns.Pawn_CarryTracker"/> (<see cref="Toils_Haul.StartCarryThing"/>), so a hauler who is
    /// interrupted on the way puts the body down at their feet rather than losing it, and a hauler caught
    /// mid-carry by a save comes back holding it (<see cref="Toils_Haul.IsCarryingTarget"/> skips the toils that
    /// were already done). The last toil is what is new: not setting the load down on a cell but handing it to the
    /// container (<see cref="Building_Casket.TryAcceptCarried"/>), in one step, so the Thing is held in exactly
    /// one place afterwards — the hands are empty and the grave is full, or nothing moved.
    /// <para/>
    /// <b>The container is reserved for this hauler alone</b> (<c>Toils_Reserve.Reserve(B)</c>, one claimant), so
    /// two haulers cannot be sent to fill one grave with two bodies; <see cref="Building_Casket.Accepts"/> is
    /// asked again at the moment of deposit regardless, because a reservation is not a lock on what the grave
    /// will take (a player can change its settings while the hauler walks).
    /// </summary>
    public sealed class JobDriver_HaulToContainer : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        private Building_Casket? Container => job.GetTarget(TargetIndex.B).Thing as Building_Casket;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            yield return Toils_Haul.GotoThingToCarry(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, thing => thing.stackCount);

            // RimWorld: Toils_Haul.CarryHauledThingToContainer — walk to the container (Touch), and give up if it
            // is gone or will no longer take what is being carried.
            Toil carryToContainer = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);
            carryToContainer.FailOn(() => !Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A));
            carryToContainer.FailOn(() =>
            {
                Building_Casket? container = Container;
                Thing? carried = pawn.carryTracker.CarriedThing;
                return container == null || container.Destroyed || carried == null || !container.Accepts(carried);
            });
            yield return carryToContainer;

            // RimWorld: Toils_Haul.DepositHauledThingInContainer.
            yield return Toils_General.Do(() =>
            {
                Building_Casket? container = Container;
                if (container == null || !container.TryAcceptCarried(pawn.carryTracker))
                {
                    EndJobWith(JobCondition.Incompletable); // what is still in hand is put down by the job's end
                }
            });
        }
    }
}
