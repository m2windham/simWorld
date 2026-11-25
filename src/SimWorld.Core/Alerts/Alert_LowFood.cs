using System;
using System.Globalization;

using SimWorld.AI;

namespace SimWorld.Alerts
{
    /// <summary>
    /// "Low food" (RimWorld: <c>RimWorld.Alert_LowFood</c>): the map holds less than
    /// <see cref="NutritionThresholdPerColonist"/> nutrition per colonist.
    ///
    /// <para/>RimWorld reads <c>map.resourceCounter.TotalHumanEdibleNutrition</c>. This port has no resource
    /// counter; the figure here is <see cref="HuntingInitiative.NutritionReachable"/>, everything edible
    /// lying on the map — the same number the hunters and the stock initiative already gate on, so the alert
    /// and the settlement's own reaction to hunger cannot disagree about how much food there is. It does
    /// not separate food only an animal would eat; RimWorld's does.
    /// </summary>
    public sealed class Alert_LowFood : Alert
    {
        /// <summary>RimWorld: <c>Alert_LowFood.NutritionThresholdPerColonist</c>.</summary>
        public const float NutritionThresholdPerColonist = 4f;

        public override AlertPriority Priority => AlertPriority.High;

        public override string DefaultLabel => "Low food";

        public override AlertReport GetReport(AlertContext context)
        {
            int colonists = context.Colonists.Count;
            if (colonists == 0) return AlertReport.Inactive;
            return HuntingInitiative.NutritionReachable(context.Map) < NutritionThresholdPerColonist * colonists
                ? AlertReport.ActiveWithoutCulprits
                : AlertReport.Inactive;
        }

        public override string GetExplanation(AlertContext context, AlertReport report)
        {
            float nutrition = HuntingInitiative.NutritionReachable(context.Map);
            int colonists = Math.Max(1, context.Colonists.Count);
            int perHead = (int)Math.Floor(nutrition / colonists);
            return "You are running low on food. There is " + nutrition.ToString("F0", CultureInfo.InvariantCulture) +
                " nutrition on the map for " + colonists.ToString(CultureInfo.InvariantCulture) +
                " citizens: " + perHead.ToString(CultureInfo.InvariantCulture) +
                " each. Hunt, harvest, gather or trade for more.";
        }
    }
}
