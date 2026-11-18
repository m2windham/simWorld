using System;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// How long an animal's grudge (<see cref="MindState.Pawn_MindState.angryAt"/>) lasts, and what ends it
    /// early. Both things that set a grudge — a failed tame and wounded prey — are RimWorld's
    /// <c>MentalStateDefOf.Manhunter</c>, so the grudge recovers the way that mental state does.
    /// <para/>
    /// <b>Sourcing.</b> The rimworldwiki.com Manhunter page: "The rampage ends after a set time, when the
    /// animal sleeps, or when it is downed", with recovery times of "a mean of 18,000 ticks (7.2 in-game
    /// hours) and a minimum of 10,000 ticks (4 in-game hours)". That is a secondary source. It is read here as
    /// the def's own two fields, <c>minTicksBeforeRecovery</c> 10,000 and a recovery MTB of 18,000 ticks
    /// after it; if the wiki's "mean" is meant to include the minimum, a grudge here runs about 10,000 ticks
    /// longer than RimWorld's on average.
    /// <para/>
    /// <b>What it replaced.</b> A flat day (60,000 ticks) that nothing ended early. A muffalo knocked down by
    /// the hunter it charged stayed "angry" on the ground, so the hunt givers refused to finish it
    /// (<see cref="AI.HuntUtility.IsSafeToHunt"/>), and it got up hours later still carrying the grudge.
    /// </summary>
    public static class ManhunterRecovery
    {
        /// <summary>RimWorld's manhunter minimum before recovery can happen, per the wiki.</summary>
        public const int MinTicksBeforeRecovery = 10000;

        /// <summary>Mean time to recover once the minimum has passed, per the wiki.</summary>
        public const float RecoveryMtbTicks = 18000f;

        /// <summary>
        /// The grudge's whole length, rolled once when it starts. RimWorld instead rolls
        /// <c>Rand.MTBEventOccurs</c> each tick after the minimum. The waiting time that produces is
        /// exponential, so drawing it once from the same distribution gives the same durations and takes one
        /// random number rather than one a tick.
        /// </summary>
        public static int RollDurationTicks()
        {
            double u = Rand.Value;
            return MinTicksBeforeRecovery + (int)Math.Ceiling(-Math.Log(1.0 - u) * RecoveryMtbTicks);
        }

        /// <summary>
        /// Ends a running grudge on the two conditions RimWorld's manhunter state recovers on: downed or
        /// asleep (the port's <see cref="MindState.MentalStateDef.recoverFromDowned"/> and
        /// <see cref="MindState.MentalStateDef.recoverFromSleep"/>, which a grudge does not go through).
        /// Called every tick from <see cref="MindState.Pawn_MindState.MindStateTick"/>.
        /// </summary>
        public static void CheckRecover(Pawn pawn)
        {
            if (pawn.mindState.angryAt == null) return;
            if (!pawn.Downed && !pawn.Asleep) return;
            pawn.mindState.angryAt = null;
            pawn.mindState.angryUntilTick = -1;
        }
    }
}
