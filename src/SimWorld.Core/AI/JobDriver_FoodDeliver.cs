using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries one meal to a prisoner and puts it down where they are (RimWorld:
    /// <c>RimWorld.JobDriver_FoodDeliver</c>). Target A is the food, target B the prisoner.
    /// <para/>
    /// <b>Delivering is not feeding, and the difference is the point.</b>
    /// <see cref="JobDriver_Warden_Feed"/> ends by putting nutrition into a downed prisoner who cannot feed
    /// themselves; this one ends by leaving a meal on the floor for a prisoner who can. Nothing here touches
    /// <c>Need_Food</c>: the prisoner's own <see cref="JobGiver_GetFood"/> does that afterwards, now finding
    /// the nearest food in the room it is standing in. See <see cref="WorkGiver_Warden_DeliverFood"/>'s doc
    /// for why that is the whole value of this job in a port that confines nobody.
    /// <para/>
    /// <b>Carrying is modelled abstractly</b>, exactly as <see cref="JobDriver_HaulToCell"/>,
    /// <see cref="JobDriver_Warden_Feed"/> and <see cref="Building.JobDriver_HaulToBuildingSite"/> already do
    /// it: no carry-tracker exists in this codebase, so the meal leaves the source cell when the pawn reaches
    /// it and nothing visibly follows the pawn in between. The whole-Thing-versus-split distinction is
    /// <see cref="JobDriver_HaulToCell"/>'s, kept for the same reason: a one-meal stack travels as the real
    /// object (so a <see cref="Things.CompQuality"/> or a damaged item arrives as itself), and only a
    /// genuinely stackable split is rebuilt by def at the far end. An interrupted delivery drops the meal at
    /// the pawn's feet (<see cref="Notify_Ending"/>) rather than destroying it.
    /// </summary>
    public sealed class JobDriver_FoodDeliver : JobDriver
    {
        /// <summary>How much of the chosen stack one delivery moves. One meal, not the whole pile: RimWorld
        /// sizes the carry to what the prisoner still needs, and a single unit is this port's honest version
        /// of that — <see cref="JobDriver_Warden_Feed"/> already spends exactly one unit to feed a prisoner,
        /// so the two warden jobs move food at the same granularity. Not a RimWorld literal.</summary>
        public const int MealsPerDelivery = 1;

        /// <summary>The Thing itself while it is off the map, for the whole-stack case; null when this job is
        /// carrying a split-off count instead (or carrying nothing yet). See
        /// <see cref="JobDriver_HaulToCell.Notify_Ending"/> for the same field doing the same job.</summary>
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

            Toil gotoFood = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            gotoFood.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoFood;

            yield return Toils_General.Do(() =>
            {
                Thing? food = job.GetTarget(TargetIndex.A).Thing;
                if (food == null || food.Destroyed || !food.Spawned)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int taken = food.stackCount < MealsPerDelivery ? food.stackCount : MealsPerDelivery;
                if (taken <= 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.count = taken;
                if (taken >= food.stackCount)
                {
                    carried = food;
                    food.DeSpawn();
                }
                else
                {
                    food.stackCount -= taken;
                }
            });

            Toil gotoPrisoner = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoPrisoner.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoPrisoner;

            yield return Toils_General.Do(() =>
            {
                Map.Map? map = pawn.Map;
                Thing? sourceThing = job.GetTarget(TargetIndex.A).Thing;
                if (map == null || sourceThing == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Where the prisoner actually is now, not where it was when the job was issued: a prisoner
                // that can walk does, and the point of this job is that the meal ends up in its room.
                Pawn? prisoner = job.GetTarget(TargetIndex.B).Thing as Pawn;
                IntVec3 at = prisoner != null && prisoner.Spawned ? prisoner.Position : pawn.Position;

                IntVec3 cell = DropCellNear(map, at, sourceThing.def);
                if (!cell.IsValid) cell = pawn.Position;

                Thing? existing = HaulAIUtility.ExistingStackAt(map, cell);
                Thing? travelling = carried;
                carried = null;

                if (existing != null && existing.def == sourceThing.def)
                {
                    existing.stackCount += job.count;
                    travelling?.Destroy(DestroyMode.Vanish);
                    return;
                }

                if (travelling != null)
                {
                    GenSpawn.Spawn(travelling, cell, map);
                    return;
                }

                Thing dropped = ThingMaker.MakeThing(sourceThing.def, sourceThing.Stuff);
                dropped.stackCount = job.count;
                GenSpawn.Spawn(dropped, cell, map);
            });
        }

        /// <summary>
        /// The cell to put the meal down on: the prisoner's own cell when it has room for the delivery,
        /// otherwise the nearest adjacent cell that does and is in the same <see cref="Room"/> — a meal put
        /// down on the far side of a wall is not in the prisoner's room, which is the one thing this whole
        /// job exists to achieve. <see cref="IntVec3.Invalid"/> when nowhere adjacent qualifies.
        /// </summary>
        private static IntVec3 DropCellNear(Map.Map map, IntVec3 at, ThingDef def)
        {
            if (GenGrid.InBounds(at, map) && HaulAIUtility.CapacityAt(map, at, def) > 0) return at;

            Room? room = map.roomTracker.RoomAt(at);
            for (int d = 0; d < GenAdj.AdjacentCells.Length; d++)
            {
                IntVec3 candidate = at + GenAdj.AdjacentCells[d];
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Walkable(candidate, map)) continue;
                if (room != null && !ReferenceEquals(map.roomTracker.RoomAt(candidate), room)) continue;
                if (HaulAIUtility.CapacityAt(map, candidate, def) > 0) return candidate;
            }
            return IntVec3.Invalid;
        }
    }
}
