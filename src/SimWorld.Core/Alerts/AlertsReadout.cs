using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Alerts
{
    /// <summary>One alert that is up, on one settlement's map.</summary>
    public sealed class ActiveAlert
    {
        internal ActiveAlert(Alert alert, AlertContext context, AlertReport report)
        {
            Alert = alert;
            Context = context;
            Report = report;
        }

        public Alert Alert { get; }

        public AlertContext Context { get; }

        public AlertReport Report { get; }
    }

    /// <summary>
    /// Every alert the game knows, asked of every settlement with an interior map (RimWorld:
    /// <c>RimWorld.AlertsReadout</c>, which builds <c>AllAlerts</c> by instantiating every concrete
    /// <see cref="Alert"/> subclass — the same reflection here, so a new alert is a new file and nothing
    /// else).
    ///
    /// <para/><b>What is left out of RimWorld's readout, and why.</b> RimWorld staggers evaluation across
    /// frames (<c>AlertsReadoutUpdate</c> checks a slice of the alerts each frame) because it is drawing them
    /// every frame. This port's host asks on demand through the god view, so each call simply evaluates all of
    /// them once; there is no per-frame budget to spread. The order is fixed — priority, highest first, then
    /// class name — so two calls on the same tick list the same alerts in the same order.
    /// </summary>
    public static class AlertsReadout
    {
        private static IReadOnlyList<Alert>? allAlerts;

        /// <summary>One instance of every concrete <see cref="Alert"/> in this assembly, highest priority
        /// first and then by class name (ordinal), so the list is the same on every machine.</summary>
        public static IReadOnlyList<Alert> AllAlerts => allAlerts ??= typeof(Alert).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Alert).IsAssignableFrom(t))
            .Select(t => (Alert)Activator.CreateInstance(t)!)
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.DefName, StringComparer.Ordinal)
            .ToList();

        /// <summary>
        /// Every alert that is up right now, on every settlement that has an interior map, in world-object
        /// order and then <see cref="AllAlerts"/> order. Empty with no game running.
        /// </summary>
        public static List<ActiveAlert> ActiveAlerts()
        {
            var active = new List<ActiveAlert>();
            World.World? world = Find.World;
            if (world == null) return active;

            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is World.Settlement settlement && settlement.InteriorMap != null)
                {
                    AddActive(ContextOf(settlement, settlement.InteriorMap), active);
                }
            }
            return active;
        }

        /// <summary>The colonists an alert sees on <paramref name="map"/>: see <see cref="AlertContext"/>.</summary>
        public static AlertContext ContextOf(World.Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            var colonists = new List<Pawn>();
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (!p.Dead && p.Spawned && ReferenceEquals(p.Map, map)) colonists.Add(p);
            }
            return new AlertContext(map, colonists, settlement.tile);
        }

        private static void AddActive(AlertContext context, List<ActiveAlert> into)
        {
            IReadOnlyList<Alert> alerts = AllAlerts;
            for (int a = 0; a < alerts.Count; a++)
            {
                AlertReport report = alerts[a].GetReport(context);
                if (report.Active) into.Add(new ActiveAlert(alerts[a], context, report));
            }
        }
    }
}
