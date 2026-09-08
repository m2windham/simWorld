using SimWorld.Sim;

namespace SimWorld.Building
{
    public class CompProperties_PowerBattery : CompProperties_Power
    {
        /// <summary>Watt-days of storage (RimWorld's own unit for a battery's <c>storedEnergyMax</c>; the
        /// vanilla standard battery's real value, 10000, is what this port's own content uses).</summary>
        public float storedEnergyMax = 10000f;

        public CompProperties_PowerBattery()
        {
            compClass = typeof(CompPowerBattery);
        }
    }

    /// <summary>Stores and discharges energy for its net (RimWorld: <c>RimWorld.CompPowerBattery</c>).</summary>
    public class CompPowerBattery : CompPower
    {
        public float storedEnergy;

        public CompProperties_PowerBattery Properties => (CompProperties_PowerBattery)props;

        public float StoredEnergyMax => Properties.storedEnergyMax;

        /// <summary>
        /// Adds (positive) or removes (negative) energy, clamped to [0, <see cref="StoredEnergyMax"/>], and
        /// returns how much was actually absorbed/drained — the rest is surplus overflow (charging) or an
        /// unmet deficit (discharging) the caller (<see cref="PowerNet"/>) still has to account for.
        /// </summary>
        public float AddEnergy(float amount)
        {
            float before = storedEnergy;
            storedEnergy = GenMath.Clamp(storedEnergy + amount, 0f, StoredEnergyMax);
            return storedEnergy - before;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedEnergy, "storedEnergy");
        }
    }
}
