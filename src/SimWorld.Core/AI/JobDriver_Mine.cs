using System.Collections.Generic;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a mineable edifice, walks adjacent to it, digs for a while, then removes it (RimWorld:
    /// <c>RimWorld.JobDriver_Mine</c>, trimmed — no mining yield spawned yet since no mined-resource
    /// ThingDefs exist in content; <see cref="Things.ThingDef.mineableThing"/>/<c>mineableYield</c> are read
    /// so a later module only needs to add the spawn call here).
    /// </summary>
    public sealed class JobDriver_Mine : JobDriver
    {
        /// <summary>RimWorld's real duration scales with the Mining skill and a MiningSpeed stat; simplified
        /// to a flat constant, pinned by this module's own tests rather than by a sourced number.</summary>
        public const int MineDurationTicks = 300;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoRock = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return gotoRock;

            Toil dig = Toils_General.Wait(MineDurationTicks);
            dig.FailOnDespawnedOrNull(TargetIndex.A);
            yield return dig;

            yield return Toils_General.Do(() =>
            {
                Thing? rock = job.GetTarget(TargetIndex.A).Thing;
                if (rock == null || rock.Destroyed) return;
                // Mining removes the rock outright rather than damaging it through TakeDamage/HitPoints —
                // matching Buildings_Natural.xml's own comment that useHitPoints/destroyable model ordinary
                // damage, and mining is deliberately not that.
                rock.Destroy(DestroyMode.KillFinalize);
            });
        }
    }
}
