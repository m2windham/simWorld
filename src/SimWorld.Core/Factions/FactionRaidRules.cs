using System;

using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// The three <see cref="FactionDef"/> flags that decide <b>when</b> a civilization may raid and <b>how far
    /// it will go</b> once it has — <see cref="FactionDef.earliestRaidDays"/>, <see cref="FactionDef.autoFlee"/>
    /// and <see cref="FactionDef.hostileToFactionlessHumanlikes"/> — gathered into one place because they were
    /// all three dormant for the same reason: each was set by shipped content and read by nothing, and each
    /// one's reader would otherwise have been a lone clause buried in a different system.
    ///
    /// <para/>Only the first two are answered here. The third is a question about a <i>pawn</i> rather than
    /// about a raid, so its reader is <c>AI.AttackTargetsUtility.HostileTo</c>, where every other hostility
    /// rule already lives — see that method for what was wrong and the measurement that showed it.
    ///
    /// <para/><b>On sourcing.</b> RimWorld's own source could not be read from this sandbox. Where a number
    /// below is RimWorld's, the comment says it is recalled rather than sourced and the test pins an ordering
    /// or a band instead of the literal, per CLAUDE.md.
    /// </summary>
    public static class FactionRaidRules
    {
        /// <summary>
        /// Whether enough days have passed for <paramref name="faction"/> to raid at all
        /// (<see cref="FactionDef.earliestRaidDays"/>). RimWorld asks this the same way and in the same place:
        /// its <c>PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroup</c> filters candidate raiders on
        /// <c>GenDate.DaysPassed >= f.def.earliestRaidDays</c> before weighting them by
        /// <see cref="FactionDef.raidCommonality"/>, which is exactly what
        /// <see cref="FactionManager.RandomEnemyFaction"/> does here once handed this as its validator.
        ///
        /// <para/><b>Why this is not redundant with the storyteller's own early-game pacing.</b> Two of the
        /// three shipped storytellers do hold the first raid back on their own — Cassandra's <c>ThreatBig</c>
        /// cycle declares <c>minDaysPassed 6</c> and Phoebe's declares <c>10</c>, both later than any shipped
        /// <see cref="FactionDef.earliestRaidDays"/>, so under those two this gate never fires. Randy Random
        /// declares no day floor anywhere: a single <c>StorytellerComp_RandomMain</c> with
        /// <c>mtbDays 1.35</c> and <c>ThreatBig</c> weighted 16, which can and does pick <c>RaidEnemy</c> on
        /// day zero. So the protection the field describes existed for two storytellers out of three and the
        /// field itself was the only thing that would have provided it for the third.
        ///
        /// <para/>A faction that names day 0 (the shipped <c>RoughOutlanders</c>, who are
        /// <see cref="FactionDef.permanentEnemy"/>) is never held back by this at all, which is the content
        /// saying "these ones do not wait".
        /// </summary>
        public static bool CanRaidYet(Faction faction)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            if (faction.def.earliestRaidDays <= 0) return true;
            return GenDate.DaysPassedAt(Find.TickManager.TicksGame) >= faction.def.earliestRaidDays;
        }

        /// <summary>
        /// Share of a war band that can be killed before the rest of it breaks off, for a faction whose def
        /// sets <see cref="FactionDef.autoFlee"/>.
        ///
        /// <para/><b>RimWorld's number, recalled and not sourced</b> (no RimWorld source in this sandbox): its
        /// <c>LordJob_AssaultColony</c> builds an assault graph with a transition out to <c>ExitMap</c> fired
        /// by <c>Trigger_FractionPawnsLost(0.5f)</c>, and that transition is added <i>only</i> when the
        /// assaulting faction's def sets <c>autoFlee</c>. Half is therefore what this stands on, and
        /// <c>FactionRaidRulesTests</c> pins the <i>ordering</i> it produces (a band that flees always loses
        /// strictly fewer than one that does not, and never all of itself) rather than the literal.
        /// </summary>
        public const float WithdrawAfterLosingFraction = 0.5f;

        /// <summary>
        /// How many of a war band of <paramref name="bandSize"/> can die before the raid is over, for
        /// <paramref name="attacker"/>.
        ///
        /// <para/><b>Why this is where <see cref="FactionDef.autoFlee"/> lands, and what it deliberately is
        /// not.</b> RimWorld expresses the flag as a transition in a <c>Lord</c> graph: an assault squad that
        /// has lost half its members walks off the map. This port has no <c>Lord</c>, no squad-level AI and —
        /// as <c>AI.CombatPostureUtility</c> records in as many words — no flight behaviour for anyone, so
        /// there is nothing on the *map* path for the flag to switch. There is on the other path:
        /// <c>Director.SettlementRaidResolver</c> resolves a raid on an unwatched settlement as one
        /// engagement, and the only thing it needs to know about a war band beyond its strength is how much
        /// of itself it will spend. A band that withdraws at half losses cannot lose more than half; a band
        /// that does not withdraw can lose all of it. That is the same statement the Lord graph makes,
        /// costed once instead of tick by tick.
        ///
        /// <para/>This is SimWorld's own translation of a RimWorld mechanism into the abstract-resolution path
        /// RimWorld does not have — the same relationship <c>RaidResolutionTuning</c>'s whole doc describes —
        /// and it is recorded as such. The map path still has no flight: a raid the god is watching fights to
        /// the last pawn whatever its def says, and that gap is <c>CombatPostureUtility</c>'s to close.
        /// </summary>
        /// <param name="attacker">The raiding faction; null (a squad posed by hand) reads as "does not flee".</param>
        /// <param name="bandSize">How many raiders are still alive to be killed.</param>
        public static int MaxRaidersLost(Faction? attacker, int bandSize)
        {
            if (bandSize <= 0) return 0;
            if (attacker == null || !attacker.def.autoFlee) return bandSize;

            // Floored at one body, for the same reason SettlementRaidResolver floors the loser's casualties
            // there: a two-raider band that runs away having lost nobody is a raid that cost its side
            // nothing, which is the shape of the bug that module exists to stop.
            int cap = (int)(bandSize * WithdrawAfterLosingFraction);
            return Math.Max(1, Math.Min(cap, bandSize));
        }
    }
}
