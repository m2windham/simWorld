using System.Collections.Generic;

using SimWorld.Building;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The roof-area half of <see cref="MapCommands"/>: <see cref="SetBuildRoofArea"/> and
    /// <see cref="SetNoRoofArea"/>. Its own file, the same split <see cref="MapCommands.StandingRules"/> keeps
    /// from <c>MapCommands.cs</c> — see that file's own doc for the shared discipline (same result type, same
    /// named-outcome refusals, same map resolution off <see cref="God.AttentionManager.FocusedSettlement"/>,
    /// same "physically impossible only" refusal rule) and <see cref="MapCommands.SetHomeArea"/> for the
    /// closest sibling: same shape, one area lower.
    ///
    /// <para/><b>Mutually exclusive by paint, exactly as RimWorld's own two designators are.</b> RimWorld's
    /// <c>Designator_AreaBuildRoof.DesignateSingleCell</c> clears <c>NoRoof</c> at every cell it paints, and
    /// <c>Designator_AreaNoRoof.DesignateSingleCell</c>/<c>FinalizeDesignationSucceeded</c> clears
    /// <c>BuildRoof</c> right back — a cell cannot sit in both areas after either command runs, and this is
    /// where both call it (there is no other designator layer in this port to have called it first). Ported
    /// as a plain "the other area loses this cell" write inside the method that adds to one, since
    /// <see cref="Building.Area"/> has no per-cell "just changed" list of its own to defer the clear through
    /// the way RimWorld's <c>justAddedCells</c> does.
    ///
    /// <para/><b>One refusal that is not RimWorld's "unwise, so allow it" rule: a thick natural roof.</b>
    /// <c>Designator_AreaNoRoof.CanDesignateCell</c> refuses to paint <c>NoRoof</c> over a cell whose roof is
    /// <see cref="RoofDef.isThickRoof"/> ("MessageNothingCanRemoveThickRoofs") — not a nicety, a real physical
    /// fact this codebase would otherwise contradict: <see cref="JobDriver_RemoveRoof.DoEffect"/> calls
    /// <see cref="RoofGrid.SetRoof"/> unconditionally, so with no refusal here a player could order a
    /// mountain's own overhead rock stripped away cell by cell through this seam, something no tool in
    /// RimWorld can do at all. <see cref="SetNoRoofArea"/> keeps that refusal for the same reason
    /// <see cref="MapCommands.PlaceBlueprint"/> refuses a cell already occupied — a cell whose command can
    /// never take effect however long a builder stands there, not a placement merely gone badly.
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Adds or removes <paramref name="cells"/> from <see cref="AreaManager.BuildRoof"/> — "roof here,
        /// from now on" (or not). Adding a cell here clears it from <see cref="AreaManager.NoRoof"/> at the
        /// same time — see the class doc. <see cref="AutoBuildRoofAreaSetter"/> already adds the same area on
        /// its own around an enclosed room in the home area; this is the same door <see cref="SetHomeArea"/>
        /// already opens for the home area, one system over — a settlement enclosing itself does not need
        /// this call at all, and a player painting further out (an open-air roof over a courtyard, say, that
        /// never enclosed anything on its own) reaches ground the settlement's own initiative never would.
        ///
        /// <para/>Refuses only: no map open; no cells given; any cell off the map. <b>Never</b> refuses a roof
        /// area with nothing under it to hold it up, or one that will never finish for want of a builder — see
        /// the class doc on <see cref="MapCommands"/> itself for why an outcome merely unwise is not a refusal.
        /// </summary>
        public static MapCommandResult SetBuildRoofArea(IReadOnlyList<IntVec3> cells, bool included)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            for (int i = 0; i < cells!.Count; i++)
            {
                map.areaManager.BuildRoof[cells[i]] = included;
                if (included) map.areaManager.NoRoof[cells[i]] = false;
            }

            return MapCommandResult.Done(
                (included ? "Added " : "Removed ") + cells.Count + " cell(s) "
                + (included ? "to" : "from") + " the build-roof area.");
        }

        /// <summary>
        /// Adds or removes <paramref name="cells"/> from <see cref="AreaManager.NoRoof"/> — "never roof here"
        /// (or "allow it again"). Adding a cell here clears it from <see cref="AreaManager.BuildRoof"/> at the
        /// same time — see the class doc. This is also what actually removes an existing roof:
        /// <see cref="WorkGiver_RemoveRoof"/> reads this area, not the reverse of
        /// <see cref="SetBuildRoofArea"/>, exactly as RimWorld's own two designators are two separate paints
        /// rather than one toggle.
        ///
        /// <para/>Refuses only: no map open; no cells given; any cell off the map; and, when
        /// <paramref name="included"/> is <c>true</c>, any cell already under a thick natural roof
        /// (<see cref="RoofDef.isThickRoof"/>) — checked over the whole batch before any cell is touched, so a
        /// request that would have partly failed leaves nothing changed. See the class doc for why this one
        /// refusal is a physical fact, not the "unwise, so allow it" case <see cref="MapCommands"/> otherwise
        /// never makes. <b>Never</b> refuses forbidding a roof nobody was ever going to remove, or one still
        /// holding a settlement's own ceiling up — that judgement is the player's, not this call's.
        /// </summary>
        public static MapCommandResult SetNoRoofArea(IReadOnlyList<IntVec3> cells, bool included)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            if (included)
            {
                for (int i = 0; i < cells!.Count; i++)
                {
                    RoofDef? roof = map.roofGrid.RoofAt(cells[i]);
                    if (roof != null && roof.isThickRoof)
                    {
                        return MapCommandResult.Refused(
                            "Nothing can remove the thick roof at " + cells[i] + ".");
                    }
                }
            }

            for (int i = 0; i < cells!.Count; i++)
            {
                map.areaManager.NoRoof[cells[i]] = included;
                if (included) map.areaManager.BuildRoof[cells[i]] = false;
            }

            return MapCommandResult.Done(
                (included ? "Added " : "Removed ") + cells.Count + " cell(s) "
                + (included ? "to" : "from") + " the no-roof area.");
        }
    }
}
