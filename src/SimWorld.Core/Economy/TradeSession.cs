using System;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Economy
{
    /// <summary>
    /// One active negotiation between the player and a trader (RimWorld: <c>RimWorld.TradeSession</c>/
    /// <c>Dialog_Trade</c>'s backing state). <see cref="SetupWith"/> seeds a <see cref="TradeDeal"/> with one
    /// <see cref="Tradeable"/> per good the trader offers and sets the negotiator's price gain from their
    /// Social skill; callers add the player's own goods and the silver line before trading.
    /// </summary>
    public class TradeSession
    {
        public ITrader? trader;

        public Pawn? playerNegotiator;

        public TradeDeal deal = new TradeDeal();

        public void SetupWith(ITrader trader, Pawn? negotiator)
        {
            this.trader = trader ?? throw new ArgumentNullException(nameof(trader));
            playerNegotiator = negotiator;

            int socialLevel = negotiator?.skills.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            deal = new TradeDeal
            {
                negotiatorGain = TradeUtility.NegotiatorPriceGainFromSocialSkill(socialLevel),
            };

            foreach ((SimWorld.Defs.ThingDef def, int count) in trader.Goods)
            {
                deal.AddTradeable(new Tradeable(def, count, 0));
            }
        }
    }
}
