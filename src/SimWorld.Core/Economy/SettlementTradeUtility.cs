using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// Wires a <see cref="TradeSession"/> to a real <see cref="Settlement"/>'s store ledger (spec: "a trade
    /// with a settlement should move real stock, not a notional number"). This module does not own
    /// <c>World/Settlement.cs</c> (see CLAUDE.md's lane boundaries for this pass), so rather than requiring
    /// <see cref="Settlement"/> to implement some trade-specific interface, this composes over its existing
    /// public store API (<see cref="Settlement.StoreCountOf"/>/<see cref="Settlement.SetStoreCount"/>) —
    /// exactly the same "read the real thing, don't reinvent it" approach <see cref="CoalSupply"/> already
    /// takes with <c>Settlement.StoreCountOf</c>.
    /// <para/>
    /// <see cref="TradeSession"/>'s own doc already says callers add the player's own goods and the silver
    /// line before trading; this is that step for the settlement-backed case, on both ends of a completed
    /// deal — seeding <em>and</em> writing back.
    /// </summary>
    public static class SettlementTradeUtility
    {
        /// <summary>
        /// Opens a session against <paramref name="trader"/>, then overwrites every line's
        /// <c>countInPlayer</c> — every ordinary good and the silver line, when one exists — with
        /// <paramref name="buyer"/>'s real current stock, and guarantees a silver line exists at all (a
        /// trader need not itself stock <see cref="EconomyThingDefOf.Silver"/> among <see cref="ITrader.Goods"/>
        /// for the buyer to be able to pay with it — see the class doc). <paramref name="tradeAccessGain"/>
        /// (an active <c>tradeAccess</c> treaty's price gain — see <see cref="Factions.Faction.TradeAccessPriceGainWith"/>)
        /// feeds <see cref="TradeDeal.settlementGain"/>, the same discount slot a settlement's own negotiator
        /// gain already occupies.
        /// </summary>
        public static TradeSession OpenSession(Settlement buyer, ITrader trader, Pawn? negotiator, float tradeAccessGain = 0f)
        {
            if (buyer == null) throw new ArgumentNullException(nameof(buyer));
            if (trader == null) throw new ArgumentNullException(nameof(trader));

            var session = new TradeSession();
            session.SetupWith(trader, negotiator);
            session.deal.settlementGain = GenMath.Clamp(tradeAccessGain, 0f, 1f);

            foreach (Tradeable t in session.deal.tradeables)
            {
                t.countInPlayer = buyer.StoreCountOf(t.thingDef);
            }

            session.deal.silverTradeable ??= new Tradeable(EconomyThingDefOf.Silver, 0, 0);
            session.deal.silverTradeable.countInPlayer = buyer.StoreCountOf(EconomyThingDefOf.Silver);

            return session;
        }

        /// <summary>
        /// Commits <paramref name="session"/>'s pending transfers (<see cref="TradeDeal.TryExecute"/>) and,
        /// only on success, writes the result back into real stores: <paramref name="buyer"/> always, and
        /// <paramref name="seller"/> too when it is a real settlement rather than an ephemeral visiting
        /// trader (<paramref name="seller"/> null — e.g. <see cref="Director.IncidentWorker_TraderCaravanArrival"/>'s
        /// generated caravan, which carries no backing store to persist into: whatever it doesn't sell simply
        /// leaves with it, same as a real caravan trader). Returns false (nothing written) exactly when
        /// <see cref="TradeDeal.TryExecute"/> does.
        /// </summary>
        public static bool TryExecute(TradeSession session, Settlement buyer, Settlement? seller, out bool actuallyTraded)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (buyer == null) throw new ArgumentNullException(nameof(buyer));

            if (!session.deal.TryExecute(out actuallyTraded))
            {
                return false;
            }

            foreach (Tradeable t in session.deal.tradeables)
            {
                buyer.SetStoreCount(t.thingDef, t.countInPlayer);
                seller?.SetStoreCount(t.thingDef, t.countOffered);
            }

            Tradeable? silver = session.deal.silverTradeable;
            if (silver != null)
            {
                buyer.SetStoreCount(EconomyThingDefOf.Silver, silver.countInPlayer);
                seller?.SetStoreCount(EconomyThingDefOf.Silver, silver.countOffered);
            }

            return true;
        }

        /// <summary>
        /// Wraps <paramref name="settlement"/>'s own real stores as an <see cref="ITrader"/> another
        /// settlement's caravan can open a session against — the seller side of a settlement-to-settlement
        /// trade route (spec: "trade routes between settlements"). A snapshot at the moment it is built,
        /// same as <see cref="OpenSession"/>'s own <c>countInPlayer</c> seeding: nothing here stays
        /// live-linked to <paramref name="settlement"/> afterward, so <see cref="TryExecute"/> — passing
        /// <paramref name="settlement"/> as its own <c>seller</c> — is what writes the real result back once
        /// a deal actually completes.
        /// </summary>
        public static ITrader AsTrader(Settlement settlement, TraderKindDef traderKind)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (traderKind == null) throw new ArgumentNullException(nameof(traderKind));

            var trader = new SettlementTrader(traderKind, settlement.faction);
            foreach (KeyValuePair<ThingDef, int> kv in settlement.Stores)
            {
                trader.goods.Add((kv.Key, kv.Value));
            }
            return trader;
        }
    }
}
