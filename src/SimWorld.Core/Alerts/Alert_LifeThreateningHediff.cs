using System.Collections.Generic;

using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Life-threatening condition" (RimWorld: <c>RimWorld.Alert_LifeThreateningHediff</c>, an
    /// <c>Alert_Critical</c>): colonists carrying a visible hediff whose current stage is
    /// <see cref="HediffStage.lifeThreatening"/>. RimWorld also excuses a disease the pawn is already fully
    /// immune to; that check is not ported, so an immune patient whose disease is still at a lethal stage
    /// stays listed until the stage drops.
    /// </summary>
    public sealed class Alert_LifeThreateningHediff : Alert
    {
        public override AlertPriority Priority => AlertPriority.Critical;

        public override string DefaultLabel => "Life-threatening condition";

        internal static bool HasLifeThreateningHediff(Pawn p)
        {
            List<Hediff>? hediffs = p.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h.Visible && h.CurStage != null && h.CurStage.lifeThreatening) return true;
            }
            return false;
        }

        public override AlertReport GetReport(AlertContext context) => ColonistsWhere(context, HasLifeThreateningHediff);

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These citizens have a condition that will kill them if it runs its course. Get them treated:" +
            CulpritList(report);
    }
}
