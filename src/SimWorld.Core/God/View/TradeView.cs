using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// One good on the table between a settlement and a visiting caravan: how many each side has of it, and
    /// what it costs in each direction.
    ///
    /// <para/><b>One row covers buying and selling, on purpose.</b> "This is their stock" and "this is what
    /// they want" are the same table read down two different columns, and splitting them into two lists would
    /// mean a good both sides hold appears twice and a player has to reconcile them. A row with
    /// <see cref="CountTraderHas"/> above zero is something to buy; a row with
    /// <see cref="CountSettlementHas"/> above zero is something to sell; most interesting rows are both.
    ///
    /// <para/><b>Both prices are the prices that will actually be charged.</b> They come off the very same
    /// <c>Economy.TradeSession</c> the command surface opens to execute a trade
    /// (<c>Economy.VisitingTraderTrade.OpenSessionWith</c>), with the settlement's negotiator and any trade
    /// treaty already applied — not a second calculation performed at the seam, which is how a quote starts
    /// drifting from a bill.
    /// </summary>
    public sealed class TradeLineView
    {
        internal TradeLineView(
            string defName,
            string label,
            int countTraderHas,
            int countSettlementHas,
            float unitBuyPrice,
            float unitSellPrice)
        {
            DefName = defName;
            Label = label;
            CountTraderHas = countTraderHas;
            CountSettlementHas = countSettlementHas;
            UnitBuyPrice = unitBuyPrice;
            UnitSellPrice = unitSellPrice;
        }

        /// <summary>The handle to pass back to <see cref="GodCommands.BuyFromTrader"/> /
        /// <see cref="GodCommands.SellToTrader"/>. Not a Def — see <see cref="GodViewSnapshot"/> on why the
        /// seam is a string.</summary>
        public string DefName { get; }

        public string Label { get; }

        /// <summary>How many of this the caravan is offering. Zero means the settlement can sell it but not
        /// buy it.</summary>
        public int CountTraderHas { get; }

        /// <summary>How many of this the settlement's own ledger holds. Zero means it can be bought but there
        /// is none to sell.</summary>
        public int CountSettlementHas { get; }

        /// <summary>Silver per unit the settlement pays to buy one.</summary>
        public float UnitBuyPrice { get; }

        /// <summary>Silver per unit the settlement receives for selling one. Always below
        /// <see cref="UnitBuyPrice"/> — a trader lives on that gap, and the gap is
        /// <c>Economy.TradeUtility</c>'s, not this view's.</summary>
        public float UnitSellPrice { get; }
    }

    /// <summary>
    /// A trade caravan that is standing at one of the civilization's settlements right now: who they are, how
    /// long they are staying, and the whole table of what can move in either direction.
    ///
    /// <para/><b>This is civilization scale, which is why it is on <see cref="GodViewSnapshot"/> and not on a
    /// settlement's interior view.</b> A caravan arriving is a fact about the civilization's relations and its
    /// stores — the thing a god decides about — not about the arrangement of buildings inside one town. It
    /// carries no <c>Thing</c>s and no <c>Def</c>s: a trader's stock crosses the seam as defNames and counts,
    /// which is what lets the host render a trade screen without being able to reach a worker through
    /// anything on it.
    /// </summary>
    public sealed class VisitingTraderView
    {
        internal VisitingTraderView(
            int settlementTile,
            string settlementName,
            string traderKindDefName,
            string traderKindLabel,
            string? factionName,
            int arrivalTick,
            int departureTick,
            int ticksUntilDeparture,
            int traderSilver,
            int settlementSilver,
            string? negotiatorName,
            float negotiatorPriceGain,
            float treatyPriceGain,
            IReadOnlyList<TradeLineView> goods)
        {
            SettlementTile = settlementTile;
            SettlementName = settlementName;
            TraderKindDefName = traderKindDefName;
            TraderKindLabel = traderKindLabel;
            FactionName = factionName;
            ArrivalTick = arrivalTick;
            DepartureTick = departureTick;
            TicksUntilDeparture = ticksUntilDeparture;
            TraderSilver = traderSilver;
            SettlementSilver = settlementSilver;
            NegotiatorName = negotiatorName;
            NegotiatorPriceGain = negotiatorPriceGain;
            TreatyPriceGain = treatyPriceGain;
            Goods = goods;
        }

        /// <summary>The world tile of the settlement being visited — the handle both trade commands take, the
        /// same one <see cref="SettlementSummary.Tile"/> already carries. At most one caravan stands at a
        /// settlement at a time (<c>Economy.TraderArrival.Land</c>), so this names exactly one trader.</summary>
        public int SettlementTile { get; }

        public string SettlementName { get; }

        public string TraderKindDefName { get; }

        public string TraderKindLabel { get; }

        /// <summary>Whose caravan this is, or null for a trader belonging to no civilization.</summary>
        public string? FactionName { get; }

        public int ArrivalTick { get; }

        /// <summary>The tick they leave. A host can turn this into a date with the same
        /// <c>GenDate.DateReadoutStringAt</c> that formatted <see cref="GodViewSnapshot.DateLabel"/>.</summary>
        public int DepartureTick { get; }

        /// <summary>How long is left to decide. This is the cost of not deciding — see
        /// <c>docs/design/player-first.md</c> §5: a cost the player can shrug off is not a decision, and a
        /// trader who waits forever is exactly that.</summary>
        public int TicksUntilDeparture { get; }

        /// <summary>Silver the caravan is carrying, which bounds what it can pay for anything sold to it.</summary>
        public int TraderSilver { get; }

        /// <summary>Silver the settlement holds, which bounds what it can buy.</summary>
        public int SettlementSilver { get; }

        /// <summary>The citizen doing the talking, or null when nobody on the live roster is left to do it —
        /// see <c>Economy.VisitingTraderTrade.BestNegotiator</c>. Reported because the prices below depend on
        /// them, and a player who can see why a price moved can act on it.</summary>
        public string? NegotiatorName { get; }

        /// <summary>The share the negotiator's Social skill takes off a buy and adds to a sell, 0-1.</summary>
        public float NegotiatorPriceGain { get; }

        /// <summary>The same, from an active trade-access treaty between the two civilizations.</summary>
        public float TreatyPriceGain { get; }

        /// <summary>Every good either side has, trader stock first in the order the caravan generated it, then
        /// the settlement's own goods the caravan did not bring, by defName. Ordered, never a set: two
        /// identically-seeded runs draw the same table in the same order.</summary>
        public IReadOnlyList<TradeLineView> Goods { get; }

        /// <summary>
        /// Every caravan standing at a settlement of the running world right now.
        ///
        /// <para/>A caravan whose settlement no longer exists is skipped rather than reported with a missing
        /// host: there is nobody there to trade with, which is the same thing
        /// <see cref="GodCommandOutcome.UnknownSettlement"/> says on the write side about a stale tile.
        /// </summary>
        internal static List<VisitingTraderView> PresentNow()
        {
            var views = new List<VisitingTraderView>();
            List<TraderCaravan> caravans = TraderArrival.PresentNow();
            for (int i = 0; i < caravans.Count; i++)
            {
                World.Settlement? host = AttentionManager.SettlementAt(caravans[i].tile);
                if (host == null) continue;
                views.Add(Of(host, caravans[i]));
            }
            return views;
        }

        /// <summary>The whole table for one settlement and one caravan, quoted off a real session.</summary>
        internal static VisitingTraderView Of(World.Settlement host, TraderCaravan caravan)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (caravan == null) throw new ArgumentNullException(nameof(caravan));

            TradeSession session = VisitingTraderTrade.OpenSessionWith(host, caravan);
            TradeDeal deal = session.deal;

            var goods = new List<TradeLineView>(deal.tradeables.Count);
            var listed = new HashSet<ThingDef>();
            for (int i = 0; i < deal.tradeables.Count; i++)
            {
                Tradeable line = deal.tradeables[i];
                listed.Add(line.thingDef);
                goods.Add(LineOf(line, deal));
            }

            // What the settlement could sell that the caravan did not turn up carrying. A trader buys what it
            // is offered — RimWorld gates this on TraderKindDef.willTrade categories, which this port has no
            // equivalent of at all (see StockGenerator's own doc on dropping the money/category filtering), so
            // the honest answer here is "anything you have". Sorted by defName rather than taken in ledger
            // order: a Dictionary's enumeration order is an implementation detail and this table is something
            // a player reads.
            var extras = new List<ThingDef>();
            foreach (KeyValuePair<ThingDef, int> entry in host.Stores)
            {
                if (entry.Value <= 0) continue;
                if (ReferenceEquals(entry.Key, EconomyThingDefOf.Silver)) continue;
                if (listed.Contains(entry.Key)) continue;
                extras.Add(entry.Key);
            }
            extras.Sort(static (a, b) => string.CompareOrdinal(a.defName, b.defName));
            for (int i = 0; i < extras.Count; i++)
            {
                goods.Add(LineOf(new Tradeable(extras[i], 0, host.StoreCountOf(extras[i])), deal));
            }

            Pawn? negotiator = VisitingTraderTrade.BestNegotiator(host);

            return new VisitingTraderView(
                host.tile,
                host.name,
                caravan.TraderKind.defName,
                caravan.TraderKind.LabelCap,
                caravan.Faction?.name,
                caravan.ArrivalTick,
                caravan.DepartureTick,
                caravan.TicksUntilDeparture,
                caravan.StockOf(EconomyThingDefOf.Silver),
                host.StoreCountOf(EconomyThingDefOf.Silver),
                negotiator?.Label,
                deal.negotiatorGain,
                deal.settlementGain,
                goods);
        }

        private static TradeLineView LineOf(Tradeable line, TradeDeal deal) =>
            new TradeLineView(
                line.thingDef.defName,
                line.thingDef.LabelCap,
                line.countOffered,
                line.countInPlayer,
                line.PriceFor(TradeAction.Buy, deal.priceType, deal.negotiatorGain, deal.settlementGain),
                line.PriceFor(TradeAction.Sell, deal.priceType, deal.negotiatorGain, deal.settlementGain));
    }
}
