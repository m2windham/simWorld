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
        /// How many days of food — for its people and for every head it keeps — a settlement wants in store
        /// before it takes on another animal: <see cref="Building.FarmingTuning.DaysOfFoodStored"/>, the year
        /// at which the fields already call the store full and lie fallow. Reused rather than restated.
        ///
        /// <para/><b>Why a year and not the four-day larder this used to alias.</b> A head of livestock is a
        /// mouth every day of the year, winter included, and it is fed out of the ledger
        /// (<c>Economy.SettlementStockInitiative.IssueFromStores</c> tops the map up whoever ate it). Measured
        /// against four days of the map alone, the gate read a harvest lying unhauled in the field as a
        /// surplus worth dozens of head, and the herd that bought ate all year. Ten-year arc, threats off:
        /// animals ate 671 → 1,254 → 2,006 nutrition a year at band 40 seed 201 and 1,415 → 3,422 → 4,392 at
        /// band 20 seed 99 — more than the people — while harvest and human eating stayed flat, so each
        /// winter drew the ledger lower. A year is the horizon the herd's appetite actually runs over, and
        /// the one point in the food economy where the settlement has food it has decided it does not need:
        /// livestock is what a full granary spills into.
        /// </summary>
        public const float DaysOfFeedWanted = Building.FarmingTuning.DaysOfFoodStored;
    }
}
