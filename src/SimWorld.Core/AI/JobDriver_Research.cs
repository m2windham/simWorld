using System.Collections.Generic;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a research bench and spends work ticks feeding <see cref="ResearchManager.CurrentProj"/>
    /// through the real <see cref="ResearchManager"/> (RimWorld: <c>RimWorld.JobDriver_Research</c>).
    /// Target A is the bench <see cref="WorkGiver_Research"/> found.
    /// <para/>
    /// <b>Points-per-tick — <see cref="StatDefOf.ResearchSpeed"/>, not a second formula.</b>
    /// <see cref="ResearchManager.ResearchPointsPerWorkTick"/> is this port's already-sourced base
    /// rate at 100% work speed (that field's own doc names the RimWorld constant it carries forward); this
    /// driver multiplies it by <c>pawn.GetStatValue(StatDefOf.ResearchSpeed)</c> — the skill-need-scaled stat
    /// this module's brief calls out as an existing, unconsumed StatDef — rather than inventing a fresh curve.
    /// This is exactly the formula <c>docs/research/tech-reachability.md</c> §1.2 had to model by hand because
    /// neither half existed yet (<c>points/day = researchers x WorkTicksPerDay x ResearchPointsPerWorkTick x
    /// speed(skill)</c>); that harness's own stand-in <c>speed(skill)</c> curve is retired by this driver in
    /// favour of the real StatDef it was always meant to be replaced by.
    /// <para/>
    /// <b>Intellectual XP per tick — 0.1, RimWorld's own value.</b> Named here rather than left as a bare
    /// literal because <c>docs/research/tech-reachability.md</c>'s assumption table already attributes it to
    /// RimWorld's <c>JobDriver_Research</c> ("Research XP per work tick: 0.1... RimWorld's JobDriver_Research");
    /// this driver is what makes that assumption a real, load-bearing call, matching the same
    /// learning-by-doing shape <see cref="Building.Frame"/> (Construction) and <see cref="Building.Plant"/>
    /// (Plants) already use for their own skills.
    /// <para/>
    /// <b>The project finishing mid-job.</b> <see cref="ResearchManager.ResearchPerformed"/> itself
    /// finishes the project and clears <see cref="ResearchManager.CurrentProj"/> the tick progress
    /// reaches its cost; this driver checks for that same tick and ends the job <see cref="JobCondition.Succeeded"/>
    /// rather than leaving the pawn standing at the bench with nothing left to do until the job's own
    /// despawn/reachability failure conditions happened to catch it. The same check covers
    /// <see cref="ResearchManager.CurrentProj"/> already being null when this toil starts (a debug
    /// command or another researcher's finish cleared it out from under this pawn) — succeeded, not
    /// incompletable, either way: the pawn did nothing wrong.
    /// </summary>
    public sealed class JobDriver_Research : JobDriver
    {
        /// <summary>RimWorld: <c>JobDriver_Research</c>'s own per-tick Intellectual xp gain — see this
        /// class's own remarks for where that number comes from in this codebase.</summary>
        public const float IntellectualXpPerTick = 0.1f;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoBench = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return gotoBench;

            var research = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            research.tickAction = () =>
            {
                ResearchManager manager = Find.ResearchManager;
                if (manager.CurrentProj == null)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                float pointsThisTick = ResearchManager.ResearchPointsPerWorkTick * pawn.GetStatValue(StatDefOf.ResearchSpeed);
                manager.ResearchPerformed(pointsThisTick, pawn);
                pawn.skills.Learn(SkillDefOf.Intellectual, IntellectualXpPerTick);

                // ResearchPerformed above may itself have just finished the project (progress reached
                // baseCost) and cleared CurrentProj — end the job the same tick rather than the next one.
                if (manager.CurrentProj == null) EndJobWith(JobCondition.Succeeded);
            };
            research.FailOnDespawnedOrNull(TargetIndex.A);
            yield return research;
        }
    }
}
