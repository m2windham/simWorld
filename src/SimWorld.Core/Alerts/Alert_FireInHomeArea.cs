using System.Collections.Generic;

using SimWorld.Things;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Fire in home area" (RimWorld: <c>RimWorld.Alert_FireInHomeArea</c>, an <c>Alert_Critical</c>): a
    /// <see cref="Fire"/> burning on a cell of the home area — the area firefighters answer
    /// (<see cref="Building.AreaManager.Home"/>). RimWorld also skips fogged cells; this port has no fog of
    /// war, so every fire in the home area counts. Every such fire is a culprit, where RimWorld's points the
    /// camera at the first one.
    /// </summary>
    public sealed class Alert_FireInHomeArea : Alert
    {
        public override AlertPriority Priority => AlertPriority.Critical;

        public override string DefaultLabel => "Fire within the town bounds";

        public override AlertReport GetReport(AlertContext context)
        {
            Map.Map map = context.Map;
            IReadOnlyList<Thing> fires = map.listerThings.ThingsOfDef(FireThingDefOf.Fire);
            List<Thing>? burning = null;
            for (int i = 0; i < fires.Count; i++)
            {
                Thing fire = fires[i];
                if (fire.Spawned && map.areaManager.Home[fire.Position]) (burning ??= new List<Thing>()).Add(fire);
            }
            return AlertReport.CulpritsAre(burning);
        }

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "There is a fire within the town bounds. Citizens on fire watch will try to put it out; if " +
            "nobody does, it will spread.";
    }
}
