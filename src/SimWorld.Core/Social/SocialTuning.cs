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
        /// A social fight needs both a bad opinion of the other party and a bad mood — RimWorld's own
        /// combination for <c>InteractionWorker_Insult</c>'s fight roll (opinion very low *and* mood already
        /// poor), reusing <see cref="MindState.MentalStateDef"/> machinery rather than a parallel system.
        /// </summary>
        public const float SocialFightOpinionThreshold = -20f;

        public const float SocialFightMoodThreshold = 0.4f;

        /// <summary>
        /// Base chance a qualifying insult escalates into a mutual <c>SocialFighting</c> mental state, before
        /// each participant's <see cref="Pawns.TraitDegreeData.socialFightChanceFactor"/> multiplies it. Not
        /// sourced from RimWorld's real constant; tuned so it is rare turn-to-turn but not negligible over a
        /// long-running population — pinned by a band/frequency test, not the literal value.
        /// </summary>
        public const float BaseSocialFightChance = 0.15f;

        /// <summary>
        /// Power of the bare-knuckled <see cref="Combat.Tool"/> <see cref="MindState.MentalState_SocialFighting"/>
        /// builds for its swings — RimWorld's own natural "fists" tool exists on the Human race ThingDef, not
        /// coded in; this port has no natural-weapons content yet (Combat module scope), so the fight builds
        /// one directly instead. Not sourced from RimWorld's real fists power; picked below the weakest content
        /// weapon (<c>MeleeWeapon_Club</c>'s head, power 12) so a social fight reads as a scuffle rather than a
        /// weapon fight — pinned by a test on relative wound severity, not the literal number.
        /// </summary>
        public const float SocialFightFistPower = 6f;

        /// <summary>Seconds between swings once a social fight's fists verb is warmed up (melee's own
        /// zero-warmup default — see <see cref="Combat.Verb_MeleeAttack"/>). Not sourced; picked so a handful
        /// of exchanges land within the mental state's own 100-1200 tick duration without being so frequent
        /// the fight resolves in a single blow.</summary>
        public const float SocialFightSwingCooldownSeconds = 1f;
    }
}
