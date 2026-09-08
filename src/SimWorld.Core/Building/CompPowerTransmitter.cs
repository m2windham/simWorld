namespace SimWorld.Building
{
    public class CompProperties_PowerTransmitter : CompProperties_Power
    {
        public CompProperties_PowerTransmitter()
        {
            compClass = typeof(CompPowerTransmitter);
        }
    }

    /// <summary>A conduit: relays power between neighbours but neither draws nor produces any itself
    /// (RimWorld: <c>RimWorld.CompPowerTransmitter</c>).</summary>
    public sealed class CompPowerTransmitter : CompPower
    {
    }
}
