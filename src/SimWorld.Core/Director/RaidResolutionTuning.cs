namespace SimWorld.Director
{
    /// <summary>
    /// Tuning for resolving a raid on a settlement nobody is watching
    /// (<see cref="SettlementRaidResolver"/>, tracker item <c>director.raids</c>).
    ///
    /// <para/><b>None of this is sourced from RimWorld, and it could not be.</b> RimWorld has exactly one
    /// colony and it is always the one on screen, so it has no abstract-resolution path to port: every raid
    /// there is fought cell by cell. These are SimWorld's own numbers for the case RimWorld does not have, and
    /// they are documented at each declaration and pinned by <c>UnwatchedRaidTests</c> as a band, an ordering
    /// or a trend — never as a bare literal a test asserts back at itself.
    ///
    /// <para/><b>The unit is <c>PawnKindDef.combatPower</c>, deliberately.</b> A raid is already *bought* in
    /// that currency (<see cref="IncidentParms.points"/> spent through <c>PawnGroupMakerUtility</c>), so
    /// pricing the defence in the same unit means the two sides of the comparison are commensurable by
    /// construction rather than by a conversion constant nobody can justify.
    /// </summary>
    public static class RaidResolutionTuning
    {
        /// <summary>
        /// Combat power credited to a live defender whose <c>Pawn</c> carries no <c>PawnKindDef</c> — a pawn
        /// built straight from a <c>ThingDef</c> (which tests do, and <c>Pawn</c>'s own two-argument
        /// constructor allows). Sits at the value shipped content gives <c>Villager</c>, which is what an
        /// ordinary settled citizen is; it is a stand-in for a missing def, not a second source of truth for
        /// what a villager is worth, so content moving is not a reason to move this.
        /// </summary>
        public const float DefaultDefenderCombatPower = 40f;

        /// <summary>
        /// How much each level of a defender's best combat skill (Shooting or Melee) moves their combat power,
        /// measured from <c>DefaultCombatSkills</c>'s own untrained baseline of level 4 — so a defender at the
        /// baseline is worth exactly their kind's <c>combatPower</c> and nothing else has to be restated. At
        /// the ends of the skill ladder this spans roughly 0.8x to 1.8x: a real difference, never the whole
        /// difference, because a settlement's defence is mostly how many people it can field.
        /// </summary>
        public const float CombatPowerPerSkillLevel = 0.05f;

        /// <summary>The skill level <see cref="CombatPowerPerSkillLevel"/> measures from —
        /// <c>Combat.DefaultCombatSkills</c>'s untrained-colonist baseline, restated here as a named constant
        /// rather than as a bare 4 in the arithmetic.</summary>
        public const int UntrainedCombatSkillLevel = 4;

        /// <summary>
        /// The share of a settlement's bare <c>Settlement.StatisticalPopulation</c> that actually turns out to
        /// fight. A settlement of forty thousand fields a militia, not forty thousand soldiers, and this is
        /// the constant that says so — without it a large civilization would be arithmetically unraidable.
        /// Deliberately on the order of a few percent: the Statistical tier is the slice of the population
        /// nobody is individually modelling, which is to say the farmers and the children, not the garrison.
        /// </summary>
        public const float StatisticalMusterFraction = 0.05f;

        /// <summary>
        /// Combat power of one mustered head from the Statistical cohort. There is no <c>Pawn</c> per
        /// Statistical citizen to read a kind, a health fraction or a skill from — by that tier's own design
        /// (spec §11.3) — so the cohort contributes one stated value per head, the same
        /// "fold the cohort in with a stated value rather than enumerating it" idiom
        /// <c>God.GodRollup.AccumulateStatisticalCohort</c> already uses for the means. Set well below
        /// <see cref="DefaultDefenderCombatPower"/>: a militia head is an armed farmer, not a villager who has
        /// been individually worth modelling.
        /// </summary>
        public const float StatisticalDefenderCombatPower = 8f;

        /// <summary>
        /// Bodies one point of prevailing combat power accounts for. This is the only constant here that
        /// converts between the two things being counted — strength and people — so it is the one that decides
        /// whether an abstract raid reads as a skirmish or a massacre. At this value a 400-point raid (roughly
        /// eight raiders against shipped content) can account for about eight deaths at the absolute outside,
        /// before either side's loss fraction scales it down: a raid can wipe out a hamlet and can never
        /// depopulate a city.
        /// </summary>
        public const float DeathsPerCombatPower = 0.02f;

        /// <summary>Share of the enemy's killing potential the side that <i>won</i> the engagement still
        /// suffers. Never zero: winning a fight is not the same as taking no losses.</summary>
        public static readonly FloatRange WinnerLossFraction = new FloatRange(0.02f, 0.10f);

        /// <summary>Share of the enemy's killing potential the side that <i>lost</i> the engagement suffers —
        /// several times <see cref="WinnerLossFraction"/>, which is what makes the outcome of the roll worth
        /// caring about rather than a cosmetic label on the same casualties.</summary>
        public static readonly FloatRange LoserLossFraction = new FloatRange(0.15f, 0.40f);

        /// <summary>Share of each of a settlement's stores carried off by a raid that was not repelled. A
        /// range rather than a constant so two identical raids on two identical towns do not produce the
        /// identical ledger.</summary>
        public static readonly FloatRange LootFraction = new FloatRange(0.10f, 0.35f);
    }
}
