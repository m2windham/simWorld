using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.God.View
{
    public static partial class GodCommands
    {
        /// <summary>
        /// Sets one citizen's priority for one work type (RimWorld: clicking a cell of the work tab, which calls
        /// <c>Pawn_WorkSettings.SetPriority</c>). <paramref name="priority"/> is 0 (never) or 1-4, 1 first —
        /// <see cref="WorkPriorityTable.LowestPriority"/> is the largest.
        ///
        /// <para/><b>The lever.</b> <c>JobGiver_Work</c> scans <c>WorkGiversInOrderNormal</c>, which this reorders,
        /// so the citizen's next routine job follows the new order. A priority set here is marked as the
        /// citizen's own (<c>Pawn_WorkSettings</c>'s manual set) and an office role applied later does not
        /// overwrite it. Setting a number turns on ranked priorities for this citizen
        /// (<see cref="WorkPriorityRow.ManualPriorities"/>) — without that the simulation reads every enabled
        /// work as 3 and the number would change nothing. Turning it on moves none of their other cells: until
        /// it is on, they can only hold 0 or 3.
        ///
        /// <para/>Refuses only the impossible: no such citizen, no such work type, a number outside 0-4, or work
        /// the citizen is incapable of (the reason names the trait, backstory or gene). Every unwise setting —
        /// nobody on Doctor, a builder never building — is allowed.
        /// </summary>
        /// <returns>Done when the setting changed; NoChange when it already stood as the citizen's own; Refused
        /// with the reason otherwise.</returns>
        public static GodCommandResult SetWorkPriority(int citizenThingId, string workTypeDefName, int priority)
        {
            if (priority < 0 || priority > Pawn_WorkSettings.LowestPriority)
            {
                return GodCommandResult.Refused(
                    "There is no order " + priority.ToString(CultureInfo.InvariantCulture) + ": 0 leaves the work alone, and 1 to "
                    + Pawn_WorkSettings.LowestPriority.ToString(CultureInfo.InvariantCulture) + " run from "
                    + WorkPriorityTable.PriorityWord(1) + " to " + WorkPriorityTable.PriorityWord(Pawn_WorkSettings.LowestPriority) + ".");
            }

            WorkTypeDef? workType = string.IsNullOrEmpty(workTypeDefName)
                ? null
                : DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null)
            {
                return GodCommandResult.Refused("No work type named '" + workTypeDefName + "'.");
            }

            Pawn? pawn = LiveCitizen(citizenThingId);
            if (pawn == null)
            {
                return GodCommandResult.Refused(
                    "No living citizen with id " + citizenThingId.ToString(CultureInfo.InvariantCulture) + ".");
            }

            string? incapable = WorkDisabledReason.For(pawn, workType);
            if (incapable != null)
            {
                return priority == 0
                    ? GodCommandResult.NoChange(incapable)
                    : GodCommandResult.Refused(incapable);
            }

            Pawn_WorkSettings settings = pawn.workSettings;
            bool wasManual = settings.useWorkPriorities;
            int before = settings.GetPriority(workType);
            bool rankingTurnsOn = !wasManual && priority != 0 && priority != Pawn_WorkSettings.DefaultPriority;

            if (rankingTurnsOn) settings.useWorkPriorities = true;
            settings.SetPriority(workType, priority);

            string work = workType.LabelCap;
            if (before == priority && !rankingTurnsOn)
            {
                return GodCommandResult.NoChange(
                    priority == 0
                        ? pawn.Label + " already leaves " + work + " alone."
                        : pawn.Label + " already does " + work + " " + WorkPriorityTable.PriorityWord(priority) + ".");
            }

            return GodCommandResult.Done(
                priority == 0
                    ? pawn.Label + " will no longer do " + work + "."
                    : pawn.Label + " now does " + work + " " + WorkPriorityTable.PriorityWord(priority) + ".");
        }

        /// <summary>A live citizen of any settlement, by thing id — the population the roster lists.</summary>
        private static Pawn? LiveCitizen(int thingId)
        {
            World.World? world = Find.World;
            if (world == null) return null;

            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement settlement)) continue;
                IReadOnlyList<Pawn> roster = settlement.Citizens;
                for (int c = 0; c < roster.Count; c++)
                {
                    if (roster[c].thingIDNumber == thingId && !roster[c].Dead) return roster[c];
                }
            }
            return null;
        }
    }
}
