namespace SimWorld.Pawns
{
    /// <summary>
    /// How much of a citizen's record is computed per tick (<c>docs/spec/simworld-spec.md</c> §11.3 — SimWorld's
    /// own; RimWorld has no named equivalent, only the unlabeled map/world-pawn split this generalises). Every
    /// tier is a real agent record with a real identity; what varies is how often it is touched.
    /// </summary>
    public enum PawnTier
    {
        /// <summary>Ticked exactly as ported: needs, mood, health, skills, jobs, every tick. The settlement
        /// under the player's attention, plus anyone promoted into it.</summary>
        Full,

        /// <summary>The full state exists and is real, but advances only on the rare/long tick buckets, never
        /// per tick. No jobs.</summary>
        Interval,

        /// <summary>A member of a cohort: identity, family, age and demography participation are real; needs
        /// and health are sampled from the cohort's distribution rather than tracked individually.</summary>
        Statistical,
    }
}
