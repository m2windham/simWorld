using System;
using System.Collections.Generic;

namespace SimWorld.Economy
{
    /// <summary>
    /// One in-progress trade: every good on the table plus the currency line, and the silver check that
    /// gates committing it (RimWorld: <c>RimWorld.TradeDeal</c>). Silver is itself modeled as a
    /// <see cref="Tradeable"/> (see <see cref="silverTradeable"/>) so buying/selling goods and paying for
    /// them share the same count-transfer mechanism; the player's silver is abstracted as a count exactly
    /// like every other good, not tied to real Thing stacks.
    /// </summary>
    public class TradeDeal
    {
        public readonly List<Tradeable> tradeables = new List<Tradeable>();

        /// <summary>The currency line, if this deal has one set up (see <see cref="CurrencyTradeable"/>).</summary>
        public Tradeable? silverTradeable;

        public PriceType priceType = PriceType.Normal;

        public float negotiatorGain;

        public float settlementGain;

        public Tradeable? CurrencyTradeable => silverTradeable;

        public void AddTradeable(Tradeable tradeable)
        {
            if (tradeable == null) throw new ArgumentNullException(nameof(tradeable));
            tradeables.Add(tradeable);
        }

        /// <summary>Net silver the player must pay across every line (can be negative if selling nets more than buying costs).</summary>
        public float NetPlayerSilverCost()
        {
            float total = 0f;
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable t = tradeables[i];
                total += t.CurTotalCurrencyCostForSource(priceType, negotiatorGain, settlementGain);
                total -= t.CurTotalCurrencyCostForDestination(priceType, negotiatorGain, settlementGain);
            }
            return total;
        }

        /// <summary>Silver the player currently holds, from <see cref="silverTradeable"/>; 0 when no currency line is set up.</summary>
        public int PlayerSilverAvailable() => silverTradeable?.countInPlayer ?? 0;

        /// <summary>
        /// Commits every pending transfer if the player can afford the net cost, moving counts between
        /// <c>countOffered</c>/<c>countInPlayer</c> on each line (including the silver line) and resetting
        /// pending transfers on success. Refuses (and changes nothing) when the net cost exceeds
        /// <see cref="PlayerSilverAvailable"/>.
        /// </summary>
        public bool TryExecute(out bool actuallyTraded)
        {
            float netCost = NetPlayerSilverCost();
            if (netCost > PlayerSilverAvailable() + 0.001f)
            {
                actuallyTraded = false;
                return false;
            }

            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable t = tradeables[i];
                t.countOffered -= t.countToTransfer;
                t.countInPlayer += t.countToTransfer;
            }

            if (silverTradeable != null)
            {
                int silverMoved = (int)Math.Round(netCost);
                silverTradeable.countInPlayer -= silverMoved;
                silverTradeable.countOffered += silverMoved;
            }

            Reset();
            actuallyTraded = true;
            return true;
        }

        /// <summary>Zeroes every pending transfer without moving any counts.</summary>
        public void Reset()
        {
            for (int i = 0; i < tradeables.Count; i++)
            {
                tradeables[i].countToTransfer = 0;
            }
        }
    }
}
