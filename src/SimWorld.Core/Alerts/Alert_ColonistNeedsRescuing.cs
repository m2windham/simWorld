using SimWorld.AI;
using SimWorld.Pawns;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Colonist needs rescue" (RimWorld: <c>RimWorld.Alert_ColonistNeedsRescuing</c>, an
    /// <c>Alert_Critical</c>): colonists lying downed somewhere other than a bed. RimWorld's
    /// <c>NeedsRescue</c> also excuses a pawn already being carried and one whose queued job will get them up;
    /// a carried pawn is not spawned here so it never reaches the check, and this port has no job queue a
    /// downed pawn could hold.
    /// </summary>
    public sealed class Alert_ColonistNeedsRescuing : Alert
    {
        public override AlertPriority Priority => AlertPriority.Critical;

        public override string DefaultLabel => "Colonist needs rescue";

        /// <summary>RimWorld: <c>Alert_ColonistNeedsRescuing.NeedsRescue</c>, trimmed as the class doc says.
        /// Shared with <see cref="Alert_ColonistNeedsTend"/>, which steps aside for anyone this covers, the same
        /// way RimWorld's does.</summary>
        public static bool NeedsRescue(Pawn p) => p.Downed && p.Spawned && !p.InBed();

        public override AlertReport GetReport(AlertContext context) => ColonistsWhere(context, NeedsRescue);

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These colonists are downed and lying where they fell. Someone must carry them to a bed, or they " +
            "may bleed, freeze or starve where they lie:" + CulpritList(report);
    }
}
