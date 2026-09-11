using System.Collections.Generic;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a fire, walks up to it and beats it down one swat at a time until it is out (RimWorld:
    /// <c>RimWorld.JobDriver_BeatFire</c>).
    /// <para/>
    /// <b>The two numbers are RimWorld's shape recalled, not sourced</b> (CLAUDE.md: say so, and pin the
    /// behaviour instead of the literal). What matters and what the tests hold to: beating removes size
    /// faster than a fire of any size regrows it, so a citizen who stays on the job always wins; and a bigger
    /// fire takes more swats than a small one.
    /// <para/>
    /// <b>No skill or stat scaling.</b> RimWorld has none either for this job — the Firefighter work type
    /// carries no <c>relevantSkills</c> in RimWorld's content or in this port's, so there is nothing to
    /// scale by and nothing was invented.
    /// <para/>
    /// A fire riding a pawn walks away mid-job. Rather than let the toil spin forever out of arm's reach,
    /// the job ends and the (emergency-priority) work giver simply re-issues it with a fresh path next tick.
    /// </summary>
    public sealed class JobDriver_BeatFire : JobDriver
    {
        /// <summary>Ticks between swats — see the class doc on why this is unsourced.</summary>
        public const int TicksPerBeat = 60;

        /// <summary>Fire size removed per swat. Comfortably outpaces
        /// <c>Fire.FireBaseGrowthPerTick</c> over the same span at any flammability.</summary>
        public const float FireSizeReductionPerBeat = 0.2f;

        private int ticksToNextBeat;

        private Fire? TargetFire => job.GetTarget(TargetIndex.A).Thing as Fire;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var beat = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            beat.FailOnDespawnedOrNull(TargetIndex.A);
            beat.initAction = () => ticksToNextBeat = TicksPerBeat;
            beat.tickAction = () =>
            {
                Fire? fire = TargetFire;
                if (fire == null || fire.Destroyed || !fire.Spawned)
                {
                    // Someone else got it, or it burned itself out. Nothing left to do is a success.
                    beat.actor.ReadyForNextToil();
                    return;
                }
                if (!InArmsReachOf(fire))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (--ticksToNextBeat > 0) return;
                ticksToNextBeat = TicksPerBeat;

                fire.fireSize -= FireSizeReductionPerBeat;
                if (fire.fireSize < Fire.MinFireSize)
                {
                    fire.Destroy();
                    beat.actor.ReadyForNextToil();
                }
            };
            yield return beat;
        }

        private bool InArmsReachOf(Fire fire) =>
            pawn.Position == fire.Position || pawn.Position.AdjacentTo8Way(fire.Position);
    }
}
