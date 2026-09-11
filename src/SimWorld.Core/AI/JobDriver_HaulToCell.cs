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
    /// <b>Carrying is modelled abstractly</b>, the same way <see cref="Building.JobDriver_HaulToBuildingSite"/>
    /// and <see cref="JobDriver_Warden_Feed"/> already do it: no carry-tracker exists in this codebase, so
    /// the source stack's <see cref="Thing.stackCount"/> drops the moment the pawn reaches it, with nothing
    /// visibly following the pawn to the stockpile in between.
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
                int carried = Math.Min(thing.stackCount, room);
                if (carried <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                thing.stackCount -= carried;
                if (thing.stackCount <= 0) thing.Destroy(DestroyMode.Vanish);
                job.count = carried;
            });

            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            yield return Toils_General.Do(() =>
            {
                Map.Map map = pawn.Map!;
                IntVec3 cell = job.GetTarget(TargetIndex.B).Cell;
                Thing carriedThing = job.GetTarget(TargetIndex.A).Thing!; // reference survives its own Destroy() above
                ThingDef def = carriedThing.def;

                Thing? existing = HaulAIUtility.ExistingStackAt(map, cell);
                if (existing != null && existing.def == def)
                {
                    existing.stackCount += job.count;
                }
                else
                {
                    Thing dropped = ThingMaker.MakeThing(def, carriedThing.Stuff);
                    dropped.stackCount = job.count;
                    GenSpawn.Spawn(dropped, cell, map);
                }
            });
        }
    }
}
