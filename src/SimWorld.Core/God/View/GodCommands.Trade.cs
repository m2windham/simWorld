using System;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Economy;

namespace SimWorld.God.View
{
    /// <summary>
    /// The write half of the trade seam — see <c>GodViewSnapshot.Trade.cs</c> for the read half.
    ///
    /// <para/><b>The defect this closes, and it is the one the audit named.</b>
    /// <c>docs/design/player-first.md</c> §10 lists <c>Economy.SettlementTradeUtility.OpenSession</c> under
    /// "built, tested, and never called from <c>src/</c>", with the note "a trader arrives with real stock and
    /// no session ever opens". Everything that decides a trade already existed — prices
    /// (<c>Economy.TradeUtility</c>), the deal and its affordability check (<c>Economy.TradeDeal</c>), the
    /// write-back into a settlement's real store ledger (<c>Economy.SettlementTradeUtility.TryExecute</c>).
    /// The two missing pieces were somewhere for a trader to be (<c>Economy.TraderCaravan</c>) and somebody to
    /// ask. These are the asking.
    ///
    /// <para/><b>Nothing here reimplements a price or moves a count.</b> Both methods do the same four things:
    /// resolve the handles the host was given, refuse what is impossible and say why, set one
    /// <c>Economy.Tradeable.countToTransfer</c>, and hand the session to
    /// <c>Economy.SettlementTradeUtility.TryExecute</c>. The only arithmetic below is
    /// <c>TradeDeal.NetPlayerSilverCost</c> read back out in order to <i>explain</i> a refusal in the same
    /// numbers the deal itself used — deliberately the deal's own expression rather than a second one, because
    /// a refusal re-derived at the seam is a refusal that drifts from the rule it describes
    /// (<c>docs/design/player-first.md</c> §5).
    ///
    /// <para/><b>What is refused, and what is emphatically not.</b> Only the impossible: no settlement on that
    /// tile, no caravan standing there, a good nothing in content names, buying more than the caravan carries,
    /// selling more than the settlement holds, and either side being unable to pay. A trade that is merely
    /// <i>ruinous</i> — selling the last of the food at a bad price, spending the treasury down to nothing on
    /// something the settlement will not need — is carried out exactly as asked. That is the rule this
    /// project holds every command to, and trade is where it bites hardest: selling your winter stores at a
    /// discount because a caravan is here now and winter is not is one of the better decisions in this game to
    /// be allowed to regret.
    ///
    /// <para/><b>The one seam limitation, stated rather than hidden.</b> <see cref="GodCommandOutcome"/> has
    /// no member for "no such good", and it lives in <c>GodCommands.cs</c>, a file three other lanes are in
    /// this round (CLAUDE.md: add a file rather than edit a shared one). An unknown defName therefore comes
    /// back as <see cref="GodCommandOutcome.Refused"/> with a reason that says so in words, rather than as a
    /// distinct outcome a host could switch on. A host that wants to tell a stale handle from a real refusal
    /// can do it on the text today and on a new outcome member whenever that file is free to change.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Buys <paramref name="count"/> units of <paramref name="thingDefName"/> from the caravan standing at
        /// <paramref name="settlementTile"/>, paying out of that settlement's own silver and adding the goods
        /// to its own store ledger.
        ///
        /// <para/>Both handles come straight off the snapshot:
        /// <see cref="VisitingTraderView.SettlementTile"/> and <see cref="TradeLineView.DefName"/>. The price
        /// charged is <see cref="TradeLineView.UnitBuyPrice"/> times <paramref name="count"/>, because the
        /// quote and the bill are the same session opened the same way
        /// (<c>Economy.VisitingTraderTrade.OpenSessionWith</c>).
        /// </summary>
        public static GodCommandResult BuyFromTrader(int settlementTile, string thingDefName, int count)
        {
            GodCommandResult? failure = ResolveTrade(
                settlementTile, thingDefName, count,
                out World.Settlement settlement, out TraderCaravan caravan, out ThingDef def);
            if (failure != null) return failure;
            if (count == 0) return GodCommandResult.NoChange("Nothing was asked for.");

            int available = caravan.StockOf(def);
            if (available <= 0)
            {
                return GodCommandResult.Refused(
                    "The caravan at " + settlement.name + " carries no " + def.LabelCap + ".");
            }
            if (count > available)
            {
                return GodCommandResult.Refused(
                    "The caravan at " + settlement.name + " carries only " + TradeQuantity(available) + " "
                    + def.LabelCap + ", not " + TradeQuantity(count) + ".");
            }

            TradeSession session = VisitingTraderTrade.OpenSessionWith(settlement, caravan);
            Tradeable line = VisitingTraderTrade.LineFor(session, settlement, caravan, def);
            line.countToTransfer = count;

            float cost = session.deal.NetPlayerSilverCost();
            int silver = session.deal.PlayerSilverAvailable();
            if (cost > silver + 0.001f)
            {
                return GodCommandResult.Refused(
                    settlement.name + " cannot afford that: " + TradeQuantity(count) + " " + def.LabelCap + " costs "
                    + TradeSilverAmount(cost) + " silver and it holds " + TradeQuantity(silver) + ".");
            }

            if (!SettlementTradeUtility.TryExecute(session, settlement, null, out bool traded) || !traded)
            {
                // Unreachable while TradeDeal's only refusal is the affordability check just made against its
                // own expression. Kept rather than asserted away for the same reason IssueEdict keeps its
                // twin: "the simulation said no after saying yes" is worth surfacing, and a silent success
                // here would be indistinguishable from a real one to the host.
                return GodCommandResult.Refused("The deal with the caravan at " + settlement.name + " did not complete.");
            }

            VisitingTraderTrade.WriteStockBack(caravan, session);
            return GodCommandResult.Done(
                settlement.name + " bought " + TradeQuantity(count) + " " + def.LabelCap + " for "
                + TradeSilverAmount(cost) + " silver.");
        }

