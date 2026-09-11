using System.Collections.Generic;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a mineable edifice, walks adjacent to it, digs for a while, then mines it out (RimWorld:
    /// <c>RimWorld.JobDriver_Mine</c>, trimmed — the dig is a flat wait rather than
    /// <c>MiningSpeed</c>-scaled progress against the rock's hit points).
    /// <para/>
    /// <b>The yield goes through <see cref="Mineable.DestroyMined"/></b> — the single path that turns a
    /// mined-out edifice into items, the way <see cref="Building.Plant.Harvest"/> is the single path for a
    /// crop. Until this module that call did not exist: the toil below called <c>rock.Destroy(KillFinalize)</c>
    /// and spawned nothing at all, while this comment claimed the opposite ("no mining yield spawned yet since
    /// no mined-resource ThingDefs exist in content"). That claim was false when it was written —
    /// <c>Buildings_Natural.xml</c> already declared <c>MineableSteel</c> dropping 60 <c>Steel</c>, and
    /// <c>Steel</c> already had construction and crafting recipes waiting on it — so a citizen mined for
    /// hundreds of ticks and the settlement gained nothing.
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
                if (rock is Mineable mineable)
                {
                    mineable.DestroyMined(pawn);
                }
                else
                {
                    // A mineable Def whose thingClass is not Mineable: no yield to give, so the old
                    // behaviour (remove it and move on) is still the right one. Content cannot reach this
                    // today — a test asserts every mineable Def in content is a Mineable — and it stays
                    // rather than throwing, because a job driver is the wrong place to fail a content bug.
                    rock.Destroy(DestroyMode.KillFinalize);
                }
            });
        }
    }
}
