using SimWorld.MindState;
using SimWorld.Pawns;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Major break risk" / "Extreme break risk" (RimWorld: <c>RimWorld.Alert_MajorOrExtremeBreakRisk</c>, an
    /// <c>Alert_Critical</c>): colonists whose mood sits below their own major break threshold and who are not
    /// already in a mental state. The label reads "Extreme" as soon as anyone is past the extreme threshold,
    /// as RimWorld's does. The bands are <see cref="MentalBreaker.CurMoodBreakIntensity"/>, against each
    /// colonist's own thresholds, not a scale invented here.
    /// </summary>
    public sealed class Alert_MajorOrExtremeBreakRisk : Alert
    {
        public override AlertPriority Priority => AlertPriority.Critical;

        public override string DefaultLabel => "Major break risk";

        internal static MentalBreakIntensity RiskOf(Pawn p) =>
            p.needs?.mood == null || p.InMentalState || p.mindState?.mentalBreaker == null
                ? MentalBreakIntensity.None
                : p.mindState.mentalBreaker.CurMoodBreakIntensity;

        public override AlertReport GetReport(AlertContext context) =>
            ColonistsWhere(context, p => RiskOf(p) >= MentalBreakIntensity.Major);

        public override string GetLabel(AlertContext context, AlertReport report)
        {
            for (int i = 0; i < report.Culprits.Count; i++)
            {
                if (report.Culprits[i] is Pawn p && RiskOf(p) == MentalBreakIntensity.Extreme) return "Extreme break risk";
            }
            return DefaultLabel;
        }

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These citizens are so unhappy they could suffer a serious mental break at any moment. Find out " +
            "what is weighing on them and give them something better:" + CulpritList(report);
    }
}