        /// <summary>
        /// Sells <paramref name="count"/> units of <paramref name="thingDefName"/> out of
        /// <paramref name="settlementTile"/>'s store ledger to the caravan standing there, at
        /// <see cref="TradeLineView.UnitSellPrice"/> each.
        ///
        /// <para/><b>A caravan will buy anything the settlement has</b>, not only what it turned up carrying —
        /// <c>Economy.VisitingTraderTrade.LineFor</c> adds a line for a good the caravan does not stock, which
        /// is the step <c>Economy.TradeSession</c>'s own doc describes ("callers add the player's own goods
        /// ... before trading"). RimWorld gates this on per-trader-kind categories; this port has no
        /// equivalent of those at all (see <c>Economy.StockGenerator</c>'s doc on dropping the category and
        /// money filtering), so inventing one here would be a rule with nothing behind it.
        ///
        /// <para/><b>What it refuses.</b> Selling what the settlement does not have, and selling more than the
        /// caravan can pay for — a trader with three hundred silver cannot buy a thousand silver of grain, and
        /// that is a fact about the caravan rather than a judgement about the trade. Selling goods the
        /// settlement will bitterly need is not refused, and is the point.
        /// </summary>
        public static GodCommandResult SellToTrader(int settlementTile, string thingDefName, int count)
        {
            GodCommandResult? failure = ResolveTrade(
                settlementTile, thingDefName, count,
                out World.Settlement settlement, out TraderCaravan caravan, out ThingDef def);
            if (failure != null) return failure;
            if (count == 0) return GodCommandResult.NoChange("Nothing was offered.");

            int held = settlement.StoreCountOf(def);
            if (held <= 0)
            {
                return GodCommandResult.Refused(settlement.name + " holds no " + def.LabelCap + " to sell.");
            }
            if (count > held)
            {
                return GodCommandResult.Refused(
                    settlement.name + " holds only " + TradeQuantity(held) + " " + def.LabelCap + ", not "
                    + TradeQuantity(count) + ".");
            }

            TradeSession session = VisitingTraderTrade.OpenSessionWith(settlement, caravan);
            Tradeable line = VisitingTraderTrade.LineFor(session, settlement, caravan, def);
            line.countToTransfer = -count;

            float net = session.deal.NetPlayerSilverCost();

            // The caravan's side of the same rounding TradeDeal.TryExecute will do, asked before rather than
            // after: a sale nets the player -net silver, and every one of those comes out of the caravan's own
            // purse. Without this the purse would simply go negative and the settlement would be paid with
            // silver that never existed — an over-claim of exactly the kind this repository has been bitten by
            // before, and invisible because nothing else reads a trader's silver.
            int owed = -(int)Math.Round(net);
            int purse = session.deal.silverTradeable?.countOffered ?? 0;
            if (owed > purse)
            {
                return GodCommandResult.Refused(
                    "The caravan at " + settlement.name + " carries only " + TradeQuantity(purse)
                    + " silver; " + TradeQuantity(count) + " " + def.LabelCap + " is worth " + TradeQuantity(owed) + ".");
            }

            if (!SettlementTradeUtility.TryExecute(session, settlement, null, out bool traded) || !traded)
            {
                // Unreachable, for the same reason as its twin in BuyFromTrader and one more: a pure sale can
                // only ever have a net cost of zero or less, and TradeDeal refuses only a net cost above the
                // settlement's silver — which is never negative. Kept rather than asserted away, because
                // reporting Done for a transfer that did not happen is the single failure mode this
                // repository has been bitten by most often.
                return GodCommandResult.Refused("The deal with the caravan at " + settlement.name + " did not complete.");
            }

            VisitingTraderTrade.WriteStockBack(caravan, session);
            return GodCommandResult.Done(
                settlement.name + " sold " + TradeQuantity(count) + " " + def.LabelCap + " for "
                + TradeQuantity(owed) + " silver.");
        }

