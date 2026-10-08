using System.Collections.Generic;

using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The mining half of <see cref="MapCommands"/>: <see cref="DesignateMine"/> and
    /// <see cref="CancelMineDesignation"/>. Its own file, the same split <c>MapCommands.Roofs.cs</c> and
    /// <c>MapCommands.StandingRules.cs</c> keep from <c>MapCommands.cs</c> — see that file's doc for the
    /// shared discipline (one result type, named outcomes, the map resolved off the god's attention, and
    /// "physically impossible only" as the whole of what is refused).
    ///
    /// <para/><b>This is the player's door into mining, and until this file it did not exist.</b> RimWorld's
    /// <c>Designator_Mine</c> is the whole of how a colony ever digs: you drag a box and the cells in it get a
    /// <see cref="DesignationDefOf.Mine"/> mark; <c>WorkGiver_Miner</c> reads only those. This port had no
    /// marks at all, so its miners dug everything. Now the mark exists, <c>AI.MiningInitiative</c> makes
    /// the settlement's own, and these two methods let the player make theirs in the same ledger — a citizen
    /// neither knows nor cares whose mark it is working.
    ///
    /// <para/><b>The one place a batch is applied partially, and why.</b> Every other method on this surface
    /// checks the whole batch and then acts, so a request that would have half failed leaves nothing changed.
    /// <see cref="DesignateMine"/> instead marks the cells that can take a mark and skips the ones that cannot,
    /// because the ordinary gesture here is a rectangle dragged across the edge of a mountain, and a rule
    /// that refused the whole drag for the open ground inside it would refuse nearly every drag anyone makes.
    /// This is RimWorld's own designator behaviour (<c>CanDesignateCell</c> is asked per cell and only the
    /// accepted ones are designated). A cell off the map is still refused for the whole request, as it is
    /// everywhere else on this surface: that is a malformed request, not a rectangle that overlapped rock.
    ///
    /// <para/><b>What is not refused.</b> A mark on a cell whose rock holds a roof up. That is the player's
    /// call, as it is in RimWorld. Be aware what it does here: <c>AI.WorkGiver_Miner</c> keeps the
    /// <see cref="Building.RoofCollapseUtility.WouldCollapseRoofIfRemoved"/> judgement it has always had, so the
    /// mark is recorded and no citizen digs it while it would bring the ceiling down. Where a host wants to
    /// warn the player before they commit, the read model is the place for it — never this command.
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Marks the rock in <paramref name="cells"/> for digging on the open settlement's map — "mine this".
        /// From here the ordinary pipeline takes over exactly as it does for a mark the settlement's own
        /// initiative made: a citizen with a free hand and the Mining work type walks to the marked rock,
        /// swings a pick until its hit points run out, and the chunk or ore it holds lands in the cell; the
        /// mark goes with the rock.
        ///
        /// <para/>Refuses only: no map open; no cells given; any cell off the map. A cell with no rock in it,
        /// or one that is already marked, is skipped (see the class doc). If nothing at all could be marked
        /// the answer says which of the two it was — <see cref="MapCommandOutcome.NoChange"/> when everything
        /// mineable was already marked (the world already matches), <see cref="MapCommandOutcome.Refused"/>
        /// when there was nothing to mine in the request.
        /// </summary>
        public static MapCommandResult DesignateMine(IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            int marked = 0, alreadyMarked = 0, nothingToMine = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (MineableUtility.GetFirstMineable(cell, map) == null)
                {
                    nothingToMine++;
                    continue;
                }
                if (map.designationManager.AddDesignation(new Designation(cell, DesignationDefOf.Mine))) marked++;
                else alreadyMarked++;
            }

            if (marked > 0)
            {
                return MapCommandResult.Done(
                    "Marked " + marked + " cell(s) for mining" + Skipped(alreadyMarked, nothingToMine) + ".");
            }
            if (alreadyMarked > 0)
            {
                return MapCommandResult.NoChange("Every mineable cell given was already marked for mining.");
            }
            return MapCommandResult.Refused("There is nothing to mine in " + cells.Count + " cell(s) given.");
        }

        /// <summary>
        /// Takes the mine mark off <paramref name="cells"/> — the undo for <see cref="DesignateMine"/>. A
        /// citizen already swinging a pick at a cell whose mark is withdrawn stops at once (the job fails on
        /// the missing mark, as RimWorld's does), and the rock keeps whatever damage it has taken. Works on
        /// the settlement's own marks as well as the player's: they are one ledger.
        ///
        /// <para/>Cancelling cells with no mark on them is <see cref="MapCommandOutcome.NoChange"/>, not a
        /// failure — the world already matches what was asked for. Refuses only: no map open; no cells given;
        /// any cell off the map.
        /// </summary>
        public static MapCommandResult CancelMineDesignation(IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            int removed = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                Designation? mark = map.designationManager.DesignationAt(cells[i], DesignationDefOf.Mine);
                if (mark == null) continue;
                map.designationManager.RemoveDesignation(mark);
                removed++;
            }

            return removed > 0
                ? MapCommandResult.Done("Cancelled the mine mark on " + removed + " cell(s).")
                : MapCommandResult.NoChange("None of those " + cells.Count + " cell(s) was marked for mining.");
        }

        private static string Skipped(int alreadyMarked, int nothingToMine)
        {
            if (alreadyMarked == 0 && nothingToMine == 0) return "";
            var parts = new List<string>(2);
            if (alreadyMarked > 0) parts.Add(alreadyMarked + " already marked");
            if (nothingToMine > 0) parts.Add(nothingToMine + " with nothing to mine");
            return " (skipped " + string.Join(", ", parts) + ")";
        }
    }
}
