using SimWorld.Defs;

namespace SimWorld.Building
{
    /// <summary>Data half of every power comp (RimWorld: <c>RimWorld.CompProperties_Power</c>).</summary>
    public class CompProperties_Power : CompProperties
    {
        public CompProperties_Power()
        {
            compClass = typeof(CompPower);
        }
    }

    /// <summary>
    /// A Thing's presence on a <see cref="PowerNet"/> (RimWorld: <c>RimWorld.CompPower</c>). On its own this
    /// is a plain conductor with no transmit/consume/produce/store behaviour of its own —
    /// <see cref="CompPowerTransmitter"/>, <see cref="CompPowerTrader"/> and <see cref="CompPowerBattery"/>
    /// are what content actually points at.
    /// </summary>
    public class CompPower : Things.ThingComp
    {
        /// <summary>Set/cleared by <see cref="PowerNetManager"/>; never assigned directly.</summary>
        public PowerNet? powerNet;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.powerNetManager.Notify_CompSpawned(this);
        }

        public override void PostDeSpawn(Map.Map map)
        {
            map.powerNetManager.Notify_CompDeSpawned(this);
            base.PostDeSpawn(map);
        }
    }
}
