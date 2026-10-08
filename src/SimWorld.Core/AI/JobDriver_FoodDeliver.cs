using System;
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
    /// <c>RimWorld.JobDriver_FoodDeliver</c>). Target A is the food — and, once it has been picked up, the
    /// Thing in the warden's hands — target B the prisoner.
    /// <para/>
    /// <b>Delivering is not feeding, and the difference is the point.</b>
    /// <see cref="JobDriver_Warden_Feed"/> ends by putting nutrition into a downed prisoner who cannot feed
    /// themselves; this one ends by leaving a meal on the floor for a prisoner who can. Nothing here touches
    /// <c>Need_Food</c>: the prisoner's own <see cref="JobGiver_GetFood"/> does that afterwards, now finding
    /// the nearest food in the room it is standing in. See <see cref="WorkGiver_Warden_DeliverFood"/>'s doc
    /// for why that is the whole value of this job in a port that confines nobody.
    /// <para/>
    /// <b>The meal is carried, not deleted and re-created.</b> Pickup moves it off its stack into the warden's
    /// <see cref="Pawns.Pawn_CarryTracker"/> (RimWorld: <c>Toils_Ingest.PickupIngestible</c>, here
    /// <see cref="Toils_Haul.StartCarryThing"/>, which re-points target A at what is in hand just as RimWorld's
    /// does); arrival puts it down beside the prisoner (RimWorld's closing
    /// <c>carryTracker.TryDropCarriedThing</c>, here <see cref="Toils_Haul.TryPlaceCarriedThing"/>). A delivery
    /// cut short — the warden downed, ordered away, the prisoner gone — leaves the meal at the warden's feet
    /// (<see cref="Pawn_JobTracker.EndCurrentJob"/>, RimWorld's <c>CleanupCurrentJob</c>) instead of destroying
    /// it, which matters most for this job: a meal that vanishes between the larder and the cell leaves a
    /// prisoner hungry with nothing in the game saying why.
    /// <para/>
    /// This driver used to hold what it picked up in a field of its own. That was lossless for a one-meal stack
    /// (the real Thing was kept and respawned by <c>Notify_Ending</c>) and lost for a meal split off a bigger
    /// stack — the usual case, since a larder is a pile — and, with no <see cref="JobDriver"/> in this port
    /// Scribed, for any save taken mid-carry. Both are closed by moving the meal into the carry tracker, which
    /// is deep-saved with the pawn. The whole-Thing-versus-split distinction is
    /// <see cref="Pawns.Pawn_CarryTracker.TryStartCarry"/>'s: a one-meal stack travels as the real object (so a
    /// <see cref="Things.CompQuality"/> or a damaged item arrives as itself), and only a genuinely stackable
    /// split is a new Thing of the same def and stuff.
    /// <para/>
    /// <b>A warden already holding the meal goes straight to the prisoner</b> (see
    /// <see cref="JobDriver_HaulToCell"/> and <see cref="Toils_Haul.IsCarryingTarget"/> for why).
    /// </summary>
    public sealed class JobDriver_FoodDeliver : JobDriver
    {
        /// <summary>How much of the chosen stack one delivery moves. One meal, not the whole pile: RimWorld
        /// sizes the carry to what the prisoner still needs, and a single unit is this port's honest version
        /// of that — <see cref="JobDriver_Warden_Feed"/> already spends exactly one unit to feed a prisoner,
        /// so the two warden jobs move food at the same granularity. Not a RimWorld literal.</summary>
        public const int MealsPerDelivery = 1;

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

            Toil gotoFood = Toils_Haul.GotoThingToCarry(TargetIndex.A, PathEndMode.ClosestTouch);
            gotoFood.FailOnDespawnedOrNull(TargetIndex.B);
            yield return gotoFood;

            yield return Toils_Haul.StartCarryThing(TargetIndex.A, food => Math.Min(food.stackCount, MealsPerDelivery));

            Toil gotoPrisoner = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoPrisoner.FailOnDespawnedOrNull(TargetIndex.B);
            gotoPrisoner.FailOn(() => !Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A));
            yield return gotoPrisoner;

            yield return Toils_General.Do(() =>
            {
                Map.Map? map = pawn.Map;
                Thing? meal = pawn.carryTracker.CarriedThing;
                if (map == null || meal == null || !Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Where the prisoner actually is now, not where it was when the job was issued: a prisoner
                // that can walk does, and the point of this job is that the meal ends up in its room.
                Pawn? prisoner = job.GetTarget(TargetIndex.B).Thing as Pawn;
                IntVec3 at = prisoner != null && prisoner.Spawned ? prisoner.Position : pawn.Position;

                IntVec3 cell = DropCellNear(map, at, meal.def);
                if (!cell.IsValid) cell = pawn.Position;

                Toils_Haul.TryPlaceCarriedThing(pawn, cell);
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
