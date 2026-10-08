using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries one item stack to a stockpile cell and merges or drops it there (RimWorld:
    /// <c>RimWorld.JobDriver_HaulToCell</c>). Target A is the item — and, once it has been picked up, the Thing
    /// in the pawn's hands — target B the destination cell.
    /// <para/>
    /// <b>The goods are carried, not deleted and re-created.</b> Pickup moves them off the stack into the pawn's
    /// <see cref="Pawns.Pawn_CarryTracker"/> (RimWorld: <c>Toils_Haul.StartCarryThing</c>, here
    /// <see cref="Toils_Haul.StartCarryThing"/>); arrival puts them down on the cell, onto a stack of the same
    /// def already there if there is one (<c>Toils_Haul.PlaceHauledThingInCell</c>, here
    /// <see cref="Toils_Haul.TryPlaceCarriedThing"/>). Anything still in hand when the job ends — interrupted
    /// before arrival, or more than the stack at the destination could take — is put down at the pawn's feet by
    /// <see cref="Pawn_JobTracker.EndCurrentJob"/>, exactly as RimWorld's job cleanup does.
    /// <para/>
    /// This driver used to hold what it picked up in a field of its own. That was lossless for a whole stack
    /// (the real Thing was kept and respawned by <c>Notify_Ending</c>) and lost for a partial one: a pile bigger
    /// than the destination cell could hold was split, the split-off count existed only as a number on the job,
    /// and a hauler downed on the way took it with them — 80 logs hauled to a 75-log cell became 5. Nothing was
    /// saved either way: no <see cref="JobDriver"/> in this port is Scribed, so a save taken mid-carry lost the
    /// goods. They are now the carry tracker's, which is deep-saved with the pawn.
    /// <para/>
    /// <b>The whole Thing travels, when the whole Thing is what moves.</b> That is
    /// <see cref="Pawns.Pawn_CarryTracker.TryStartCarry"/>'s rule: a carry that takes the entire stack moves the
    /// real object, so a <see cref="Things.CompQuality"/> item, a damaged item or a <see cref="Corpse"/> arrives
    /// as itself; only a partial stack — which by definition is a stackable good, where one unit really is
    /// interchangeable with another — is split off by count.
    /// <para/>
    /// <b>A hauler already holding the goods goes straight to the cell.</b> A loaded job rebuilds its driver and
    /// starts again from the first toil (see <see cref="JobDriver"/>), so the fetch and pickup toils skip
    /// themselves when target A is already what the pawn is carrying (<see cref="Toils_Haul.IsCarryingTarget"/>).
    /// </summary>
    public sealed class JobDriver_HaulToCell : JobDriver
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

            yield return Toils_Haul.GotoThingToCarry(TargetIndex.A, PathEndMode.ClosestTouch);

            // As much as the destination can take right now, which is read at pickup rather than when the job
            // was given: a stack that has grown since leaves less room, and no room ends the job unpicked-up.
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, thing =>
                Math.Min(thing.stackCount, HaulAIUtility.CapacityAt(pawn.Map!, job.GetTarget(TargetIndex.B).Cell, thing.def)));

            Toil carryToCell = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            carryToCell.FailOn(() => !Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A));
            yield return carryToCell;

            yield return Toils_General.Do(() =>
            {
                if (!Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Toils_Haul.TryPlaceCarriedThing(pawn, job.GetTarget(TargetIndex.B).Cell);
            });
        }
    }
}
