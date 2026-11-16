using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Need doctor" (RimWorld: <c>RimWorld.Alert_NeedDoctor</c>): there are patients, and not one colonist
    /// who is on their feet has Doctor work switched on. RimWorld's own test, kept as it is — the healthy
    /// doctor is anyone not downed whose <see cref="Pawn_WorkSettings.WorkIsActive"/> says Doctor, which is
    /// the same priority the player sets in the work tab. So this alert is what tells a player that the
    /// priority they turned off was the one keeping people alive.
    ///
    /// <para/>RimWorld's patient test is "downed, in bed and hungry" or <c>ShouldBeTendedNowByPlayer</c>.
    /// The second half is <see cref="HealthAIUtility.HasHediffsNeedingTend"/> here; the first reads "hungry"
    /// as anything past <see cref="Needs.HungerCategory.Fed"/> (the exact comparison in RimWorld's source
    /// was not checked for this port).
    /// </summary>
    public sealed class Alert_NeedDoctor : Alert
    {
        public override AlertPriority Priority => AlertPriority.High;

        public override string DefaultLabel => "Need doctor";

        public override AlertReport GetReport(AlertContext context)
        {
            IReadOnlyList<Pawn> colonists = context.Colonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (!p.Downed && p.workSettings != null && p.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
                {
                    return AlertReport.Inactive;
                }
            }

            List<Thing>? patients = null;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                bool hungryInBed = p.Downed && p.needs?.food != null
                    && p.needs.food.CurCategory > Needs.HungerCategory.Fed && p.InBed();
                if (hungryInBed || HealthAIUtility.HasHediffsNeedingTend(p)) (patients ??= new List<Thing>()).Add(p);
            }
            return AlertReport.CulpritsAre(patients);
        }

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These colonists need medical care, and nobody able to walk has Doctor work enabled. Enable Doctor " +
            "work for someone who can do it:" + CulpritList(report);
    }
}
