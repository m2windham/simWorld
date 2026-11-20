using System.Globalization;

using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;

namespace SimWorld.God.View
{
    /// <summary>
    /// Gifting to a visiting caravan's civilization (RimWorld: <c>FactionGiftUtility</c> — gifting goods or
    /// silver raises goodwill by their value). The third verb of the trade seam beside
    /// <see cref="BuyFromTrader"/> and <see cref="SellToTrader"/>, sharing their handles and their resolution.
    ///
    /// <para/><b>What is refused.</b> Only the impossible: not one of our settlements, no caravan there, no such
    /// good, giving what the settlement does not hold, a caravan that belongs to no civilization (there is
    /// nobody's goodwill to move), and a permanent enemy (RimWorld: such a faction cannot be gifted to; and
    /// <see cref="Faction.TryAffectGoodwillWith"/> will not move it either). A hostile or warring civilization
    /// <i>is</i> accepted — in RimWorld a gift is exactly how a soured relation is mended — and so is a gift
    /// too small to move goodwill at all, or one given to a partner already at 100. Those are unwise, not
    /// impossible.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Gives <paramref name="count"/> units of <paramref name="thingDefName"/> (silver included) out of
        /// <paramref name="settlementTile"/>'s store ledger to the caravan standing there, and raises goodwill
        /// with its civilization by <see cref="TradeTrust.GiftGoodwillChange"/> of their market value — the same
        /// function <see cref="TradeLineView.GoodwillIfAllGifted"/> and
        /// <see cref="VisitingTraderView.GoodwillIfAllSilverGifted"/> quote from.
        /// </summary>
        public static GodCommandResult GiftToTrader(int settlementTile, string thingDefName, int count)
        {
            GodCommandResult? failure = ResolveTrade(
                settlementTile, thingDefName, count,
                out World.Settlement settlement, out TraderCaravan caravan, out ThingDef def, gift: true);
            if (failure != null) return failure;
            if (count == 0) return GodCommandResult.NoChange("Nothing was given.");

            Faction? theirs = caravan.Faction;
            if (theirs == null)
            {
                return GodCommandResult.Refused(
                    "The caravan at " + settlement.name + " belongs to no civilization; there is nobody whose goodwill a gift could win.");
            }
            if (theirs.def.permanentEnemy)
            {
                return GodCommandResult.Refused(theirs.name + " is a permanent enemy — no gift will move them.");
            }

            int held = settlement.StoreCountOf(def);
            if (held <= 0)
            {
                return GodCommandResult.Refused(settlement.name + " holds no " + def.LabelCap + " to give.");
            }
            if (count > held)
            {
                return GodCommandResult.Refused(
                    settlement.name + " holds only " + TradeQuantity(held) + " " + def.LabelCap + ", not "
                    + TradeQuantity(count) + ".");
            }

            float value = TradeUtility.BaseMarketValue(def) * count;
            settlement.SetStoreCount(def, held - count);
            caravan.AddStock(def, count);
            int gained = TradeTrust.RecordGift(settlement.faction, theirs, value);

            return GodCommandResult.Done(
                settlement.name + " gave " + TradeQuantity(count) + " " + def.LabelCap + " (worth "
                + value.ToString("F0", CultureInfo.InvariantCulture) + " silver) to " + theirs.name + "."
                + (gained > 0 ? TrustGainNote(caravan, gained) : " Their goodwill did not change."));
        }
    }
}
