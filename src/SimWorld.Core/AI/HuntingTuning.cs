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

        /// <summary>
        /// Per wounding hit, the chance a surviving animal turns on its hunter, scaled by the race's
        /// <see cref="Pawns.RaceProperties.wildness"/>. RimWorld's own trigger is
        /// <c>RaceProperties.manhunterOnDamageChance</c>, a per-species field this port's
        /// <see cref="Pawns.RaceProperties"/> does not carry; wildness ("how readily an untamed individual
        /// ... turns on a handler", that field's own words) is the nearest thing it does, and
        /// <see cref="Pawns.TameUtility.TryTame"/> already reaches for it exactly this way for the failed-tame
        /// anger roll. Same shape, own constant.
        /// <para/>
        /// <b>Why it is so much smaller than <see cref="Pawns.AnimalTuning.TameFailAngerChance"/> (0.5), which
        /// it otherwise mirrors.</b> A taming attempt rolls once per attempt; a hunt lands many wounds before
        /// anything dies (a bow puts five-plus arrows into a chicken and dozens into a muffalo), so the same
        /// per-event chance compounds into "practically every hunt is spoiled". The number that matters is the
        /// per-<i>hunt</i> one, and this is picked so that provoked prey is an occasional setback rather than
        /// the usual outcome — which is what it cost to find out: the first calibration here was an order of
        /// magnitude higher and a single arrow routinely ended hunting for the day.
        /// </summary>
        public const float RevengeChancePerWildnessOnWound = 0.02f;

        /// <summary>How long a hunt-provoked animal stays angry. Matched to the failed-tame duration
        /// deliberately: both set the same <see cref="MindState.Pawn_MindState.angryAt"/> field and are read
        /// by the same <see cref="ThinkNode_ConditionalAngryAtHandler"/> tier, so two different durations
        /// would be two different meanings for one piece of state.</summary>
        public const int RevengeAngerDurationTicks = Pawns.AnimalTuning.TameFailAngerDurationTicks;

        /// <summary>
        /// How long a single hunt may run before the job gives up, enforced through <see cref="Job.expiryInterval"/>
        /// (already checked every tick by <see cref="Pawn_JobTracker.JobTrackerTick"/> — nothing new needed).
        /// Prey flees (<see cref="JobGiver_AnimalFlee"/>) and a hunter re-paths after it, so without a
        /// ceiling a hunt across open ground can run indefinitely. Half an in-game day.
        /// </summary>
        public const int HuntJobExpiryTicks = GenDate.TicksPerDay / 2;

        /// <summary>
        /// How close a melee hunter must be to swing, in cells. A melee <see cref="Combat.VerbProperties"/>
        /// built by <see cref="Combat.MeleeVerbUtility"/> leaves <see cref="Combat.VerbProperties.range"/> at
        /// its 90-cell default (it is a ranged field that means nothing for a swing), so a melee hunt must not
        /// read range off the verb. √2 is one cell including diagonals — RimWorld's own melee reach.
        /// </summary>
        public const float MeleeReachCells = 1.4143f;
    }
}
