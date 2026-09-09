using SimWorld.Sim;

namespace SimWorld.God
{
    /// <summary>
    /// God-layer tuning. RimWorld has no edict system to source numbers from — everything here is SimWorld's
    /// own, documented at its declaration and pinned by <c>GodTests</c> behaviour (a band or a boundary, not
    /// the literal) rather than trusted as a magic constant.
    /// </summary>
    public static class GodTuning
    {
        /// <summary>
        /// How many standing edicts a civilization can hold active at once. Deliberately small: a god view
        /// that read every unlocked edict as "on" all the time would not be a choice, it would be a checklist.
        /// Three lets a civilization commit to a real trade-off (a labor push, a research direction, a
        /// tolerated hardship) without the slot budget itself becoming the whole game — not sourced from
        /// RimWorld (nothing there caps how many things a player can order at once); pinned by
        /// <c>GodTests.Slot_budget_refuses_activation_past_the_cap</c> rather than trusted as a bare literal.
        /// </summary>
        public const int MaxActiveEdicts = 3;

        /// <summary>
        /// Cadence for <see cref="GodManager.GodTick"/> and <see cref="GodRollup"/>'s recompute — the "rare or
        /// long bucket, not per tick" the brief asks for. Reuses <see cref="GenTicks.TickLongInterval"/>
        /// (2000 ticks, ~33s) rather than inventing a third interval: nothing about the god layer needs to be
        /// fresher than the tier system's own coarse tick (<c>docs/spec/simworld-spec.md</c> §11.3), and a
        /// mismatched cadence would just mean the rollup and the tier ticks disagree about how stale "current"
        /// is allowed to be.
        /// </summary>
        public const int GodTickIntervalTicks = GenTicks.TickLongInterval;

        /// <summary>
        /// Band a settlement's bare Statistical population is sampled within for <see cref="GodRollup.MeanIndustrySkill"/>
        /// (<c>SkillRecord</c> levels, 0-<see cref="Work.SkillRecord.MaxLevel"/>) — there is no per-person
        /// <c>SkillRecord</c> at that tier to read, by design (spec §11.3), so this is SimWorld's own stand-in,
        /// not sourced from RimWorld (which has no cohort-sampling concept for skills either). Deliberately
        /// low and narrow rather than centered on the skill range's midpoint: every promotion trigger
        /// (<c>Pawn_TierTracker.Notify_RoleChanged</c> among them — "leader, founder, great worker") already
        /// pulls a genuinely skilled citizen up to Full, so whoever is left in an untouched Statistical cohort
        /// is, by construction, nobody a civilization singled out for skill. Pinned by
        /// <c>GodTests.A_settlements_statistical_cohort_is_folded_into_the_means_by_a_weighted_sample</c> as a
        /// band, not a literal.
        /// </summary>
        public const float StatisticalIndustrySkillSampleMin = 1f;

        public const float StatisticalIndustrySkillSampleMax = 6f;
    }
}
