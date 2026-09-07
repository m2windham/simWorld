namespace SimWorld.AI
{
    /// <summary>Which of a <see cref="Job"/>'s three targets a Toil/FailOn helper refers to (RimWorld: <c>Verse.AI.TargetIndex</c>).</summary>
    public enum TargetIndex
    {
        A,
        B,
        C,
    }

    /// <summary>
    /// How a job ended, or its current status while running (RimWorld: <c>Verse.AI.JobCondition</c>).
    /// <b>Deviation:</b> the brief asked for a separate "JobEndOutcome" type; real RimWorld has no such
    /// type — <c>JobCondition</c> is both the running/ended status and the value <c>EndJobWith</c> takes,
    /// so this port uses the one real enum rather than inventing a second.
    /// </summary>
    public enum JobCondition : byte
    {
        None,
        Ongoing,
        Succeeded,
        Incompletable,
        Errored,
        InterruptForced,
        InterruptOptional,
        QueuedNoLongerValid,
    }

    /// <summary>
    /// Why a job exists, for bookkeeping (RimWorld: <c>Verse.AI.JobTag</c>). RimWorld's real enum lists
    /// several dozen values across every system (combat, hauling, social...); this port keeps the handful
    /// this module's own job givers use.
    /// </summary>
    public enum JobTag
    {
        Misc,
        SatisfyBasicNeeds,
        Idle,
        Work,
        DirectedOrder,
    }

    /// <summary>
    /// What a haul job should do with its payload (RimWorld: <c>Verse.HaulMode</c>). Carried on <see cref="Job"/>
    /// for shape fidelity; hauling itself (no zones exist yet) is out of this pass's scope, so nothing sets
    /// this to anything but <see cref="Undefined"/>.
    /// </summary>
    public enum HaulMode
    {
        Undefined,
        ToCellStorage,
        ToCellNonStorage,
        ToContainer,
    }

    /// <summary>Where a pather should stop relative to a job's target (RimWorld: <c>Verse.AI.PathEndMode</c>).</summary>
    public enum PathEndMode
    {
        /// <summary>Stand exactly on the target cell.</summary>
        OnCell,
        /// <summary>Stand adjacent to the target (used for impassable things — mining, chopping).</summary>
        Touch,
        /// <summary>Like <see cref="Touch"/>; RimWorld distinguishes a "closest approach" variant this port does not.</summary>
        ClosestTouch,
        /// <summary>RimWorld's per-ThingDef interaction cell; no such per-def cell exists yet, so this port
        /// treats it the same as <see cref="Touch"/>.</summary>
        InteractionCell,
        /// <summary>No positional requirement at all; the job needs no path.</summary>
        None,
    }

    /// <summary>
    /// When a <see cref="Toil"/> is considered finished (RimWorld: <c>Verse.AI.ToilCompleteMode</c>). RimWorld
    /// also has <c>FinishedBusy</c> for melee busy-stances; combat beyond fleeing is out of this pass's scope
    /// so it is omitted here.
    /// </summary>
    public enum ToilCompleteMode
    {
        /// <summary>Finishes the instant its <c>initAction</c> returns.</summary>
        Instant,
        /// <summary>Finishes after <see cref="Toil.defaultDuration"/> ticks of <c>tickAction</c>.</summary>
        Delay,
        /// <summary>Finishes when <see cref="Pawn_PathFollower"/> stops moving (arrival or a path failure).</summary>
        PatherArrival,
        /// <summary>Never finishes on its own; only an explicit <see cref="JobDriver.EndJobWith"/> ends it.</summary>
        Never,
    }
}
