using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Tuning for the Statistical tier's cohort sampling (<c>docs/spec/simworld-spec.md</c> §11.3/§11.4). Every
    /// value here is SimWorld's own choice, not sourced from RimWorld (which has no cohort-sampling concept —
    /// its world pawns simply freeze) — documented at the declaration per <c>CLAUDE.md</c>'s rule for numbers
    /// that could not be sourced.
    /// </summary>
    public static class TieringTuning
    {
        /// <summary>
        /// Band a Statistical citizen's needs (mood, food, rest, joy) are sampled within, as a fraction of each
        /// need's own max level. Deliberately narrower and higher than the full [0, 1] range: a member of the
        /// deep population who is still alive and un-tracked is assumed to be "getting by" rather than at a
        /// crisis extreme — crisis extremes are what Full-tier simulation exists to make individually real, not
        /// what an untracked cohort member is presumed to be living through. Not a sourced distribution, only a
        /// plausible one; §11.5 leaves the actual shape of cohort sampling an open question.
        /// </summary>
        public const float StatisticalNeedSampleMin = 0.4f;

        public const float StatisticalNeedSampleMax = 0.85f;

        /// <summary>Band a Statistical citizen's overall health fraction (<see cref="Pawn_TierTracker.SampledHealthFraction"/>)
        /// is sampled within. Deliberately never wired into the real hediff/capacity system (see the tracker's
        /// own doc): it is a coarse readout for anything that wants a "how hale is this citizen" number without
        /// the sim inventing specific injuries nobody ever gave them — §11.4's fidelity rule applied to the
        /// engine's own state, not only to the chronicle.</summary>
        public const float StatisticalHealthFractionMin = 0.55f;

        public const float StatisticalHealthFractionMax = 1f;

        /// <summary>
        /// How long a citizen must be continuously <see cref="PawnTier.Interval"/> and insignificant before
        /// the director settles them into the cohort (<see cref="God.AttentionManager"/>, which is the only
        /// caller; <see cref="Pawn_TierTracker.DemoteToStatistical"/> still refuses to know this number).
        ///
        /// <para/><b>SimWorld's own; it could not be sourced from RimWorld</b>, which has no equivalent
        /// decision to make — a RimWorld pawn becomes a world pawn the instant it leaves the map, with no
        /// waiting period at all, because there is no middle tier for it to wait in. §11.5 lists <i>when</i>
        /// to settle as explicitly undecided, so this is a decision taken here rather than a constant copied
        /// from anywhere.
        ///
        /// <para/>One in-game year (<see cref="GenDate.TicksPerYear"/>), deliberately the same span as
        /// <see cref="DemographyTuning.DemographyIntervalTicks"/>: a citizen who has lived through a whole
        /// demographic sweep — a year in which they could have married, borne a child or died of age — with
        /// nobody attending them is deep population by any reading, and the year is the largest natural unit
        /// this simulation has below a lifetime. The cost of waiting is small and the cost of not waiting is
        /// not: Interval is already ~175-225x cheaper than Full (§11.3's measured table) so the year buys
        /// back only the further ~5x to Statistical, while settling too eagerly permanently thins what the
        /// chronicle can say about a citizen the player merely glanced away from (§11.4 — fidelity is decided
        /// at write time and never recovered). Pinned as a boundary either side of this constant by
        /// <c>AttentionTests.A_citizen_settles_to_Statistical_only_once_the_threshold_has_passed</c>, never as
        /// a literal.
        /// </summary>
        public const int IntervalSettleTicks = GenDate.TicksPerYear;
    }
}
