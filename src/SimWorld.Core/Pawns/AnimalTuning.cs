using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Tuning for taming, training and wild-animal behaviour (system: Animals — <c>ai.animals</c> in
    /// <c>docs/status.json</c>). RimWorld's own curves for taming odds and training decay are not available
    /// to this port; every constant below is this port's own choice. What each one backs is pinned by a
    /// trend test in <c>AITests</c> or <c>PawnsTests</c> — never the literal — and each constant says which
    /// trend. See <c>Crafting.HusbandryTuning</c> for the produce/butcher side of the same module.
    /// </summary>
    public static class AnimalTuning
    {
        // ---- taming (TameUtility) ----

        /// <summary>Chance floor before the tamer's skill and the animal's wildness are applied.</summary>
        public const float TameBaseChance = 0.5f;

        /// <summary>Added per Animals skill level (0-20): a level-20 handler gains +0.6 over a level-0 one.
        /// Backs "a better handler tames more often".</summary>
        public const float TameSkillFactorPerLevel = 0.03f;

        /// <summary>Subtracted, scaled by the animal's wildness (0-1): a maximally wild animal loses this
        /// much outright. Backs "a wilder animal resists more".</summary>
        public const float TameWildnessFactor = 0.9f;

        public const float TameSuccessXp = 40f;

        /// <summary>
        /// How long an animal that has turned on a handler stays angry at them. <b>The port's own number</b>:
        /// RimWorld's revenge is a full <c>Manhunter</c> mental state with its own duration, and this port's
        /// stand-in for it is the personal grudge <see cref="MindState.Pawn_MindState.angryAt"/>, which needs a
        /// deadline. <b>Whether</b> an animal turns on a handler is no longer a number here at all: it is the
        /// race's own <see cref="RaceProperties.manhunterOnTameFailChance"/>, rolled by
        /// <see cref="TameUtility.TryTame"/> the way RimWorld rolls it. There used to be a
        /// <c>TameFailAngerChance</c> (0.5, scaled by wildness) in this slot; it was invented, and it turned a
        /// third of every failed muffalo taming into a day-long mauling that RimWorld does not have.
        /// </summary>
        public const int TameFailAngerDurationTicks = GenDate.TicksPerDay;

        /// <summary>
        /// Ticks after a taming attempt before the same animal can be tried again (RimWorld:
        /// <c>TameUtility.MinTameInterval</c> = 30000, twelve in-game hours, read by
        /// <c>TameUtility.TriedToTameTooRecently</c> in both <c>WorkGiver_Tame</c> and
        /// <c>JobDriver_Tame</c>). Sourced from the decompile, not tuned.
        /// </summary>
        public const int MinTameIntervalTicks = 30000;

        // ---- training (Pawn_TrainingTracker) ----

        public const float TrainXp = 20f;

        /// <summary>How often <see cref="Pawn_TrainingTracker.TrainingTrackerTick"/> actually checks for
        /// decay — a coarse cadence (~33s of game time), never per-tick-per-animal work
        /// (<c>docs/perf/baseline.md</c>).</summary>
        public const int TrainingDecayCheckIntervalTicks = GenTicks.TickLongInterval;

        /// <summary>No decay roll happens within this long of the animal's last successful training
        /// session. Backs "reinforced training does not decay".</summary>
        public const int TrainingDecayGraceTicks = GenDate.TicksPerDay * 4;

        /// <summary>Mean-time-between-decay-events once outside the grace window. Backs "untended training
        /// can decay eventually".</summary>
        public const float TrainingDecayMtbDays = 6f;

        // ---- flee (JobGiver_AnimalFlee) ----

        /// <summary>Minimum wildness for an untamed animal to bother fleeing a nearby humanlike at all.</summary>
        public const float FleeWildnessThreshold = 0.4f;
    }
}
