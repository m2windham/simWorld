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

        /// <summary>
        /// How many citizens of the focused settlement may sit at <see cref="PawnTier.Full"/> at once —
        /// the Full-tier budget <see cref="God.AttentionBudget"/> fills, most significant first.
        ///
        /// <para/><b>SimWorld's own; it could not be sourced from RimWorld.</b> RimWorld has no middle tier
        /// and therefore no budget to spend: a pawn is on the map (full) or it is a world pawn (frozen), and
        /// the number of pawns on a map is bounded by the map, not by a policy. §11.5 lists tier budgets as
        /// explicitly undecided and says where the answer has to come from — "measurement, not guesswork" —
        /// so this is read off <c>docs/perf/baseline.md</c> rather than chosen for roundness.
        ///
        /// <para/><b>500, and what that is defended with.</b> Two independent readings of the same report
        /// land either side of it:
        /// <list type="bullet">
        /// <item><description><b>The last flat point on the measured curve.</b> baseline.md §1 times whole
        /// pawn-days at seven populations, and per-pawn cost is <i>not</i> flat: 14.0-14.1 ms/pawn-day
        /// through N=500, then 17.4 at N=1,000 and 21.7 at N=2,500 as GC and cache pressure start being paid
        /// (§4's ~4.2 MB/pawn-day is the mechanism). N=500 is the largest measured population at which one
        /// more citizen still costs what the last one did, which is the property a budget wants — past it,
        /// the marginal citizen is quietly more expensive than the average one, and a budget set there would
        /// be spending money it had not counted.</description></item>
        /// <item><description><b>Inside the sick-population ceiling, not the healthy one.</b> The healthy
        /// 15x ceiling is a measured 2,500-5,000 (§1), but "Where the ceiling is" is blunt that a colony
        /// where everyone is hale is not the normal case; folding in §10's post-fix 3.26x multiplier for
        /// wounds-and-a-disease puts a realistically sick population's 15x ceiling at a projected
        /// 750-1,500. 500 sits below the low end of that projection with room, so the budget holds at turbo
        /// speed in the state a settlement actually spends its life in, not only in the best case.
        /// A budget defended by a projection should sit under it, not on it.</description></item>
        /// </list>
        ///
        /// <para/>It is also large enough to be a town rather than a cast list — §11.4's point is that the
        /// tier a citizen lived at is what the chronicle can ever say about them, so a budget tight enough to
        /// be comfortable would be buying performance with history. Pinned by behaviour
        /// (<c>AttentionBudgetTests</c> builds rosters relative to this constant and asserts the shape of
        /// what is kept), never by the literal 500 appearing in an assertion.
        /// </summary>
        public const int FullTierBudget = 500;
    }
}
