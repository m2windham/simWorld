namespace SimWorld.AI
{
    /// <summary>
    /// Tuning for the taming half of the <c>Handling</c> work type (system: Animals — <c>ai.animals</c>).
    /// Like <see cref="HuntingTuning"/>, and for the same reason, <b>every figure here is this port's own</b>:
    /// RimWorld's taming is driven by a player <c>Designation.Tame</c> painted on one animal at a time, so
    /// the number that stands in for the player's judgement — how much livestock a settlement wants at all —
    /// has no RimWorld counterpart to copy. See <see cref="TamingInitiative"/> for the translation this backs
    /// and <see cref="Pawns.AnimalTuning"/> for the rolls a taming attempt then makes.
    /// <para/>
    /// There is exactly one constant, and it is an alias rather than a new number. That is deliberate: the
    /// herd a settlement wants is derived from the food economy's own figures
    /// (<see cref="HuntingTuning.NutritionPerEaterPerDay"/>, <see cref="Pawns.Pawn.HungerRate"/>) rather than
    /// picked, so there is no literal here that could drift away from the one the hunting gate reads.
    /// </summary>
    public static class TamingTuning
    {
        /// <summary>
        /// How many days of an animal's feed a settlement wants covered before it takes that animal on.
        ///
        /// <para/><b>The same horizon the settlement holds for its own people</b>
        /// (<see cref="HuntingTuning.DaysOfFoodWanted"/>), reused rather than restated. A second number here
        /// would be a second answer to one question — "how far ahead does this settlement keep its larder?" —
        /// and the two would drift. It also makes <see cref="TamingInitiative.HerdWanted"/> and
        /// <see cref="HuntingInitiative.WantsMeat"/> two readings of a single ledger: below the larder the
        /// settlement wants, the animals on its land are meat; above it, each further larder's-worth of
        /// surplus is one more mouth it can afford to keep.
        /// </summary>
        public const float DaysOfFeedWanted = HuntingTuning.DaysOfFoodWanted;
    }
}
