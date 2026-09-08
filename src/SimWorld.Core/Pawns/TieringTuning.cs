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
    }
}
