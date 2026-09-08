using System.Collections.Generic;

namespace SimWorld.Building
{
    /// <summary>
    /// One connected group of power comps sharing generation, consumption and storage (RimWorld:
    /// <c>RimWorld.PowerNet</c>). Built and maintained by <see cref="PowerNetManager"/>; never construct one
    /// directly.
    /// </summary>
    public sealed class PowerNet
    {
        internal readonly List<CompPower> connectors = new List<CompPower>();

        public IReadOnlyList<CompPower> Connectors => connectors;

        public IEnumerable<CompPowerTrader> Traders()
        {
            for (int i = 0; i < connectors.Count; i++)
            {
                if (connectors[i] is CompPowerTrader trader) yield return trader;
            }
        }

        public IEnumerable<CompPowerBattery> Batteries()
        {
            for (int i = 0; i < connectors.Count; i++)
            {
                if (connectors[i] is CompPowerBattery battery) yield return battery;
            }
        }

        /// <summary>Sum of every trader's (signed) <see cref="CompPowerTrader.PowerOutput"/> as things stand
        /// this instant — positive producers minus positive consumers, ignoring the brownout policy
        /// <see cref="PowerNetTick"/> is about to apply.</summary>
        public float CurrentEnergyGainRate()
        {
            float total = 0f;
            foreach (CompPowerTrader trader in Traders()) total += trader.PowerOutput;
            return total;
        }

        public float TotalStoredEnergy()
        {
            float total = 0f;
            foreach (CompPowerBattery battery in Batteries()) total += battery.storedEnergy;
            return total;
        }

        /// <summary>
        /// Advances this net by one tick (RimWorld: <c>PowerNet.PowerNetTick</c>).
        /// <b>Deviation from this module's brief:</b> the brief describes brownout as traders "shut off in a
        /// defined order." Real RimWorld's <c>PowerNet</c> does not selectively sacrifice devices by
        /// priority — once batteries cannot cover a shortfall, <i>every</i> consuming trader on the net loses
        /// power in the same tick, and every one of them regains it together the moment supply recovers.
        /// This port keeps that real, simultaneous whole-net brownout rather than inventing a selective
        /// shutoff order RimWorld does not have; see this module's report.
        /// </summary>
        public void PowerNetTick()
        {
            float generation = 0f;
            float consumption = 0f;
            foreach (CompPowerTrader trader in Traders())
            {
                float baseDraw = trader.BasePowerConsumption;
                if (baseDraw < 0f) generation += -baseDraw;
                else consumption += baseDraw;
            }

            float net = generation - consumption;
            if (net >= 0f)
            {
                DistributeSurplus(net);
                SetConsumersPowered(true);
                return;
            }

            float deficit = -net;
            float drained = DrainBatteries(deficit);
            bool fullyCovered = drained >= deficit - 0.0001f;
            SetConsumersPowered(fullyCovered);
        }

        private void DistributeSurplus(float surplus)
        {
            foreach (CompPowerBattery battery in Batteries())
            {
                if (surplus <= 0f) break;
                surplus -= battery.AddEnergy(surplus);
            }
            // Any surplus left over after every battery is full is wasted — RimWorld's real batteries do
            // the same; there is no elsewhere for it to go without a grid-export mechanic this pass has no
            // use for.
        }

        /// <summary>Drains up to <paramref name="amount"/> across every battery on the net, in connector
        /// order (RimWorld's own battery order is likewise just registration order, not a priority list —
        /// nothing in real RimWorld distinguishes one battery's discharge priority from another's).</summary>
        private float DrainBatteries(float amount)
        {
            float drained = 0f;
            foreach (CompPowerBattery battery in Batteries())
            {
                if (drained >= amount) break;
                drained += -battery.AddEnergy(-(amount - drained));
            }
            return drained;
        }

        private void SetConsumersPowered(bool on)
        {
            foreach (CompPowerTrader trader in Traders())
            {
                if (trader.BasePowerConsumption > 0f) trader.powerOn = on;
            }
        }
    }
}
