using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Stats;

namespace SimWorld.Filth
{
    /// <summary>
    /// Walks to one pile of filth and scrubs it away a layer at a time (RimWorld:
    /// <c>RimWorld.JobDriver_CleanFilth</c>).
    /// <para/>
    /// Work is counted the way RimWorld counts it: a running total that ticks up by the cleaner's
    /// <c>CleaningSpeed</c>, and every time it passes the def's
    /// <see cref="Defs.FilthProperties.cleaningWorkToReduceThickness"/> one point of
    /// <see cref="Filth.thickness"/> comes off. A three-thick pile therefore costs three times what a fresh
    /// one does, which is the whole point of thickness existing — filth that has been walked over all day is
    /// a real job, not the same flat cost as a single footprint.
    /// <para/>
    /// <b>Cleaning has no skill.</b> This driver teaches the cleaner nothing and consults no
    /// <see cref="Work.SkillDef"/>, which is RimWorld's own design (there is no Cleaning skill in the game)
    /// and the reason <c>CleaningSpeed</c> ships with <c>capacityFactors</c> but no <c>skillNeedFactors</c> —
    /// a cleaner is only ever slowed by injury, never sped up by practice. It is the one wired work giver in
    /// this codebase that does not call <c>skills.Learn</c>, so the absence is deliberate rather than missed.
    /// </summary>
    public sealed class JobDriver_CleanFilth : JobDriver
    {
        /// <summary>Work accumulated toward the next layer; reset each time one comes off. A field rather
        /// than a captured local because the toil's <c>initAction</c> has to clear it when the toil is
        /// re-entered, the same shape <see cref="JobDriver_Repair"/> uses.</summary>
        private float cleaningWorkDone;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var clean = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            clean.FailOnDespawnedOrNull(TargetIndex.A);
            clean.initAction = () => cleaningWorkDone = 0f;
            clean.tickAction = () =>
            {
                if (!(clean.Job.GetTarget(TargetIndex.A).Thing is Filth filth) || filth.Destroyed)
                {
                    // Somebody else finished it, or it rotted away underfoot while we walked over. Nothing
                    // left to clean is a success, not a failure — the cell is clean either way.
                    clean.actor.ReadyForNextToil();
                    return;
                }

                cleaningWorkDone += clean.Pawn.GetStatValue(CleaningStatDefOf.CleaningSpeed);
                if (cleaningWorkDone < filth.Props.cleaningWorkToReduceThickness) return;

                cleaningWorkDone = 0f;
                filth.ThinFilth();
                if (filth.Destroyed) clean.actor.ReadyForNextToil();
            };
            yield return clean;
        }
    }
}
