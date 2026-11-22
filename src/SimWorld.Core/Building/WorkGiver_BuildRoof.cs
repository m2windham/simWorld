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
    /// Sends a builder to roof a cell of the build-roof area (RimWorld: <c>RimWorld.WorkGiver_BuildRoof</c>,
    /// read from <c>josh-m/RW-Decompile/RimWorld/WorkGiver_BuildRoof.cs</c>; WorkGiverDef <c>BuildRoofs</c>,
    /// Construction, priority 100, from RimWorld 1.0's <c>Core/Defs/WorkGiverDefs/WorkGivers.xml</c>).
    /// Cell-scanning over <see cref="AreaManager.BuildRoof"/>, which <see cref="AutoBuildRoofAreaSetter"/>
    /// marks. A cell nobody can stand next to is reached through the building standing on it, as RimWorld does.
    ///
    /// <para/><b>Not ported.</b> RimWorld reserves the cell on its own <c>Ceiling</c> layer so a roofer does not
    /// block whoever uses the floor; this port has no reservation layers, so the cell itself is reserved.
    /// Forbidden cells (nothing forbids a cell here) and the plant that blocks a roof
    /// (<c>RoofUtility.FirstBlockingThing</c> reads <c>PlantProperties.interferesWithRoof</c>, which this port's
    /// plants do not carry) are not checked.
    /// </summary>
    public sealed class WorkGiver_BuildRoof : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>RimWorld scans the area every time; an empty area is skipped here so a settlement with no
        /// closed room pays nothing for the scan.</summary>
        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || pawn.Map.areaManager.BuildRoof.TrueCount == 0;

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (IntVec3 c in map.areaManager.BuildRoof.ActiveCells) yield return c;
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            if (!map.areaManager.BuildRoof[cell]) return false;
            if (map.roofGrid.Roofed(cell)) return false;
            if (!map.reservationManager.CanReserve(pawn, cell)) return false;
            if (!Reachability.CanReach(pawn, cell, PathEndMode.Touch) && BuildingToTouchToBeAbleToBuildRoof(cell, pawn) == null) return false;
            if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map)) return false;
            return RoofCollapseUtility.ConnectedToRoofHolder(cell, map, assumeRoofAtRoot: true);
        }

        public override Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            LocalTargetInfo stand = cell;
            if (!Reachability.CanReach(pawn, cell, PathEndMode.Touch))
            {
                Thing? building = BuildingToTouchToBeAbleToBuildRoof(cell, pawn);
                if (building != null) stand = building;
            }
            return new Job(RoofJobDefOf.BuildRoof, cell, stand);
        }

        /// <summary>The building on a cell no one can stand by, when that building can itself be touched.</summary>
        private static Thing? BuildingToTouchToBeAbleToBuildRoof(IntVec3 cell, Pawn pawn)
        {
            Map.Map map = pawn.Map!;
            if (GenGrid.Standable(cell, map)) return null;
            Thing? edifice = map.edificeGrid[cell];
            if (edifice == null) return null;
            return Reachability.CanReach(pawn, edifice, PathEndMode.Touch) ? edifice : null;
        }
    }

    /// <summary>
    /// Walks to a build-roof cell and roofs it and its roofable neighbours (RimWorld:
    /// <c>RimWorld.JobDriver_BuildRoof</c> over <c>JobDriver_AffectRoof</c>, read from the 1.0 decompile).
    /// Target A is the cell, B where to stand. 65 units of work (RimWorld's <c>BaseWorkAmount</c>), spent at the
    /// builder's construction speed — this port reads that speed off the Construction skill, as
    /// <see cref="AI.JobDriver_Repair"/> does. On completion every cell of the 3x3 round the target that is in
    /// the area, unroofed and held up gets <c>RoofConstructed</c>. Its sibling <c>JobDriver_RemoveRoof</c> is
    /// not ported: nothing here designates a roof for removal.
    /// </summary>
    public sealed class JobDriver_BuildRoof : JobDriver
    {
        /// <summary>RimWorld: <c>JobDriver_AffectRoof.BaseWorkAmount</c>.</summary>
        public const float BaseWorkAmount = 65f;

        private float workLeft;

        private IntVec3 Cell => job.GetTarget(TargetIndex.A).Cell;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoToil = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);
            AddFailConditions(gotoToil);
            yield return gotoToil;

            var doWork = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            AddFailConditions(doWork);
            doWork.FailOn(() => pawn.Map!.roofGrid.Roofed(Cell));
            doWork.FailOnCannotReach(TargetIndex.B, PathEndMode.Touch);
            doWork.initAction = () => workLeft = BaseWorkAmount;
            doWork.tickAction = () =>
            {
                int level = pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
                workLeft -= JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel.Evaluate(level);
                if (workLeft > 0f) return;
                DoEffect();
                doWork.actor.ReadyForNextToil();
            };
            yield return doWork;
        }

        /// <summary>RimWorld's three driver-wide fail conditions, on each toil: the cell left the area, or
        /// nothing would hold a roof there.</summary>
        private void AddFailConditions(Toil toil)
        {
            toil.FailOn(() => pawn.Map == null || !pawn.Map.areaManager.BuildRoof[Cell]);
            toil.FailOn(() => !RoofCollapseUtility.WithinRangeOfRoofHolder(Cell, pawn.Map!));
            toil.FailOn(() => !RoofCollapseUtility.ConnectedToRoofHolder(Cell, pawn.Map!, assumeRoofAtRoot: true));
        }

        private void DoEffect()
        {
            Map.Map map = pawn.Map!;
            // The eight neighbours, then the cell itself (RimWorld's GenAdj.AdjacentCellsAndInside order).
            for (int i = 0; i <= GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 c = i < GenAdj.AdjacentCells.Length ? Cell + GenAdj.AdjacentCells[i] : Cell;
                if (!GenGrid.InBounds(c, map)) continue;
                if (!map.areaManager.BuildRoof[c] || map.roofGrid.Roofed(c)) continue;
                if (!RoofCollapseUtility.WithinRangeOfRoofHolder(c, map)) continue;
                // RimWorld lays RoofConstructed everywhere; a house that names its own roof (a pit house's
                // earth) gets that instead (shelter lane, DwellingDef.roofDef).
                map.roofGrid.SetRoof(c, DwellingUtility.RoofFor(map, c));
            }
        }
    }

    [DefOf]
    public static class RoofJobDefOf
    {
        public static JobDef BuildRoof = null!;
    }
}
