namespace SimWorld.Social
{
    /// <summary>
    /// Social layer tuning. As with <see cref="Pawns.DemographyTuning"/>, every value here is documented at
    /// its declaration: some are RimWorld's own shape (the interaction-selection-by-weight mechanic, the
    /// compatibility-factor mechanic) with SimWorld's own magnitudes, since this pass could not check numbers
    /// against RimWorld's decompiled source from this environment. Where that is true it says so; assertions
    /// in <c>tests/SimWorld.Core.Tests/Social</c> pin the resulting *behaviour* (an ordering, a band, a sign)
    /// rather than the literal constant, per the repository's own rule for untraceable numbers.
    /// </summary>
    public static class SocialTuning
    {
        /// <summary>Opinion is clamped to this band either side of zero, matching RimWorld's own [-100, 100].</summary>
        public const float MinOpinion = -100f;
        public const float MaxOpinion = 100f;

        /// <summary>
        /// Half-width of the compatibility factor's range (see
        /// <see cref="SocialUtility.CompatibilityFactor"/>): every ordered pair of pawns lands somewhere in
        /// [-CompatibilityRange, +CompatibilityRange], stable for that pair forever. Not RimWorld's real
        /// magnitude (not sourced) — sized so it can tip a near-neutral relationship without swamping a
        /// family bond or a strong memory stack.
        /// </summary>
        public const float CompatibilityRange = 15f;

        /// <summary>
        /// How often <see cref="SocialInteractionManager.SocialInteractionTick"/> sweeps the population for
        /// interactions — a rare-tick manager sweep in the same shape as
        /// <see cref="Pawns.FamilyManager.DemographyTick"/> and <c>Storyteller.StorytellerTick</c>, gated once
        /// per interval rather than run per-pawn-per-tick (see <c>docs/perf/baseline.md</c> §2: health alone is
        /// 75% of attributed per-tick cost, and every other tracker already gates behind a hash interval).
        /// 2,500 ticks is RimWorld's own social-interaction check cadence (<c>Pawn_InteractionsTracker</c>'s
        /// <c>InteractionIntervalTicks</c>).
        /// </summary>
        public const int InteractionIntervalTicks = 2500;

        /// <summary>
        /// Chance an eligible pawn attempts an interaction on any given interval it is checked. Not sourced —
        /// tuned so a small population (a handful of pawns) sees interactions every few intervals rather than
        /// every single one, without making the mechanic rare enough to never show up in a short test run.
        /// </summary>
        public const float InteractionChancePerPawnPerInterval = 0.6f;

        /// <summary>Base selection weight for chitchat: always available, the mundane default (RimWorld: the same role its own Chitchat interaction plays — highest commonality, no gate).</summary>
        public const float ChitchatBaseWeight = 10f;

        /// <summary>Opinion at/above which a deep talk becomes selectable at all — a stranger or a disliked pawn does not get one.</summary>
        public const float DeepTalkMinOpinion = 10f;

        public const float DeepTalkBaseWeight = 4f;

        public const float InsultBaseWeight = 1f;

        /// <summary>Multiplies insult's weight when the initiator already dislikes the recipient (RimWorld: insult is far likelier from someone who already has poor opinion of the target).</summary>
        public const float InsultLowOpinionWeightFactor = 6f;

        /// <summary>Mood (0-1) below which a pawn is frustrated enough to raise insult's weight further.</summary>
        public const float InsultLowMoodThreshold = 0.4f;

        public const float InsultLowMoodWeightFactor = 3f;

        public const float SlightBaseWeight = 2f;

        public const float SlightLowOpinionWeightFactor = 2.5f;

        /// <summary>
        /// Chance an insult escalates into a mutual <c>SocialFighting</c> mental state <i>before</i>
        /// <see cref="SocialFightUtility.SocialFightChance"/>'s chain of factors — capacity, opinion, traits,
        /// age gap — multiplies it (RimWorld: <c>InteractionDef.socialFightBaseChance</c>, a per-interaction
        /// content field read by <c>Pawn_InteractionsTracker.SocialFightChance</c>).
        ///
        /// <para/><b>4%, and what that replaced.</b> This was 0.15 behind two hard gates that RimWorld does
        /// not have (see <see cref="SocialFightUtility"/>). The RimWorld value for the Insult interaction
        /// could not be read out of its content from this environment; 0.04 is the figure the RimWorld wiki's
        /// own Social page states for an insult ("Every insult has a 4% chance of starting a social fight,
        /// while slights have a 0.5% chance"), which is a secondary source rather than the def, so per
        /// CLAUDE.md the resulting <i>behaviour</i> is what the tests pin — a settlement that is otherwise
        /// healthy does not lose a third of its people to its own citizens in a week — and not this literal.
        ///
        /// <para/><b>Where it will eventually live.</b> On <see cref="InteractionDef"/>, as RimWorld has it,
        /// so that Slight can carry its own 0.5% and content can add a fight-capable interaction without
        /// touching code. It is here for now because moving it means editing
        /// <c>Data/Core/Defs/InteractionDefs/Interactions.xml</c>, a shared content file, and this lane has
        /// no need of a second escalating interaction — insult is still the only one that rolls.
        /// </summary>
        public const float InsultSocialFightBaseChance = 0.04f;

