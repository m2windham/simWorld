using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Walks onto an empty, sowable growing-zone cell and plants it (RimWorld: <c>RimWorld.JobDriver_Sow</c>).
    /// Work speed scales with the Plants skill, the same shape (and same unsourced curve values — see
    /// <see cref="PlantUtility.WorkSpeedFactorFromPlantsLevel"/>) <see cref="JobDriver_ConstructFinishFrame"/>
    /// already uses for Construction.
    /// </summary>
    public sealed class JobDriver_Sow : JobDriver
    {
        /// <summary>Total sowing work a cell needs, before the Plants-skill speed factor. Unsourced —
        /// RimWorld's own real Sow work amount is not known here — picked to be a short, observable chunk of
        /// simulated labor, on the same order as <c>JobDriver_Mine.MineDurationTicks</c>.</summary>
        public const float SowWorkAmount = 300f;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            float workDone = 0f;
            work.tickAction = () =>
            {
                int skillLevel = work.Pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                workDone += PlantUtility.WorkSpeedFactorFromPlantsLevel.Evaluate(skillLevel);
                if (workDone < SowWorkAmount) return;

                TrySow(work.Pawn, work.Job);
                work.actor.ReadyForNextToil();
            };
            yield return work;
        }

        /// <summary>Re-checks eligibility at the moment of sowing rather than trusting the scan that queued
        /// this job — the cell, the zone, or what it wants sown may have changed while the pawn walked over.</summary>
        private static void TrySow(Pawn pawn, Job job)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return;
            IntVec3 cell = job.GetTarget(TargetIndex.A).Cell;

            if (!(map.zoneManager.ZoneAt(cell) is Zone_Growing growing) || !growing.allowSow) return;
            ThingDef? toSow = growing.plantDefToGrow;
            if (toSow == null) return;
            if (map.thingGrid.CellContains(cell, ThingCategory.Plant)) return;

            var plant = (Plant)ThingMaker.MakeThing(toSow);
            plant.Growth = Plant.SeedlingGrowth;
            GenSpawn.Spawn(plant, cell, map);
        }
    }
}
