using System.Collections.Generic;

namespace SimWorld.AI
{
    /// <summary>Walks to a cell and pauses there briefly (RimWorld: <c>RimWorld.JobDriver_GotoWander</c>).</summary>
    public sealed class JobDriver_GotoWander : JobDriver
    {
        /// <summary>Not RimWorld-sourced (that value lives on the wander JobGiver's duration range, itself
        /// tuned per pawn kind); a short, arbitrary idle beat once arrived.</summary>
        public const int PauseTicks = 60;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(PauseTicks);
        }
    }
}
