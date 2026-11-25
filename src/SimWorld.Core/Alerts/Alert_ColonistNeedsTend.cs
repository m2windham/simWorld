using SimWorld.AI;
using SimWorld.Health;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Colonist needs treatment" (RimWorld: <c>RimWorld.Alert_ColonistNeedsTend</c>): colonists with a
    /// wound or illness a doctor could tend now (<see cref="HealthAIUtility.HasHediffsNeedingTend"/>), who are
    /// not already covered by <see cref="Alert_ColonistNeedsRescuing"/>.
    ///
    /// <para/>RimWorld excuses a patient already lying in a <i>medical</i> bed — they are where a doctor will
    /// find them. This port has no medical/ordinary distinction between beds, so any bed excuses them: a
    /// patient in bed is waiting for a doctor, and <see cref="Alert_NeedDoctor"/> is what says when no doctor
    /// is coming.
    /// </summary>
    public sealed class Alert_ColonistNeedsTend : Alert
    {
        public override AlertPriority Priority => AlertPriority.High;

        public override string DefaultLabel => "Citizen needs treatment";

        public override AlertReport GetReport(AlertContext context) =>
            ColonistsWhere(context, p =>
                HealthAIUtility.HasHediffsNeedingTend(p) && !p.InBed()
                && !Alert_ColonistNeedsRescuing.NeedsRescue(p));

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These citizens have wounds or illnesses that need a doctor's care. Untended wounds bleed and " +
            "can become infected:" + CulpritList(report);
    }
}
