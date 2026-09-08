using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>
    /// Data half of <see cref="CompPowerTrader"/> (RimWorld: <c>RimWorld.CompProperties_Power</c> as a trader
    /// actually uses it — the one comp-properties class RimWorld shares between transmitter/trader/plant;
    /// this port splits transmitter's own properties out, see <see cref="CompProperties_PowerTransmitter"/>,
    /// so a non-transmitting comp's XML never has to carry a meaningless <c>basePowerConsumption</c>).
    /// </summary>
    public class CompProperties_PowerTrader : CompProperties_Power
    {
        /// <summary>
        /// Watts drawn from the net while <see cref="CompPowerTrader.PowerOn"/> — RimWorld's real convention,
        /// kept here: a <b>negative</b> value means this Thing produces that much instead of consuming it
        /// (<see cref="CompPowerPlant"/> content authors it negative). One signed field covers both, exactly
        /// like RimWorld's <c>CompPowerPlant : CompPowerTrader</c> inheritance.
        /// </summary>
        public float basePowerConsumption;

        public CompProperties_PowerTrader()
        {
            compClass = typeof(CompPowerTrader);
        }
    }

    /// <summary>
    /// A power consumer that can be switched off by the net's brownout policy (RimWorld: <c>RimWorld.CompPowerTrader</c>).
    /// A negative <see cref="CompProperties_PowerTrader.basePowerConsumption"/> makes this a producer instead —
    /// see <see cref="CompPowerPlant"/>, which is nothing more than this same comp under a distinct XML Class
    /// name for readability, matching RimWorld's own <c>CompPowerPlant : CompPowerTrader</c>.
    /// </summary>
    public class CompPowerTrader : CompPower
    {
        /// <summary>
        /// Whether this comp is actually drawing/producing right now. True by default; a consumer
        /// (<c>basePowerConsumption &gt; 0</c>) is the only kind <see cref="PowerNet.PowerNetTick"/> ever
        /// flips false, on a whole-net brownout — see that method's remarks for why that is a deliberate
        /// deviation from this module's brief, not an oversight.
        /// </summary>
        public bool powerOn = true;

        public CompProperties_PowerTrader Properties => (CompProperties_PowerTrader)props;

        public float BasePowerConsumption => Properties.basePowerConsumption;

        /// <summary>Positive while producing (consumption authored negative), negative while consuming,
        /// zero while switched off (RimWorld: <c>CompPowerTrader.PowerOutput</c>).</summary>
        public float PowerOutput => powerOn ? -BasePowerConsumption : 0f;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref powerOn, "powerOn", true);
        }
    }
}
