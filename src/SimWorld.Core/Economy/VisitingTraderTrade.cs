using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// One settlement doing business with one arrived <see cref="TraderCaravan"/>.
    ///
    /// <para/><b>This deliberately computes no price and moves no goods.</b>
    /// <see cref="SettlementTradeUtility"/> already decides both, and <see cref="TradeUtility"/> already owns
    /// the pricing curve, the buy markup, the sell discount and the floor. Everything here is the three
    /// things that were genuinely missing between "a caravan is standing there" and "the ledger changed":
    /// <list type="number">
    /// <item><b>Who negotiates</b> (<see cref="BestNegotiator"/>) — RimWorld makes the player pick a pawn and
    /// prices the deal off their Social skill; a god looking at a civilization picks no pawn, so the
    /// settlement fields its best talker.</item>
    /// <item><b>What treaty terms apply</b> (<see cref="OpenSessionWith"/>) — the
    /// <c>Factions.Faction.TradeAccessPriceGainWith</c> slot <see cref="SettlementTradeUtility.OpenSession"/>
    /// already accepts and which nothing ever filled.</item>
    /// <item><b>Where the trader's own stock goes afterwards</b> (<see cref="WriteStockBack"/>) — a visiting
    /// caravan has no <see cref="Settlement"/> ledger for <see cref="SettlementTradeUtility.TryExecute"/> to
    /// write its side into, so without this a player could buy the same fifty steel all afternoon.</item>
    /// </list>
    ///
    /// <para/><b>One session-opening path, read and write alike.</b> The god view quotes prices by opening a
    /// session through <see cref="OpenSessionWith"/> and reading it, and the command surface executes by
    /// opening one the same way. That is not duplication to be factored out later — it is the reason a quoted
    /// price is the price actually charged. <c>docs/design/player-first.md</c> §5: "an explanation re-derived
    /// at the seam is an explanation that will drift from the rule it describes"; a re-derived <i>price</i> is
    /// worse, because the player only finds out after paying it.
    /// </summary>
    public static class VisitingTraderTrade
    {
        /// <summary>
        /// The citizen who will do the talking: the settlement's highest Social skill, ties broken by
        /// <c>Pawn.thingIDNumber</c> so the answer never depends on roster order and two identically-seeded
        /// runs quote identical prices. Null for a settlement with nobody left alive on its live roster, which
        /// <see cref="TradeSession.SetupWith"/> already reads as a Social level of zero — no gain, no penalty.
        ///
        /// <para/><b>Only the live roster is considered, and that is honest rather than a shortcut.</b>
        /// <see cref="Settlement.Citizens"/> holds the Full/Interval slice; the Statistical cohort is a bare
        /// count with no <c>Pawn</c> behind it (that tier's own design), so it has no Social skill to read.
        /// A settlement whose people are all Statistical negotiates at zero gain, which is the same thing the
        /// tier already says about them everywhere else: they are a number, not a roster.
        /// </summary>
        public static Pawn? BestNegotiator(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            Pawn? best = null;
            int bestLevel = -1;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn pawn = citizens[i];
                if (pawn.Dead) continue;

                int level = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (best == null
                    || level > bestLevel
                    || (level == bestLevel && pawn.thingIDNumber < best.thingIDNumber))
                {
                    best = pawn;
                    bestLevel = level;
                }
            }
            return best;
        }

        /// <summary>
        /// Opens a <see cref="TradeSession"/> between <paramref name="settlement"/>'s real store ledger and
        /// <paramref name="caravan"/>'s real stock, with the settlement's best negotiator and whatever trade
        /// treaty the two civilizations have signed already applied.
        ///
        /// <para/>This is <see cref="SettlementTradeUtility.OpenSession"/> with its two open parameters
        /// filled, and nothing else. It is the caller the audit in <c>docs/design/player-first.md</c> §10
        /// records as missing: "<c>SettlementTradeUtility.OpenSession</c> — a trader arrives with real stock
        /// and no session ever opens."
        /// </summary>
        public static TradeSession OpenSessionWith(Settlement settlement, TraderCaravan caravan)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (caravan == null) throw new ArgumentNullException(nameof(caravan));

            float treatyGain =
                settlement.faction != null && caravan.Faction != null && !ReferenceEquals(settlement.faction, caravan.Faction)
                    ? settlement.faction.TradeAccessPriceGainWith(caravan.Faction)
                    : 0f;

            return SettlementTradeUtility.OpenSession(settlement, caravan, BestNegotiator(settlement), treatyGain);
        }

        /// <summary>
        /// The line for <paramref name="def"/> on <paramref name="session"/>, adding one when the caravan
        /// does not stock that good at all.
        ///
        /// <para/><see cref="TradeSession.SetupWith"/> seeds one <see cref="Tradeable"/> per good the
        /// <i>trader</i> offers, and its own doc says "callers add the player's own goods and the silver line
        /// before trading" — this is that documented step, reached when a settlement wants to sell something
        /// the caravan did not turn up carrying. The new line starts at the caravan's own count (zero, by
        /// definition of being here) and the settlement's real store count, exactly as
        /// <see cref="SettlementTradeUtility.OpenSession"/> seeds every other line.
        /// </summary>
        public static Tradeable LineFor(TradeSession session, Settlement settlement, TraderCaravan caravan, ThingDef def)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (caravan == null) throw new ArgumentNullException(nameof(caravan));
            if (def == null) throw new ArgumentNullException(nameof(def));

            List<Tradeable> lines = session.deal.tradeables;
            for (int i = 0; i < lines.Count; i++)
            {
                if (ReferenceEquals(lines[i].thingDef, def)) return lines[i];
            }

            var added = new Tradeable(def, caravan.StockOf(def), settlement.StoreCountOf(def));
            session.deal.AddTradeable(added);
            return added;
        }

        /// <summary>
        /// Writes a completed deal's trader side back onto <paramref name="caravan"/>: what it has left of
        /// every good, and what it has left of its silver.
        ///
        /// <para/><b>Why this is here at all.</b> <see cref="SettlementTradeUtility.TryExecute"/> writes the
        /// seller's side only when the seller is a real <see cref="Settlement"/> with a ledger to write into;
        /// its own doc says a visiting caravan carries none, and that "whatever it doesn't sell simply leaves
        /// with it". True across a whole visit — but <i>during</i> one, the caravan's stock has to come down
        /// as it sells, or a player could buy the same stack repeatedly and a caravan would be an infinite
        /// resource rather than an occasion. The deal already tracked it correctly in
        /// <see cref="Tradeable.countOffered"/>; this is the one line that was missing, putting that number
        /// back where the next session will read it.
        ///
        /// <para/>Call only after <see cref="SettlementTradeUtility.TryExecute"/> has returned true. On a
        /// refusal the deal moved nothing, and writing back would be writing back the same numbers — harmless
        /// but indistinguishable from a bug, so the rule is the simple one.
        /// </summary>
        public static void WriteStockBack(TraderCaravan caravan, TradeSession session)
        {
            if (caravan == null) throw new ArgumentNullException(nameof(caravan));
            if (session == null) throw new ArgumentNullException(nameof(session));

            List<Tradeable> lines = session.deal.tradeables;
            for (int i = 0; i < lines.Count; i++)
            {
                caravan.SetStock(lines[i].thingDef, lines[i].countOffered);
            }

            Tradeable? silver = session.deal.silverTradeable;
            if (silver != null) caravan.SetStock(EconomyThingDefOf.Silver, silver.countOffered);
        }
    }
}
