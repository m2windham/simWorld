using System.Globalization;

using SimWorld.AI;
using SimWorld.Pawns;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Colonists idle" (RimWorld: <c>RimWorld.Alert_ColonistsIdle</c>): colonists with nothing to do.
    ///
    /// <para/>RimWorld reads <c>Pawn_MindState.IsIdle</c>, which tracks the <c>JobTag.Idle</c> of the last job
    /// taken. This port's <see cref="JobTag"/> is carried but never set, so idleness is read from the job
    /// itself: wandering (<see cref="JobDefOf.GotoWander"/>, what the bottom of the humanlike think tree hands
    /// a pawn with nothing better) while sane and on their feet. A wander inside a mental state is a break,
    /// not idleness, and is left to the break alerts. RimWorld's own alert also stays quiet for the first
    /// day of a game; that gate is not ported.
    /// </summary>
    public sealed class Alert_ColonistsIdle : Alert
    {
        public override AlertPriority Priority => AlertPriority.Medium;

        public override string DefaultLabel => "Citizens idle";

        internal static bool IsIdle(Pawn p) =>
            !p.Downed && !p.InMentalState && p.jobs?.curJob?.def == JobDefOf.GotoWander;

        public override AlertReport GetReport(AlertContext context) => ColonistsWhere(context, IsIdle);

        public override string GetLabel(AlertContext context, AlertReport report) =>
            report.Culprits.Count.ToString(CultureInfo.InvariantCulture)
            + (report.Culprits.Count == 1 ? " citizen idle" : " citizens idle");

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These citizens have nothing to do. Check their work priorities, or give them work:" +
            CulpritList(report);
    }
}
