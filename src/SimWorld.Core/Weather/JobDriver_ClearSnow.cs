using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Stats;

namespace SimWorld.Weather
{
    /// <summary>
    /// Walks to one snowed-in cell and shovels it bare (RimWorld: <c>RimWorld.JobDriver_ClearSnow</c>). Work
    /// accrues at the shoveller's <see cref="SnowStatDefOf.GeneralLaborSpeed"/> per tick against
    /// <see cref="ClearWorkPerSnowDepth"/> × the cell's depth, so thick snow takes longer than a thin layer —
    /// and when it is done the cell is set to zero outright, as RimWorld's is, not thinned.
    /// </summary>
    public sealed class JobDriver_ClearSnow : JobDriver
    {
        /// <summary>Work to clear a cell at full depth (RimWorld: <c>JobDriver_ClearSnow.ClearWorkPerSnowDepth</c>,
        /// 50). Recalled, not sourced; tests pin "deeper snow takes longer and the cell ends bare".</summary>
        public const float ClearWorkPerSnowDepth = 50f;

        private float workDone;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);

            var clear = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            clear.initAction = () => workDone = 0f;
            clear.tickAction = () =>
            {
                Map.Map? map = clear.Pawn.Map;
                if (map == null) return;
                IntVec3 cell = clear.Job.GetTarget(TargetIndex.A).Cell;

                workDone += clear.Pawn.GetStatValue(SnowStatDefOf.GeneralLaborSpeed);
                if (workDone >= ClearWorkPerSnowDepth * map.snowGrid.GetDepth(cell))
                {
                    map.snowGrid.SetDepth(cell, 0f);
                    clear.actor.ReadyForNextToil();
                }
            };
            yield return clear;
        }
    }
}
