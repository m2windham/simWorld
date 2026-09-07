using System;
using SimWorld.Defs;

namespace SimWorld.Economy
{
    /// <summary>Which side of the counter a price is quoted for.</summary>
    public enum TradeAction
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// One good on the trade table: how much the trader has, how much the player has, and how much is
    /// about to move (RimWorld: <c>RimWorld.Tradeable</c>, simplified to plain counts rather than backing
    /// Thing stacks — no Things module exists yet for this to hold real references to).
    /// </summary>
    public class Tradeable
    {
        public readonly ThingDef thingDef;

        /// <summary>How many the trader is offering.</summary>
        public int countOffered;

        /// <summary>How many the player currently has (abstracted as a count).</summary>
        public int countInPlayer;

        /// <summary>Pending transfer: positive buys from the trader, negative sells to the trader. Zeroed by <see cref="TradeDeal.Reset"/>.</summary>
        public int countToTransfer;

        public Tradeable(ThingDef thingDef, int countOffered, int countInPlayer)
        {
            this.thingDef = thingDef ?? throw new ArgumentNullException(nameof(thingDef));
            this.countOffered = countOffered;
            this.countInPlayer = countInPlayer;
        }

        /// <summary>Per-unit price for buying or selling this good under the given terms.</summary>
        public float PriceFor(TradeAction action, PriceType priceType = PriceType.Normal, float negotiatorGain = 0f, float settlementGain = 0f)
        {
            return action == TradeAction.Buy
                ? TradeUtility.GetPricePlayerBuy(thingDef, priceType, 1f, negotiatorGain, settlementGain)
                : TradeUtility.GetPricePlayerSell(thingDef, priceType, 1f, negotiatorGain, settlementGain);
        }

        /// <summary>Silver the player must pay for the pending buy on this line (0 when not buying).</summary>
        public float CurTotalCurrencyCostForSource(PriceType priceType = PriceType.Normal, float negotiatorGain = 0f, float settlementGain = 0f)
        {
            return countToTransfer > 0 ? PriceFor(TradeAction.Buy, priceType, negotiatorGain, settlementGain) * countToTransfer : 0f;
        }

        /// <summary>Silver the player receives for the pending sell on this line (0 when not selling).</summary>
        public float CurTotalCurrencyCostForDestination(PriceType priceType = PriceType.Normal, float negotiatorGain = 0f, float settlementGain = 0f)
        {
            return countToTransfer < 0 ? PriceFor(TradeAction.Sell, priceType, negotiatorGain, settlementGain) * -countToTransfer : 0f;
        }
    }
}
