using System.Collections.Generic;
using System.Text;

using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The designation half of <see cref="MapCommands"/>: <see cref="Designate(string, IReadOnlyList{IntVec3})"/>,
    /// <see cref="RemoveDesignations(string, IReadOnlyList{IntVec3})"/> and <see cref="SetSettlementInitiative"/>
    /// — RimWorld's designators (mine, chop, harvest, hunt, tame, slaughter, deconstruct, haul urgently) and the
    /// standing rule that says whether the settlement also does that work on its own initiative.
    ///
    /// <para/><b>The player's marks come first; the settlement fills in.</b> A pawn doing the matching kind of
    /// work takes the player's marks before anything the settlement decided for itself
    /// (<see cref="AI.WorkGiver_PlayerDesignated"/>), and with none it can reach falls back to the settlement's
    /// own initiative exactly as before. Switching an initiative off (<see cref="SetSettlementInitiative"/>)
    /// is "only do what I mark": the settlement stops, the marks are still worked — and a settlement told to
    /// hunt nothing it was not shown may go hungry. That is allowed; see <see cref="MapCommands"/>'s own rule on
    /// refusing only the impossible.
    ///
    /// <para/><b>Multi-cell, RimWorld's way.</b> A list of cells is a drag rectangle: for a cell designation
    /// (Mine) each cell is marked; for a thing designation every thing on each cell that can take the mark is
    /// marked (<c>Designator.DesignateMultiCell</c>). The cells that took nothing are reported by reason, so a
    /// host can say "designated 6; 3 had nothing that can be mined".
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Marks <paramref name="designationDefName"/> over <paramref name="cells"/> on the open settlement.
        /// <see cref="MapCommandOutcome.Done"/> when at least one mark was placed, the reason naming how many and
        /// why the rest were not; <see cref="MapCommandOutcome.NoChange"/> when every cell was already marked;
        /// otherwise <see cref="MapCommandOutcome.Refused"/> with the reasons. <see cref="MapCommandOutcome.UnknownDef"/>
        /// for a name that is no designation the player can place.
        /// </summary>
        public static MapCommandResult Designate(string designationDefName, IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            DesignationDef? def = ResolvePlayerDesignation(designationDefName);
            if (def == null) return MapCommandResult.UnknownDef(designationDefName);
            if (cells == null || cells.Count == 0) return MapCommandResult.Refused("No cells given.");

            int placed = 0;
            var rejected = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < cells.Count; i++)
            {
                int n = DesignatorUtility.DesignateCellOrThingsIn(def, cells[i], map, out string? refusal);
                placed += n;
                if (n == 0) Tally(rejected, refusal!);
            }
            return Summarise(def, placed, cells.Count, rejected);
        }

        /// <summary>
        /// Marks the one thing with <paramref name="thingId"/> (<c>PawnView.ThingId</c>/<c>ThingView.ThingId</c>) —
        /// "hunt that one". For a thing designation only; Mine is a cell designation and is refused here.
        /// Refused in the rule's own words ("Muffalo is not wild").
        /// </summary>
        public static MapCommandResult Designate(string designationDefName, int thingId)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            DesignationDef? def = ResolvePlayerDesignation(designationDefName);
            if (def == null) return MapCommandResult.UnknownDef(designationDefName);
            if (def.targetType != TargetType.Thing)
            {
                return MapCommandResult.Refused(def.defName + " marks cells, not things — designate the cell instead.");
            }
            Thing? t = FindThingById(map, thingId);
            if (t == null) return MapCommandResult.Refused("Nothing with id " + thingId + " is on the open settlement's map.");

            string? refusal = DesignatorUtility.TryDesignateThing(def, t, map);
            if (refusal == null) return MapCommandResult.Done(t.Label + " designated: " + def.defName + ".");
            return refusal.EndsWith("already designated", System.StringComparison.Ordinal)
                ? MapCommandResult.NoChange(refusal + ".")
                : MapCommandResult.Refused(refusal + ".");
        }

        /// <summary>
        /// Removes <paramref name="designationDefName"/>'s marks — every kind when null — on
        /// <paramref name="cells"/> and on the things standing there, whoever placed them (RimWorld:
        /// <c>Designator_Cancel</c>). A mark the settlement placed may come back if its initiative still wants
        /// that work; <see cref="SetSettlementInitiative"/> is how to stop that.
        /// <see cref="MapCommandOutcome.NoChange"/> when nothing was marked there.
        /// </summary>
        public static MapCommandResult RemoveDesignations(string? designationDefName, IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            DesignationDef? def = null;
            if (designationDefName != null)
            {
                def = ResolvePlayerDesignation(designationDefName);
                if (def == null) return MapCommandResult.UnknownDef(designationDefName);
            }
            if (cells == null || cells.Count == 0) return MapCommandResult.Refused("No cells given.");

            int removed = 0;
            for (int i = 0; i < cells.Count; i++) removed += DesignatorUtility.RemoveDesignationsIn(def, cells[i], map);
            return removed == 0
                ? MapCommandResult.NoChange("Nothing is designated there.")
                : MapCommandResult.Done("Removed " + removed + " designation(s).");
        }

        /// <summary>Removes the marks (of one kind, or every kind when null) on the thing with
        /// <paramref name="thingId"/>.</summary>
        public static MapCommandResult RemoveDesignations(string? designationDefName, int thingId)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            DesignationDef? def = null;
            if (designationDefName != null)
            {
                def = ResolvePlayerDesignation(designationDefName);
                if (def == null) return MapCommandResult.UnknownDef(designationDefName);
            }
            Thing? t = FindThingById(map, thingId);
            if (t == null) return MapCommandResult.Refused("Nothing with id " + thingId + " is on the open settlement's map.");
            int removed = DesignatorUtility.RemoveDesignationsOn(def, t, map);
            return removed == 0
                ? MapCommandResult.NoChange(t.Label + " is not designated.")
                : MapCommandResult.Done("Removed " + removed + " designation(s) from " + t.Label + ".");
        }

        /// <summary>
        /// Switches the open settlement's own initiative for one kind of designated work on or off —
        /// <c>Mine</c>, <c>CutPlant</c>, <c>Hunt</c> or <c>Tame</c>. Off: the settlement no longer does that work
        /// of its own accord (it withdraws its own Mine marks, cuts no plant it was not shown, hunts and tames
        /// nothing unmarked); the player's marks are still worked. On by default. Refused for a kind the
        /// settlement never does on its own (Slaughter, Deconstruct, HaulUrgently, HarvestPlant).
        /// </summary>
        public static MapCommandResult SetSettlementInitiative(string designationDefName, bool on)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            DesignationDef? def = ResolvePlayerDesignation(designationDefName);
            if (def == null) return MapCommandResult.UnknownDef(designationDefName);

            bool hasInitiative = false;
            IReadOnlyList<DesignationDef> kinds = MapViewSnapshot.InitiativeKinds;
            for (int i = 0; i < kinds.Count; i++) hasInitiative |= kinds[i] == def;
            if (!hasInitiative)
            {
                return MapCommandResult.Refused(
                    "The settlement never does " + def.defName + " work on its own initiative, so there is nothing to switch.");
            }

            if (map.designationManager.SettlementInitiativeOn(def) == on)
            {
                return MapCommandResult.NoChange("The settlement's own " + def.defName + " initiative is already " + (on ? "on" : "off") + ".");
            }
            map.designationManager.SetSettlementInitiative(def, on);
            return MapCommandResult.Done(on
                ? "The settlement will again do " + def.defName + " work on its own initiative."
                : "The settlement will only do " + def.defName + " work it is shown.");
        }

        private static DesignationDef? ResolvePlayerDesignation(string? defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            DesignationDef? def = DefDatabase<DesignationDef>.GetNamedSilentFail(defName);
            return def != null && DesignatorUtility.IsPlayerDesignation(def) ? def : null;
        }

        private static void Tally(List<KeyValuePair<string, int>> rejected, string reason)
        {
            for (int i = 0; i < rejected.Count; i++)
            {
                if (rejected[i].Key != reason) continue;
                rejected[i] = new KeyValuePair<string, int>(reason, rejected[i].Value + 1);
                return;
            }
            rejected.Add(new KeyValuePair<string, int>(reason, 1));
        }

        private static MapCommandResult Summarise(DesignationDef def, int placed, int cellCount, List<KeyValuePair<string, int>> rejected)
        {
            var sb = new StringBuilder();
            sb.Append("Designated ").Append(placed).Append(' ').Append(def.defName)
              .Append(def.targetType == TargetType.Cell ? " cell(s)" : " target(s)")
              .Append(" over ").Append(cellCount).Append(" cell(s)");
            for (int i = 0; i < rejected.Count; i++)
            {
                sb.Append(i == 0 ? "; " : ", ").Append(rejected[i].Value).Append(" cell(s): ").Append(rejected[i].Key);
            }
            sb.Append('.');
            string reason = sb.ToString();

            if (placed > 0) return MapCommandResult.Done(reason);
            bool allAlready = rejected.Count == 1 && rejected[0].Key == "already designated";
            return allAlready ? MapCommandResult.NoChange(reason) : MapCommandResult.Refused(reason);
        }
    }
}
