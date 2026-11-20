using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a designated animal, works on it for <see cref="SlaughterDurationTicks"/>, and kills it with a
    /// cut (RimWorld: <c>JobDriver_Slaughter</c> → <c>ExecutionUtility.DoExecutionByCut</c>). The body is an
    /// ordinary corpse afterwards, which hauling and butchering already handle. RimWorld's execution cut aims
    /// at the neck and spills blood; this port kills with a <c>Cut</c> damage record and no wound, so the
    /// death reads as violent (<c>Pawn_HealthTracker.DiedViolently</c>) without simulating the wound.
    /// </summary>
    public sealed class JobDriver_Slaughter : JobDriver
    {
        /// <summary>RimWorld's <c>JobDriver_Slaughter.SlaughterDuration</c> (recalled, not re-read).</summary>
        public const int SlaughterDurationTicks = 180;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil work = Toils_General.Wait(SlaughterDurationTicks);
            work.FailOnDespawnedOrNull(TargetIndex.A);
            yield return work;

            yield return Toils_General.Do(() =>
            {
                // Animals only: MapCommands.OrderJob takes any job on any thing, and a person is not slaughtered.
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn victim) || !victim.RaceProps.Animal || victim.Dead || !victim.Spawned) return;
                victim.health.Kill(new DamageInfo(DamageDefOf.Cut, 99999f, 999f, pawn), null);
            });
        }
    }
}
