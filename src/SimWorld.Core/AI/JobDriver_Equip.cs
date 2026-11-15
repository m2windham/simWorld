using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a weapon and takes it up as primary, dropping any primary already carried where the pawn
    /// stands (RimWorld: <c>RimWorld.JobDriver_Equip</c>; the drop is RimWorld's
    /// <c>Pawn_EquipmentTracker.MakeRoomFor</c>). RimWorld's equip is instant once there, and so is this.
    /// </summary>
    public sealed class JobDriver_Equip : JobDriver
    {
        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            Toil gotoWeapon = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            gotoWeapon.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoWeapon;
            yield return Toils_General.Do(() =>
            {
                if (job.GetTarget(TargetIndex.A).Thing is not ThingWithComps weapon || !weapon.Spawned) return;
                Map.Map map = weapon.Map!;
                weapon.DeSpawn();
                ThingWithComps? old = pawn.equipment.Primary;
                if (old != null)
                {
                    pawn.equipment.RemoveEquipment(old);
                    GenSpawn.Spawn(old, pawn.Position, map);
                }
                pawn.equipment.AddEquipment(weapon);
            });
        }
    }
}
