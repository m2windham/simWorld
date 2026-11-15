namespace SimWorld.Alerts
{
    /// <summary>
    /// "Starvation" (RimWorld: <c>RimWorld.Alert_StarvationColonists</c>): colonists whose food need has hit
    /// bottom (<see cref="Needs.Need_Food.Starving"/>) and who are building malnutrition by the hour. The
    /// explanation is RimWorld's in substance, not its exact string table entry.
    /// </summary>
    public sealed class Alert_StarvationColonists : Alert
    {
        public override AlertPriority Priority => AlertPriority.High;

        public override string DefaultLabel => "Starvation";

        public override AlertReport GetReport(AlertContext context) =>
            ColonistsWhere(context, p => p.needs?.food != null && p.needs.food.Starving);

        public override string GetExplanation(AlertContext context, AlertReport report) =>
            "These colonists are starving. Every hour without food deepens their malnutrition, which will " +
            "eventually kill them. Get them food:" + CulpritList(report);
    }
}
