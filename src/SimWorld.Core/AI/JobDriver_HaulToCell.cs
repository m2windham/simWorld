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
    /// the goods leave the source cell the moment the pawn reaches it, with nothing visibly following the
    /// pawn to the stockpile in between.
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
    /// An interrupted carry now drops the goods at the pawn's feet (<see cref="Notify_Ending"/>) instead of
    /// destroying them. A save taken mid-carry still loses them, as it always did: no
    /// <see cref="JobDriver"/> in this port is Scribed, so nothing re-links a Thing that is off the map when
    /// the save is written. A real carry tracker closes that window and is not in this port.
    /// </summary>
    public sealed class JobDriver_HaulToCell : JobDriver
    {
        /// <summary>The Thing itself while it is off the map, for the whole-stack case; null when this job is
        /// carrying a split-off count instead (or carrying nothing yet).</summary>
        private Thing? carried;

        public override bool TryMakePreToilReservations()
        {
            if (pawn.Map == null) return false;
            return pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        public override void Notify_Ending()
        {
            base.Notify_Ending();
            Thing? thing = carried;
            carried = null;
            if (thing == null || thing.Destroyed || thing.Spawned) return;
            if (pawn.Map == null || !pawn.Spawned) return;
            GenSpawn.Spawn(thing, pawn.Position, pawn.Map);
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
                    carried = thing;
                    thing.DeSpawn();
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
                Thing? travelling = carried;
                carried = null;

                if (existing != null && existing.def == def)
                {
                    existing.stackCount += job.count;
                    // The merged-into stack is the survivor; a whole Thing that travelled here has been
                    // absorbed into it. (Only reachable for a stackable def: a stackLimit-1 Thing on the
                    // destination cell leaves CapacityAt at 0, and the pickup toil above refuses the job.)
                    travelling?.Destroy(DestroyMode.Vanish);
                    return;
                }

                if (travelling != null)
                {
                    GenSpawn.Spawn(travelling, cell, map);
                    return;
                }

                Thing dropped = ThingMaker.MakeThing(def, sourceThing.Stuff);
                dropped.stackCount = job.count;
                GenSpawn.Spawn(dropped, cell, map);
            });
        }
    }
}
