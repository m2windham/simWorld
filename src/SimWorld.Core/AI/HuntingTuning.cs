using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Tuning for the <c>Hunt</c> work type (system 9: AI — hunting). Every constant here is <b>this port's
    /// own</b>: RimWorld's hunting is driven by a player <c>Designation.Hunt</c>, and the numbers that would
    /// otherwise stand in for the player's judgement (when to hunt at all, what is too big to take on, how
    /// readily wounded prey turns on the hunter) have no RimWorld counterpart to copy — RimWorld simply asks
    /// the player. Each one is pinned by a trend test in <c>HuntingAITests</c> — a band, a direction or an
    /// ordering — never by the literal, exactly as CLAUDE.md requires for an unsourced number. See
    /// <see cref="HuntingInitiative"/> for the translation these back, and <see cref="Pawns.AnimalTuning"/>
    /// for the taming/training side of the same animals module.
    /// </summary>
    public static class HuntingTuning
    {
        /// <summary>
        /// How many days of food a settlement wants banked before it stops sending hunters out. Below this it
        /// hunts; at or above it, wild animals are left alone. Deliberately a few days rather than a season:
        /// the meat a kill yields lands on the map as an ordinary item stack, so the target has to be small
        /// enough that a handful of kills actually reaches it — otherwise the gate would never close and
        /// "hunt when hungry" would collapse into "hunt everything, always", which is the very behaviour
        /// removing the player's designation could so easily produce.
        /// </summary>
        public const float DaysOfFoodWanted = 4f;

        /// <summary>
        /// Nutrition one pawn burns per day at hunger rate 1, read straight off <see cref="Needs.Need_Food"/>'s
        /// own RimWorld-sourced per-tick rate rather than restated as a second literal here — so the two can
        /// never drift apart. (RimWorld: 1.6 nutrition/day.)
        /// </summary>
        public const float NutritionPerEaterPerDay = Needs.Need_Food.BaseFoodFallPerTick * GenDate.TicksPerDay;

        /// <summary>
        /// How much bigger than the hunter (by <see cref="Pawns.Pawn.BodySize"/>) a live animal may be and
        /// still be considered worth taking on. RimWorld has no such rule — the player decides what to
        /// designate, and picks the fight or doesn't. With no player to ask, this stands in for "a lone
        /// hunter does not start a fight it cannot win": a human (body size 1) will hunt a muffalo (1.5) but
        /// not something that outweighs it more than twofold. A <b>ratio</b>, not an absolute size, so the
        /// rule keeps meaning the same thing if a larger humanlike race is ever added.
        /// </summary>
        public const float MaxPreyBodySizeRatio = 2f;

        // The revenge chance per wounding hit used to live here as RevengeChancePerWildnessOnWound, 0.02 x the
        // race's wildness, standing in for RimWorld's RaceProperties.manhunterOnDamageChance because this port
        // did not carry that field. It does now (RaceProperties.manhunterOnDamageChance, scaled by the hunter's
        // HuntingStealth in HuntUtility.TryProvokeRevenge), so the proxy is gone. The proxy's own doc warned
        // that a per-hit chance compounds over a hunt; RimWorld's answer to that is the stealth stat, not a
        // smaller number.

        // How long a hunt-provoked animal stays angry used to be RevengeAngerDurationTicks, the failed-tame
        // day. Both grudges are RimWorld's manhunter state and now share its recovery: Pawns.ManhunterRecovery.

        // A hunt's time limit used to live here as HuntJobExpiryTicks, an invented half day set as the job's
        // expiryInterval. RimWorld's own limit is JobDriver_Hunt's 5000 ticks from the job's start, failing
        // the approach and shoot toils only; it is ported there as JobDriver_Hunt.MaxHuntTicks.

        /// <summary>
        /// How close a melee hunter must be to swing, in cells. A melee <see cref="Combat.VerbProperties"/>
        /// built by <see cref="Combat.MeleeVerbUtility"/> leaves <see cref="Combat.VerbProperties.range"/> at
        /// its 90-cell default (it is a ranged field that means nothing for a swing), so a melee hunt must not
        /// read range off the verb. √2 is one cell including diagonals — RimWorld's own melee reach.
        /// </summary>
        public const float MeleeReachCells = 1.4143f;
    }
}