        /// <summary>
        /// Power of a bare fist — RimWorld's own natural "fists" tool exists on the Human race ThingDef, not
        /// coded in; this port has no natural-weapons content yet (Combat module scope), so
        /// <see cref="AI.AttackVerbUtility.NaturalWeaponFor"/> builds one directly instead and reads this.
        /// Not sourced from RimWorld's real fists power; picked below the weakest content weapon
        /// (<c>MeleeWeapon_Club</c>'s head, power 12) so a social fight reads as a scuffle rather than a
        /// weapon fight — pinned by a test on relative wound severity, not the literal number.
        ///
        /// <para/><b>There used to be a second, faster fist.</b>
        /// <see cref="MindState.MentalState_SocialFighting"/> built its own <see cref="Combat.Tool"/> with a
        /// one-second cooldown, half of <see cref="Combat.Tool"/>'s own two-second default and so half of the
        /// cooldown every other unarmed pawn in the game swung at — the same punch at two different speeds
        /// depending on who threw it. The fight now goes through
        /// <see cref="AI.AttackVerbUtility.NaturalWeaponFor"/> like everything else, so there is one fist and
        /// the second constant is gone.
        /// </summary>
        public const float SocialFightFistPower = 6f;

        // ---- Romance (social.romance) ----
        // None of these are RimWorld's: RimWorld's romance model reads an orientation and a cheating risk
        // this port has no data for, so every number below is this port's own and is pinned by a trend test
        // (a better-liked suitor is likelier to be accepted, a marriage is harder to leave than an affair)
        // rather than by the literal.

        /// <summary>Youngest a citizen can court or be courted. Adulthood as this port already models it —
        /// the same line demography draws for marriage.</summary>
        public const float RomanceMinAgeYears = 16f;

        /// <summary>
        /// Opinion below which a romance attempt is never made and never accepted. Set against what this
        /// port's opinion system actually produces rather than picked in the abstract: a Friend relation is
        /// +12 and a deep talk +6, so the bar sits just above "we are friends" and below "we are friends who
        /// have talked properly" — which is where courtship should start.
        /// </summary>
        public const float RomanceMinOpinion = 15f;

        /// <summary>Selection weight of a romance attempt for a pair who clear every gate. Small: the sweep
        /// runs over the whole population and courtship should be rare per pair per interval.</summary>
        public const float RomanceBaseWeight = 0.4f;

        /// <summary>Floor of the acceptance chance for a pair who just clear the opinion bar.</summary>
        public const float RomanceBaseAcceptChance = 0.15f;

        /// <summary>How much of the acceptance chance opinion accounts for.</summary>
        public const float RomanceOpinionAcceptWeight = 0.5f;

        /// <summary>How much of it the pair's stable compatibility factor accounts for.</summary>
        public const float RomanceCompatibilityAcceptWeight = 0.25f;

        /// <summary>
        /// Memory balance above which a couple never breaks up. Measured on their social memories alone —
        /// what they have actually done to each other — rather than on total opinion, which also carries the
        /// <c>Lover</c> relation's own +20 and the pair's stable compatibility factor. Both of those would
        /// make the gate say something else: the first that a couple is happy because they are a couple, the
        /// second that two people who happen to click can never fall out. Zero, so a couple parts once the
        /// ledger between them has gone negative.
        /// </summary>
        public const float BreakupMaxMemoryOpinion = 0f;

        /// <summary>Selection weight of a breakup for an unhappy couple.</summary>
        public const float BreakupBaseWeight = 0.6f;

        /// <summary>A marriage is harder to leave than an affair: the same unhappiness ends one sooner.</summary>
        public const float DivorceWeightFactor = 0.35f;
    }
}
