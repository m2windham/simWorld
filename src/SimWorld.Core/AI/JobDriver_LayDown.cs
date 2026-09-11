using System.Collections.Generic;
using SimWorld.Needs;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to the target — a claimed <see cref="Building.ConstructionThingDefOf.Bed"/> when
    /// <see cref="JobGiver_GetRest"/> found one, else the pawn's own position — and sleeps there until
    /// rested (RimWorld: <c>RimWorld.JobDriver_LayDown</c>). A bed is reserved like any other claimed target
    /// (<see cref="Toils_Reserve.Reserve"/>) so two pawns never converge on the same one; the ground case
    /// reserves nothing, exactly as this job always did before beds existed.
    /// </summary>
    public sealed class JobDriver_LayDown : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            LocalTargetInfo target = job.GetTarget(TargetIndex.A);
            if (!target.HasThing) return true;
            return pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, target);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            if (job.GetTarget(TargetIndex.A).HasThing) yield return Toils_Reserve.Reserve(TargetIndex.A);

            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            var sleep = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sleep.initAction = () =>
            {
                pawn.Asleep = true;
                if (pawn.needs.rest != null)
                {
                    Thing? bed = job.GetTarget(TargetIndex.A).Thing;
                    pawn.needs.rest.lastRestEffectiveness = bed != null ? Need_Rest.BedRestEffectiveness : 1f;
                }
            };
            sleep.tickAction = () =>
            {
                // Something woke the pawn (RimWorld: RestUtility.WakeUp, which this port's
                // Pawn_JobTracker.Notify_DamageTaken calls by clearing the flag). Nothing else in the job
                // clears it, so finding it clear means an interrupt arrived — and lying here awake is the one
                // outcome that must not happen: Need_Rest.Resting reads pawn.Asleep, so an awake sleeper's
                // rest *falls*, the "rested" exit below can never be reached, and the job runs forever. End
                // instead and let the think tree decide: the danger tier sits above the needs tier, so a pawn
                // woken by an enemy it can reach fights, and one woken by a stray shot from across the map
                // simply goes back to bed.
                if (!pawn.Asleep)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel >= 0.999f)
                {
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            sleep.FailOn(() => pawn.needs.rest == null);
            sleep.FailOnDespawnedOrNull(TargetIndex.A);
            yield return sleep;
        }

        public override void Notify_Ending() => pawn.Asleep = false;
    }
}
