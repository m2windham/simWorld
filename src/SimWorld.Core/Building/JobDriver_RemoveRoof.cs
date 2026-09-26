using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Map;

namespace SimWorld.Building
{
    /// <summary>
    /// Strips the roof off one cell of <see cref="AreaManager.NoRoof"/> (RimWorld:
    /// <c>RimWorld.JobDriver_RemoveRoof</c>). See <see cref="JobDriver_AffectRoof"/>'s own doc for the shape
    /// every roof job shares. Simpler than <see cref="JobDriver_BuildRoof"/> in two ways RimWorld's own class
    /// already is: target A and B are always the same cell — RimWorld's own <c>WorkGiver_RemoveRoof.JobOnCell</c>
    /// never substitutes an adjacent building the way <c>WorkGiver_BuildRoof</c>'s does, even though a NoRoof
    /// cell can equally well be a wall (this port's own <see cref="RoofWorkTests"/> covers roofing a wall cell
    /// through <see cref="WorkGiver_BuildRoof"/>, and nothing stops the same wall cell from being painted
    /// <see cref="AreaManager.NoRoof"/> too) — <see cref="PathEndMode.ClosestTouch"/> already reaches a
    /// non-standable cell by walking adjacent to it, the same way <see cref="PathEndMode.Touch"/> does for any
    /// impassable target, so RimWorld never needed a second name for what to touch here. And there is no
    /// support check — removing a roof cannot itself collapse anything (removing the <i>edifice</i> that held
    /// one up is what does that, and <see cref="RoofCollapseUtility.Notify_RoofHolderDespawned"/> already
    /// covers it).
    /// </summary>
    public sealed class JobDriver_RemoveRoof : JobDriver_AffectRoof
    {
        protected override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        /// <summary>RimWorld: <c>JobDriver_RemoveRoof.MakeNewToils</c>'s own single <c>this.FailOn</c> — see
        /// <see cref="JobDriver_BuildRoof.MakeNewToils"/>'s own doc for why this port applies it to every toil
        /// by hand rather than through a driver-level helper.</summary>
        public override IEnumerable<Toil> MakeNewToils()
        {
            foreach (Toil toil in base.MakeNewToils())
            {
                toil.FailOn(() => pawn.Map == null || !pawn.Map.areaManager.NoRoof[Cell]);
                yield return toil;
            }
        }

        protected override void DoEffect()
        {
            Map.Map? map = pawn.Map;
            if (map == null) return;

            map.roofGrid.SetRoof(Cell, null);

            // A roof this port already builds and removes only one cell at a time can still leave a larger
            // neighbouring patch flying once this cell's own support is gone — the same check
            // RoofCollapseCellsFinder already runs when an edifice despawns (RimWorld:
            // JobDriver_RemoveRoof.DoEffect's own CheckCollapseFlyingRoofs call, immediately after the same
            // SetRoof(null)).
            RoofCollapseCellsFinder.ProcessRoofRemoved(Cell, map);
        }

        protected override bool DoWorkFailOn() => pawn.Map != null && !pawn.Map.roofGrid.Roofed(Cell);
    }
}
