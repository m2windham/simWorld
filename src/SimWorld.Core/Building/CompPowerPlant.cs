namespace SimWorld.Building
{
    /// <summary>XML-Class-only distinction from <see cref="CompPowerTrader"/> so content reads clearly
    /// (RimWorld: <c>RimWorld.CompProperties_Power</c> again, same story as <see cref="CompProperties_PowerTrader"/>).</summary>
    public class CompProperties_PowerPlant : CompProperties_PowerTrader
    {
        public CompProperties_PowerPlant()
        {
            compClass = typeof(CompPowerPlant);
        }
    }

    /// <summary>
    /// A power producer (RimWorld: <c>RimWorld.CompPowerPlant : CompPowerTrader</c>). No fuel/intermittency
    /// model exists yet (no <c>CompRefuelable</c> in this codebase) — a plant always runs at full output
    /// while spawned; <see cref="CompPowerTrader.PowerOutput"/> already does the sign-flip that makes a
    /// negative <c>basePowerConsumption</c> mean production, so this class adds nothing behaviourally.
    /// </summary>
    public sealed class CompPowerPlant : CompPowerTrader
    {
    }
}
