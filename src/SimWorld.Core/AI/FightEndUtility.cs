using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// When an attack job should stop because the fight it belonged to is over — as opposed to won, which
    /// <see cref="Toils_Combat.AttackTarget"/> already knows how to notice.
    ///
    /// <para/><b>The rule is the finder's own, asked again.</b> <see cref="AttackTargetFinder"/> will only
    /// ever offer a target that <see cref="AttackTargetsUtility.HostileTo"/> its searcher, so every
    /// <c>AttackMelee</c> and <c>AttackStatic</c> job starts out against an enemy. Nothing asked again
    /// afterwards: the shared toil runs until its target is down, dead or gone. But hostility here is not
    /// only a matter of factions at war. An animal's grudge lapses after a day
    /// (<see cref="HuntUtility.IsAngry"/>), a manhunt has a deadline, and a wild animal can be tamed in the
    /// middle of the fight it started — and in each case both sides kept swinging at somebody the finder
    /// would no longer have picked, until one of them could not get up.
    ///
    /// <para/>RimWorld does not need the question in this form because its fight jobs are short and
    /// re-issued: <c>JobGiver_AIFightEnemy</c> hands out attack jobs with an expiry and
    /// <c>checkOverrideOnExpire</c>, so the job giver's own hostility test is re-run every few seconds, and
    /// a manhunter that recovers has its jobs stopped by the mental state ending. This port's attack jobs
    /// never expire, so the test is made here instead. (Recalled, not read from a decompile in this sandbox;
    /// pinned by <c>AnimalGrudgeTests</c>.)
    ///
    /// <para/>A player's own order is left alone: <see cref="Job.playerForced"/> means somebody chose this
    /// target on purpose, and a bad decision is allowed to be bad (<c>docs/design/player-first.md</c>).
    /// </summary>
    public static class FightEndUtility
    {
        /// <summary>True when <paramref name="job"/>'s target is a pawn that <paramref name="attacker"/> is
        /// no longer at odds with.</summary>
        public static bool FightIsOver(Pawn attacker, Job job)
        {
            if (attacker == null || job == null || job.playerForced) return false;
            if (!(job.GetTarget(TargetIndex.A).Thing is Pawn target)) return false;
            return !AttackTargetsUtility.HostileTo(attacker, target);
        }
    }
}
