using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Map;

namespace SimWorld.Building
{
    /// <summary>
    /// Builds a constructed roof over one cell of <see cref="AreaManager.BuildRoof"/> (RimWorld:
    /// <c>RimWorld.JobDriver_BuildRoof</c>). See <see cref="JobDriver_AffectRoof"/>'s own doc for the shape
    /// every roof job shares.
    /// </summary>
    public sealed class JobDriver_BuildRoof : JobDriver_AffectRoof
    {
        protected override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// Three extra ways this job gives up mid-toil, applied to every toil so a pawn part-way to the cell
        /// notices too, not only once work starts (RimWorld: <c>JobDriver_BuildRoof.MakeNewToils</c>'s own
        /// three <c>this.FailOn</c> calls, which — unlike a <see cref="Toil"/>'s own <c>FailOn</c> — apply to
        /// every toil the base sequence yields from that point on; this port's <see cref="JobDriver"/> has no
        /// such driver-level helper, so the same effect is reached by adding the condition to each toil as it
        /// comes back from <see cref="JobDriver_AffectRoof.MakeNewToils"/>).
        /// </summary>
        public override IEnumerable<Toil> MakeNewToils()
        {
            foreach (Toil toil in base.MakeNewToils())
            {
                toil.FailOn(() => pawn.Map == null || !pawn.Map.areaManager.BuildRoof[Cell]);
                toil.FailOn(() => pawn.Map == null || !RoofCollapseUtility.WithinRangeOfRoofHolder(Cell, pawn.Map));
                toil.FailOn(() => pawn.Map == null || !RoofCollapseUtility.ConnectedToRoofHolder(Cell, pawn.Map));
                yield return toil;
            }
        }

        protected override void DoEffect()
        {
            Map.Map? map = pawn.Map;
            if (map == null) return;

            // Roofs the whole 3x3 around the cell, not just the cell itself (RimWorld:
            // JobDriver_BuildRoof.DoEffect's own GenAdj.AdjacentCellsAndInside walk) — one swing of the tool
            // finishes a corner, not one cell at a time, provided each of those cells is itself still a
            // legitimate BuildRoof cell that has not already been roofed or lost its support since this job
            // started walking over.
            IntVec3 origin = Cell;
            for (int d = -1; d <= 1; d++)
            {
                for (int e = -1; e <= 1; e++)
                {
                    var c = new IntVec3(origin.x + d, 0, origin.z + e);
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.areaManager.BuildRoof[c]) continue;
                    if (map.roofGrid.Roofed(c)) continue;
                    if (!RoofCollapseUtility.WithinRangeOfRoofHolder(c, map)) continue;
                    if (RoofUtility.FirstBlockingThing(c, map) != null) continue;
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
            }
        }

        protected override bool DoWorkFailOn() => pawn.Map != null && pawn.Map.roofGrid.Roofed(Cell);
    }
}
