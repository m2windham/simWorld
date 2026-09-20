using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// How long a visiting trade caravan stays before it moves on.
    ///
    /// <para/><b>Unsourced, and said so rather than dressed up.</b> RimWorld's own trade caravans stay for a
    /// duration set by the lord job that brought them (<c>LordJob_TradeWithColony</c>), and that number could
    /// not be verified against decompiled source from this environment. Per CLAUDE.md's rule for a number that
    /// could not be sourced, the literal below is an order-of-magnitude judgement and the behaviour is pinned
    /// by test as a band and an ordering — "a caravan is present for a while and then is not", "a longer stay
    /// departs later" — never as the literal itself.
    ///
    /// <para/>The judgement: a stay has to be long enough that a god looking at a civilization has a real
    /// chance to notice the caravan and decide, and short enough that not trading is a cost rather than a
    /// postponement. A day is the smallest unit a civilization-scale player thinks in; three is long enough
    /// that a missed one stings without being an emergency.
    /// </summary>
    public static class TraderArrivalTuning
    {
        /// <summary>One to three in-game days on the tile, rolled per arrival from the arrival's own
        /// <see cref="RandomStream"/> so two identically-seeded games get identically-timed caravans.</summary>
        public static readonly IntRange StayDurationTicks =
            new IntRange(GenDate.TicksPerDay, GenDate.TicksPerDay * 3);
    }
}
