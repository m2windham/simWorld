using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Marriage, birth and death-from-age tuning. Every value below is SimWorld's own choice, documented at
    /// its declaration — <c>docs/research/epoch-inspiration.md</c> §5 measured Epoch's own numbers but they are
    /// tuning for Epoch's compressed clock (adults at 2.5 years, dead by 14-22) and are deliberately not
    /// reused here: SimWorld pawns run RimWorld's real age/life-stage clock
    /// (<see cref="Pawn_AgeTracker"/>, <see cref="GenDate.TicksPerYear"/> = 3.6M ticks, life stages at
    /// 3/13/18 for humans, <c>Data/Core/Defs/ThingDefs_Races/Races_Humanlike.xml</c>). Only the *shape* of
    /// Epoch's birth formula (base × mood × food-security × doctrine) is carried over, per
    /// <c>docs/research/epoch-inspiration.md</c> §5 "What SimWorld should take".
    /// </summary>
    public static class DemographyTuning
    {
        /// <summary>
        /// Age at which a pawn can marry and enters the fertility window. Matches <c>HumanlikeAdult</c>'s
        /// <c>minAge</c> in <c>Races_Humanlike.xml</c> (RimWorld's own life-stage threshold) and real-world
        /// legal adulthood. Deliberately *not* <c>HumanlikeTeenager</c>'s age-13 threshold, even though that
        /// stage is flagged <c>reproductive="true"</c> in content (RimWorld's own biology model does treat
        /// teenagers as reproductive) — SimWorld chooses not to model teen marriage/parenthood.
        /// </summary>
        public const float MinMarriageAgeYears = 18f;

        /// <summary>
        /// Upper end of the fertility window, applied to both partners since SimWorld has no gendered
        /// fertility curve yet (RimWorld's own per-age pregnancy-chance curve is not ported). Approximates
        /// the real-world decline in fertility past the mid-40s — a simplification, not a sourced number.
        /// </summary>
        public const float MaxFertilityAgeYears = 45f;

        /// <summary>
        /// Spread of the hidden lifespan roll around a race's own <c>lifeExpectancy</c>: every pawn's death
        /// budget lands in [lifeExpectancy - spread, lifeExpectancy + spread] — for humans (lifeExpectancy 80),
        /// [65, 95]. ±15 years approximates the spread of a real actuarial survival curve without hard-coding
        /// Epoch's absolute DEATH_LO/DEATH_HI day thresholds, and scales automatically with any race's own
        /// <c>lifeExpectancy</c> stat instead of being human-specific.
        /// </summary>
        public const float LifespanSpreadYears = 15f;

        /// <summary>
        /// Minimum years between successive births from the same household. Approximates historical,
        /// pre-modern, breastfeeding-mediated human birth spacing (roughly two years) rather than Epoch's
        /// 330-compressed-day cooldown, which was tuned for a lifespan a fifth as long as ours.
        /// </summary>
        public const float MinBirthIntervalYears = 2f;

        /// <summary>
        /// Base chance an eligible couple conceives at each demography interval, before the mood and
        /// food-security multipliers below. Tuned (not sourced — nothing in RimWorld or Epoch gives a number
        /// that transfers) so a food-secure, contented couple averages a birth roughly every 3-4 years once
        /// the cooldown above allows it, in the middle of pre-modern historical birth-spacing norms.
        /// </summary>
        public const float BaseBirthChancePerInterval = 0.35f;

        /// <summary>
        /// Multiplier applied when the household is not food secure (<see cref="FoodSecurityThreshold"/>).
        /// Same order of magnitude as Epoch's own food-insecurity multiplier (0.4,
        /// <c>docs/research/epoch-inspiration.md</c> §5) but chosen independently: famine's depressive effect
        /// on historical fertility is well documented, and roughly halving-to-more-than-halving the chance
        /// reads as the right size of effect on our own clock.
        /// </summary>
        public const float FoodInsecureBirthFactor = 0.4f;

        /// <summary>
        /// Food need level (0-1) below which a parent counts as not food secure. SimWorld has no
        /// settlement-level food-store signal wired to Pawns yet, so this uses each parent's own
        /// <c>Need_Food</c> level — the weaker-fed partner decides, matching "a household is only as
        /// food-secure as its hungriest member".
        /// </summary>
        public const float FoodSecurityThreshold = 0.5f;

        /// <summary>
        /// How often <see cref="FamilyManager.DemographyTick"/> sweeps the population for marriage, birth and
        /// death-from-age. Once per in-game year (RimWorld's own 60-day year, <see cref="GenDate.TicksPerYear"/>)
        /// keeps this a rare-tick concern, nowhere near per-tick-per-pawn work, while staying frequent enough
        /// that an 80-year lifespan gets dozens of checks.
        /// </summary>
        public const int DemographyIntervalTicks = GenDate.TicksPerYear;

        /// <summary>Cooldown in ticks, derived from <see cref="MinBirthIntervalYears"/>.</summary>
        public const int MinBirthIntervalTicks = (int)(MinBirthIntervalYears * GenDate.TicksPerYear);
    }
}
