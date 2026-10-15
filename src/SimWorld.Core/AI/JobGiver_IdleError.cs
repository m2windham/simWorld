using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// The last node of a think tree, for a pawn nothing above it could find a thing to do (RimWorld:
    /// <c>Verse.AI.JobGiver_IdleError</c>): wait a hundred ticks, then think again.
    ///
    /// <para/><b>Why a tree must end in a job.</b> <see cref="Pawn_JobTracker.JobTrackerTick"/> asks the tree
    /// for a job on every tick a pawn has none, exactly as RimWorld's does. RimWorld never pays for that
    /// because its tree cannot come back empty: this node is its answer of last resort, so a pawn it cannot
    /// use waits and is not asked again until the wait ends or something interrupts it. This port's tree used
    /// to end in <see cref="JobGiver_WanderAnywhere"/>, which gives up after eight random offsets all land
    /// on cells it cannot stand on or reach — and a pawn standing in a place with no such cell (three raiders
    /// spawned inside a rock face were the case that found it) was asked the whole tree, every tier, on every
    /// tick, for as long as it lived: 37 seconds of CPU apiece over four in-game days, and a share of the random
    /// stream drawn each tick by the wander's offsets, for pawns that were doing nothing at all.
    ///
    /// <para/><b>RimWorld logs an error here</b> ("The behavior tree should never get here"), because its
    /// wander never fails. This port's can, legitimately, so the node is quiet: a pawn boxed in, or a raider
    /// with no way out, simply waits. The wait is an ordinary <see cref="JobDriver_Wait"/> job, so the
    /// constant think tree can still break into it the moment there is somebody to fight.
    /// </summary>
    public sealed class JobGiver_IdleError : ThinkNode_JobGiver
    {
        /// <summary>How long the wait lasts before the pawn thinks again (RimWorld: <c>JobGiver_IdleError.WaitTime</c>).</summary>
        public const int WaitTime = 100;

        protected override Job? TryGiveJob(Pawn pawn) => new Job(WaitJobDefOf.Wait) { expiryInterval = WaitTime };
    }

    /// <summary>The wait job. Its own <c>[DefOf]</c> class in its own file, beside the giver that issues it
    /// (CLAUDE.md: a new binding is a new class, not a field appended to <see cref="JobDefOf"/>).</summary>
    [DefOf]
    public static class WaitJobDefOf
    {
        /// <summary>Stand where you are until the job expires (RimWorld: <c>JobDefOf.Wait</c>, driven by
        /// <see cref="JobDriver_Wait"/> as <see cref="BurningJobDefOf.Wait_Wander"/> is).</summary>
        public static JobDef Wait = null!;
    }
}
