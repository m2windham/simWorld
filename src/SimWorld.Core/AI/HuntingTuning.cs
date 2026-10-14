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
        /// <see cref="Pawns.RaceProperties"/> does not carry; wildness is the nearest thing it does.
        /// <para/>
        /// <b>Left as it was, deliberately, and why.</b> RimWorld's hunt revenge is
        /// <c>race.manhunterOnDamageChance x LerpDoubleClamped(1, 30, 3, 1, distance) x (1 - HuntingStealth)</c>
        /// per damaging hit (<c>PawnUtility.GetManhunterOnDamageChance</c>); the species figures are 10% for a
        /// muffalo and 0 for a husky or a chicken (RimWorld wiki infobox, "Manhunter Chance"). The stealth term is
        /// a pawn stat built from Shooting and Animals skill that this port has no StatDef for, and without it
        /// the muffalo's rule runs at its worst case: ~20% per arrow at ordinary range, so nearly every muffalo
        /// hunt would end in a charge. Porting the rule without the term would be a new source of exactly the
        /// wounds this lane removes, so the wildness-scaled stand-in stays until a stealth stat exists. Measured
        /// over 60 days of seed 12345 it set no grudge at all: every one of 89 came from a taming. (The taming
        /// roll that this constant used to be explained against is gone: it is the race's own
        /// <c>manhunterOnTameFailChance</c> now, see <see cref="Pawns.TameUtility.TryTame"/>.)
        /// <para/>
        /// Why it is small: a hunt lands many wounds before anything dies (a bow puts five-plus arrows into a
        /// chicken and dozens into a muffalo), so a per-hit chance compounds into "practically every hunt is
        /// spoiled". The number that matters is the per-<i>hunt</i> one, and this is picked so that provoked
        /// prey is an occasional setback rather than the usual outcome — which is what it cost to find out:
        /// the first calibration here was an order of magnitude higher and a single arrow routinely ended
        /// hunting for the day.
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
