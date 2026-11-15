using System.Collections.Generic;

using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a piece of apparel, takes a moment to put it on, drops whatever worn piece it conflicts with
    /// where the pawn stands, and wears it (RimWorld: <c>RimWorld.JobDriver_Wear</c>).
    ///
    /// <para/><b>Recalled:</b> RimWorld's wait is the apparel's <c>EquipDelay</c> stat in seconds. This port
    /// has no <c>EquipDelay</c> stat, so every piece takes <see cref="WearDurationTicks"/>; the tests pin that
    /// dressing takes time and ends dressed, not the number.
    /// </summary>
    public sealed class JobDriver_Wear : JobDriver
    {
        /// <summary>Stand-in for <c>EquipDelay</c> x 60 ticks (recalled, unsourced).</summary>
        public const int WearDurationTicks = 120;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            Toil gotoApparel = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            gotoApparel.FailOnDespawnedOrNull(TargetIndex.A);
            yield return gotoApparel;
            Toil wait = Toils_General.Wait(WearDurationTicks);
            wait.FailOnDespawnedOrNull(TargetIndex.A);
            yield return wait;
            yield return Toils_General.Do(() =>
            {
                if (job.GetTarget(TargetIndex.A).Thing is not ThingWithComps apparel || !apparel.Spawned) return;
                Map.Map map = apparel.Map!;
                apparel.DeSpawn();
                DropConflicting(apparel, map);
                pawn.apparel.Wear(apparel);
            });
        }

        /// <summary>Takes off and drops every worn piece the new one conflicts with (RimWorld: the
        /// <c>pawn.apparel.TryDrop</c> loop in <c>JobDriver_Wear</c>'s finishing toil).</summary>
        private void DropConflicting(ThingWithComps apparel, Map.Map map)
        {
            IReadOnlyList<ThingWithComps> worn = pawn.apparel.WornApparel;
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                ThingWithComps old = worn[i];
                if (old.def.apparel == null || !old.def.apparel.ConflictsWith(apparel.def.apparel!)) continue;
                pawn.apparel.RemoveApparel(old);
                GenSpawn.Spawn(old, pawn.Position, map);
            }
        }
    }
}
