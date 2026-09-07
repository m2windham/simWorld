using System.Collections.Generic;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to the target cell (usually the pawn's own position — see <see cref="JobGiver_GetRest"/>) and
    /// sleeps there until rested (RimWorld: <c>RimWorld.JobDriver_LayDown</c>, ground-only — no bed comps
    /// or postures exist yet).
    /// </summary>
    public sealed class JobDriver_LayDown : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            var sleep = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sleep.initAction = () => pawn.Asleep = true;
            sleep.tickAction = () =>
            {
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel >= 0.999f)
                {
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            sleep.FailOn(() => pawn.needs.rest == null);
            yield return sleep;
        }

        public override void Notify_Ending() => pawn.Asleep = false;
    }
}
