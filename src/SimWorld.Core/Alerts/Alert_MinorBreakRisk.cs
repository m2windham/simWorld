using SimWorld.MindState;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Minor break risk" (RimWorld: <c>RimWorld.Alert_MinorBreakRisk</c>): colonists whose mood is below
    /// their minor break threshold but not yet their major one — the ones
    /// <see cref="Alert_MajorOrExtremeBreakRisk"/> does not already cover.
    /// </summary>
    public sealed class Alert_MinorBreakRisk : Alert
    {
        public override AlertPriority Priority => AlertPriority.High;

        public override string DefaultLabel => "Low spirits";

        public override AlertReport GetReport(AlertContext context) =>
            ColonistsWhere(context, p => Alert_MajorOrExtremeBreakRisk.RiskOf(p) == MentalBreakIntensity.Minor);

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These citizens are low enough that they could lose their composure:" + CulpritList(report);
    }
}
