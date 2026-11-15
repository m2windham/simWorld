using System.Collections.Generic;

using SimWorld.Alerts;

namespace SimWorld.God.View
{
    /// <summary>
    /// One alert that is up right now, on one settlement's map (RimWorld: an entry in the alert readout at the
    /// right of the screen). Values only: the alert's class name as its handle, the words the core wrote, and
    /// the thing ids of whatever it is about.
    /// </summary>
    public sealed class AlertView
    {
        internal AlertView(
            string defName, string label, string explanation, AlertPriority priority, int settlementTile,
            IReadOnlyList<int> culpritThingIds)
        {
            DefName = defName;
            Label = label;
            Explanation = explanation;
            Priority = priority;
            SettlementTile = settlementTile;
            CulpritThingIds = culpritThingIds;
        }

        /// <summary>RimWorld's alert class name, e.g. <c>Alert_LowFood</c> — stable across saves and builds.
        /// RimWorld has no AlertDef; the class is the identity, so it is the handle here too.</summary>
        public string DefName { get; }

        /// <summary>The short line for the readout, e.g. "Colonist needs rescue" or "3 colonists idle".</summary>
        public string Label { get; }

        /// <summary>The tooltip text: what is wrong and what to do, culprits named. Display verbatim.</summary>
        public string Explanation { get; }

        public AlertPriority Priority { get; }

        /// <summary>The world tile of the settlement whose map raised it.</summary>
        public int SettlementTile { get; }

        /// <summary>Thing ids (<c>thingIDNumber</c>) of the pawns or things it is about, in the order the alert
        /// found them — the same ids <see cref="CitizenLine.ThingId"/> and the map view's
        /// <c>PawnView.ThingId</c>/<c>ThingView.ThingId</c> carry, so the host can select or jump to them. Empty
        /// for an alert about the settlement as a whole (low food).</summary>
        public IReadOnlyList<int> CulpritThingIds { get; }

        internal static List<AlertView> ActiveNow()
        {
            List<ActiveAlert> active = AlertsReadout.ActiveAlerts();
            var views = new List<AlertView>(active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                ActiveAlert a = active[i];
                IReadOnlyList<Things.Thing> culprits = a.Report.Culprits;
                var ids = new int[culprits.Count];
                for (int c = 0; c < culprits.Count; c++) ids[c] = culprits[c].thingIDNumber;

                views.Add(new AlertView(
                    a.Alert.DefName,
                    a.Alert.GetLabel(a.Context, a.Report),
                    a.Alert.GetExplanation(a.Context, a.Report),
                    a.Alert.Priority,
                    a.Context.SettlementTile,
                    ids));
            }
            return views;
        }
    }
}
