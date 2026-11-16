using System;
using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Alerts
{
    /// <summary>
    /// How loudly an alert asks to be read (RimWorld: <c>RimWorld.AlertPriority</c>, the same three values in
    /// the same order). RimWorld draws <see cref="Critical"/> alerts flashing red; this port only carries the
    /// ranking, and the host decides what it looks like.
    /// </summary>
    public enum AlertPriority
    {
        Medium,
        High,
        Critical,
    }

    /// <summary>
    /// One settlement's on-map situation, as an alert reads it — the stand-in for RimWorld's habit of walking
    /// <c>Find.Maps</c> and <c>map.mapPawns.FreeColonistsSpawned</c> from inside every alert.
    ///
    /// <para/><b>Who counts as a colonist.</b> The settlement's own <see cref="World.Settlement.Citizens"/> who
    /// are alive and spawned on its interior map. That is RimWorld's <c>FreeColonistsSpawned</c> with this
    /// port's ownership model: a citizen is the civilization's person, and only a spawned one is somewhere a
    /// doctor or a hauler could reach. A citizen at a coarse tier has no body on the map and no alert can be
    /// raised about them, which is the truth — nothing on the map can be done for them either.
    /// </summary>
    public sealed class AlertContext
    {
        public AlertContext(Map.Map map, IReadOnlyList<Pawn> colonists, int settlementTile)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Colonists = colonists ?? throw new ArgumentNullException(nameof(colonists));
            SettlementTile = settlementTile;
        }

        public Map.Map Map { get; }

        /// <summary>Alive, spawned, on <see cref="Map"/>, and the settlement's own.</summary>
        public IReadOnlyList<Pawn> Colonists { get; }

        /// <summary>The world tile of the settlement this map belongs to.</summary>
        public int SettlementTile { get; }
    }

    /// <summary>
    /// Whether an alert is up, and what it is about (RimWorld: <c>RimWorld.AlertReport</c>, whose
    /// <c>culpritsPawns</c>/<c>culpritsThings</c> are folded into one list of <see cref="Thing"/>s here because
    /// a <see cref="Pawn"/> is one).
    /// </summary>
    public sealed class AlertReport
    {
        private AlertReport(bool active, IReadOnlyList<Thing> culprits)
        {
            Active = active;
            Culprits = culprits;
        }

        public static AlertReport Inactive { get; } = new AlertReport(false, Array.Empty<Thing>());

        public bool Active { get; }

        /// <summary>What the alert is about — the colonists starving, the fire burning — in the order the
        /// alert found them. Empty for an alert about the whole settlement rather than anyone in it
        /// (<see cref="Alert_LowFood"/>).</summary>
        public IReadOnlyList<Thing> Culprits { get; }

        /// <summary>RimWorld's <c>AlertReport.CulpritsAre</c>: active exactly when there is at least one.</summary>
        public static AlertReport CulpritsAre(IReadOnlyList<Thing>? culprits) =>
            culprits == null || culprits.Count == 0 ? Inactive : new AlertReport(true, culprits);

        /// <summary>Active with nobody to point at (RimWorld: <c>AlertReport.Active</c>).</summary>
        public static AlertReport ActiveWithoutCulprits { get; } = new AlertReport(true, Array.Empty<Thing>());
    }

    /// <summary>
    /// One condition the player should know about right now (RimWorld: <c>RimWorld.Alert</c>). Like
    /// RimWorld's, an alert holds no state: it is derived fresh from the simulation every time it is asked,
    /// so there is nothing to tick and nothing to save. Each concrete alert keeps RimWorld's class name, which
    /// is also the handle the god view carries (RimWorld has no <c>AlertDef</c>; the class is the identity).
    ///
    /// <para/>RimWorld's alerts read <c>Find.Maps</c> themselves; these are handed one
    /// <see cref="AlertContext"/> at a time by <see cref="AlertsReadout"/>, because this port has many
    /// settlements and the player needs to know which one is on fire.
    /// </summary>
    public abstract class Alert
    {
        /// <summary>RimWorld: <c>Alert.defaultPriority</c>.</summary>
        public virtual AlertPriority Priority => AlertPriority.Medium;

        /// <summary>RimWorld: <c>Alert.defaultLabel</c>.</summary>
        public abstract string DefaultLabel { get; }

        public abstract AlertReport GetReport(AlertContext context);

        /// <summary>RimWorld: <c>Alert.GetLabel</c>. Most alerts just say <see cref="DefaultLabel"/>; a few
        /// count their culprits into it.</summary>
        public virtual string GetLabel(AlertContext context, AlertReport report) => DefaultLabel;

        /// <summary>RimWorld: <c>Alert.GetExplanation</c> — what is wrong and what to do about it.</summary>
        public abstract string GetExplanation(AlertContext context, AlertReport report);

        /// <summary>The identity the seam carries: RimWorld's own class name, e.g. <c>Alert_LowFood</c>.</summary>
        public string DefName => GetType().Name;

        /// <summary>"\n  Name" per culprit, the list RimWorld's explanations append to their own sentence.</summary>
        protected static string CulpritList(AlertReport report)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < report.Culprits.Count; i++)
            {
                text.Append("\n  ").Append(report.Culprits[i].Label);
            }
            return text.ToString();
        }

        /// <summary>The colonists matching <paramref name="predicate"/>, as culprits.</summary>
        protected static AlertReport ColonistsWhere(AlertContext context, Func<Pawn, bool> predicate)
        {
            List<Thing>? culprits = null;
            IReadOnlyList<Pawn> colonists = context.Colonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (predicate(colonists[i])) (culprits ??= new List<Thing>()).Add(colonists[i]);
            }
            return AlertReport.CulpritsAre(culprits);
        }
    }
}
