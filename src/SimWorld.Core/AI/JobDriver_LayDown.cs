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
        /// <summary>
        /// How often a pawn lying down looks up to see whether something more important needs it (RimWorld:
        /// <c>Toils_LayDown.LayDown</c>'s <c>IsHashIntervalTick(211)</c>, with <c>lookForOtherJobs</c> true for
        /// <c>JobDriver_LayDown</c> — both read from the 1.0 decompile).
        /// </summary>
        public const int LookForOtherJobsIntervalTicks = 211;

        public override bool TryMakePreToilReservations()
        {
            LocalTargetInfo target = job.GetTarget(TargetIndex.A);
            if (!target.HasThing) return true;
            return pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, target);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            bool inBed = job.GetTarget(TargetIndex.A).HasThing;
            if (inBed) yield return Toils_Reserve.Reserve(TargetIndex.A);

            // A bed is walked onto at its sleeping slot, not wherever its footprint happens to be nearest
            // (RimWorld: Toils_Bed.GotoBed). The ground case walks to the cell it was given, as it always did.
            yield return inBed ? Toils_Bed.GotoBed(TargetIndex.A) : Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

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
                // Emergency work gets a sleeper up (RimWorld: the lying-down toil re-runs the think tree every
                // 211 ticks, and "Emergency work" sits above the rest giver in its humanlike tree, so a doctor
                // asleep beside a colonist bleeding to death gets up and tends them). Only that one tier is
                // asked here, not the whole tree, and the narrowing is this port's: its tree carries the combat
                // tier (the draft's stand-in, CombatPostureUtility) above needs, so a full re-run would get a
                // sleeper out of bed for a raider it has merely seen — exactly what LayDown's
                // casualInterruptible="false" exists to prevent (JobDefs_Core.xml). Hunger waking a sleeper,
                // which RimWorld's full re-run can also do, is not asked about either.
                if (pawn.IsHashIntervalTick(LookForOtherJobsIntervalTicks))
                {
                    Job? emergency = JobGiver_Work.TryGiveEmergencyJob(pawn);
                    if (emergency != null)
                    {
                        pawn.jobs.StartJob(emergency, JobCondition.InterruptOptional);
                        return;
                    }
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
