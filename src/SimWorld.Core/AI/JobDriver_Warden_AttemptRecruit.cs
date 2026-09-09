using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a prisoner and makes one recruitment visit (RimWorld: a trim of <c>RimWorld.JobDriver_ChatWithPrisoner</c>
    /// — real RimWorld loops several <c>ConvinceRecruitee</c> rounds inside one job; this port's own
    /// <see cref="WardenUtility.TryInteract"/> already made "one visit = one resistance step, or a recruit
    /// once it's already at zero" the whole mechanic, so one <see cref="WardenUtility.TryInteract"/> call per
    /// completed job is the faithful shape here — a prisoner still short of zero resistance simply gets
    /// re-visited by a fresh job the next time <see cref="WorkGiver_Warden_AttemptRecruit"/> scans, exactly as
    /// <see cref="JobDriver_Tame"/>/<see cref="JobDriver_Train"/> already re-roll per job rather than looping
    /// internally).
    /// </summary>
    public sealed class JobDriver_Warden_AttemptRecruit : JobDriver
    {
        /// <summary>Not RimWorld-sourced; a short, arbitrary interaction beat, matching <see cref="JobDriver_Tame"/>/<see cref="JobDriver_Train"/>'s own.</summary>
        public const int InteractDurationTicks = 200;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoPrisoner = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return gotoPrisoner;

            Toil interact = Toils_General.Wait(InteractDurationTicks);
            interact.FailOnDespawnedOrNull(TargetIndex.A);
            yield return interact;

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Pawn prisoner) || prisoner.Destroyed || !prisoner.Spawned) return;
                WardenUtility.TryInteract(pawn, prisoner);
            });
        }
    }
}
