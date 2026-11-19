using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries one item stack to a stockpile cell and merges or drops it there (RimWorld:
    /// <c>RimWorld.JobDriver_HaulToCell</c>). Target A is the item, target B the destination cell.
    /// <b>Carrying a split-off count is modelled abstractly</b>, the same way <see cref="JobDriver_Warden_Feed"/>
    /// does it: the goods leave the source cell the moment the pawn reaches it, and only a number on the job
    /// follows the pawn to the stockpile.
    /// <para/>
    /// <b>The whole Thing travels, when the whole Thing is what moves.</b> This driver used to destroy the
    /// source and build a fresh Thing of the same def at the destination. That is lossless only for goods
    /// with no state of their own beyond def, stuff and count — and this port already has three kinds that
    /// have more: a <see cref="Things.CompQuality"/> item (a masterwork weapon arrived as an ordinary one), a
    /// damaged item (hit points reset to full), and now a <see cref="Corpse"/>, which would have arrived as
    /// an empty body with nobody inside it. So when the carry takes the entire stack, the real object is
    /// taken off the map and put back down at the destination; only a partial stack — which by definition is
    /// a stackable good, where one unit really is interchangeable with another — is still split off by count.
    /// <para/>
    /// <b>The whole Thing is held in <see cref="Pawns.Pawn_CarryTracker"/></b>, not in this driver: no
    /// <see cref="JobDriver"/> in this port is Scribed, so a Thing held in a driver field was in no save at all,
    /// and the job's target A and the hauler's reservation — both saved, both pointing at it — loaded as null.
    /// The carry tracker deep-saves what it holds, which registers its load id, so both references re-link. An
    /// interrupted whole-stack carry drops the goods at the pawn's feet, as anything left in the carry tracker is
    /// (<see cref="Pawn_JobTracker.EndCurrentJob"/>). <b>A split-off count does not:</b> it exists only as a
    /// number on the job, so an interrupted partial carry loses it — the defect
    /// <see cref="Building.JobDriver_HaulToBuildingSite"/> had until it carried the split-off piece through
    /// the tracker too. Splitting here as well would close that, at the cost of a new Thing (and thing id) per
    /// partial haul.
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

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            yield return Toils_General.Do(() =>
            {
                Thing thing = job.GetTarget(TargetIndex.A).Thing!;
                Map.Map map = pawn.Map!;
                IntVec3 cell = job.GetTarget(TargetIndex.B).Cell;

                int room = HaulAIUtility.CapacityAt(map, cell, thing.def);
                int carriedCount = Math.Min(thing.stackCount, room);
                if (carriedCount <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.count = carriedCount;
                if (carriedCount >= thing.stackCount)
                {
                    // The whole stack: TryStartCarry despawns this very Thing into the pawn's hands.
                    if (pawn.carryTracker.TryStartCarry(thing, carriedCount) <= 0)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                }
                else
                {
                    thing.stackCount -= carriedCount;
                }
            });

            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            yield return Toils_General.Do(() =>
            {
                Map.Map map = pawn.Map!;
                IntVec3 cell = job.GetTarget(TargetIndex.B).Cell;
                Thing sourceThing = job.GetTarget(TargetIndex.A).Thing!; // still referenced whether it moved or was split
                ThingDef def = sourceThing.def;

                Thing? existing = HaulAIUtility.ExistingStackAt(map, cell);
                Thing? travelling = pawn.carryTracker.CarriedThing;

                if (existing != null && existing.def == def)
                {
                    existing.stackCount += job.count;
                    // The merged-into stack is the survivor; a whole Thing that travelled here has been
                    // absorbed into it. (Only reachable for a stackable def: a stackLimit-1 Thing on the
                    // destination cell leaves CapacityAt at 0, and the pickup toil above refuses the job.)
                    if (travelling != null) pawn.carryTracker.DestroyCarriedThing();
                    return;
                }

                if (travelling != null)
                {
                    pawn.carryTracker.TryDropCarriedThing(cell, out _);
                    return;
                }

                Thing dropped = ThingMaker.MakeThing(def, sourceThing.Stuff);
                dropped.stackCount = job.count;
                GenSpawn.Spawn(dropped, cell, map);
            });
        }
    }
}