        /// <summary>
        /// The three handles both commands need, and every refusal that does not depend on which direction the
        /// goods are moving. Returns null when everything resolved; the out parameters are only meaningful
        /// then.
        /// </summary>
        private static GodCommandResult? ResolveTrade(
            int settlementTile,
            string thingDefName,
            int count,
            out World.Settlement settlement,
            out TraderCaravan caravan,
            out ThingDef def)
        {
            settlement = null!;
            caravan = null!;
            def = null!;

            World.Settlement? found = AttentionManager.SettlementAt(settlementTile);
            if (found == null) return GodCommandResult.UnknownSettlement(settlementTile);
            settlement = found;

            // Departed caravans are swept from the write paths and never from a read — see
            // Economy.TraderArrival.PruneDeparted for why the sweep cannot live in a world tick and why the
            // litter it collects is bounded at one. Ahead of the lookup rather than after it, so the sweep
            // happens on the "nobody is here" path too, which is the commonest one to be called on. It can
            // never take the caravan out from under the lookup: it only removes caravans whose stay is over,
            // and those are exactly the ones the lookup already refuses to return.
            TraderArrival.PruneDeparted();

            TraderCaravan? present = TraderArrival.At(settlementTile);
            if (present == null)
            {
                return GodCommandResult.Refused(
                    "No trade caravan is at " + settlement.name + " — one may have left already.");
            }
            caravan = present;

            if (string.IsNullOrEmpty(thingDefName))
            {
                return GodCommandResult.Refused("No good was named.");
            }

            ThingDef? resolved = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (resolved == null)
            {
                // See the class doc: no GodCommandOutcome member exists for this and the file that declares
                // them is not this lane's to edit.
                return GodCommandResult.Refused("No good named '" + thingDefName + "'.");
            }
            def = resolved;

            if (ReferenceEquals(def, EconomyThingDefOf.Silver))
            {
                return GodCommandResult.Refused(
                    "Silver is what a trade is paid in, not something to trade for it.");
            }

            if (count < 0)
            {
                return GodCommandResult.Refused(
                    "A trade moves goods one way: " + TradeQuantity(count) + " is not a quantity. Use the other command.");
            }

            return null;
        }

        private static string TradeQuantity(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string TradeSilverAmount(float value) => value.ToString("F0", CultureInfo.InvariantCulture);
    }
}
