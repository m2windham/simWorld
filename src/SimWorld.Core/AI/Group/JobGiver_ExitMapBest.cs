using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// Walk off the map by the nearest edge this pawn can reach (RimWorld: <c>Verse.AI.JobGiver_ExitMapBest</c>,
    /// the whole of <c>DutyDefOf.ExitMapBest</c>). Unlike <see cref="JobGiver_ExitMap"/>, the last tier of an
    /// assault, it does not first ask whether anybody is left to fight: a pawn given this has been told to
    /// go, and goes.
    ///
    /// <para/><b>A prisoner is refused.</b> Nothing in RimWorld hands a prisoner this job — prisoners have
    /// their own branch of the think tree and no duty. Here a raider the lord lost while it was down keeps
    /// this as its duty (see <see cref="Lord"/>'s <c>RemovePawn</c>), and that raider may be captured before
    /// it gets up; without this check the settlement's prisoner would walk out of its cell and off the map.
    /// </summary>
    public class JobGiver_ExitMapBest : ThinkNode_JobGiver
    {
        /// <summary>How far along an edge to look for a cell to leave by; the same bound, for the same
        /// reason, as <see cref="JobGiver_ExitMap"/>'s.</summary>
        private const int EdgeSearchSpan = 40;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;
            if (CaptureUtility.FindHostFaction(pawn) != null) return null;

            IntVec3 exit = ApproachUtility.BestExitCell(pawn, map, EdgeSearchSpan);
            if (!exit.IsValid) return null;
            return new Job(DutyJobDefOf.ExitMap, exit);
        }
    }

    /// <summary>
    /// Flee off the map (RimWorld: <c>Verse.AI.JobGiver_ExitMapPanic</c>, the first tier of the
    /// <c>PanicFlee</c> mental state's subtree). In RimWorld it is <see cref="JobGiver_ExitMapBest"/> that may
    /// bash through a door in its way; this port has no bashing, so it is the same job — a class of its own so
    /// content says which of the two it means.
    /// </summary>
    public sealed class JobGiver_ExitMapPanic : JobGiver_ExitMapBest
    {
    }

    /// <summary>Duties a <see cref="Lord"/>'s toils hand out. Its own <c>[DefOf]</c> class rather than fields
    /// added to <see cref="DutyDefOf"/> (CLAUDE.md: a new binding goes in its own class).</summary>
    [DefOf]
    public static class LordDutyDefOf
    {
        /// <summary>Leave by the nearest edge (RimWorld: <c>DutyDefOf.ExitMapBest</c>). What
        /// <see cref="LordToil_ExitMap"/> hands out, and what a member lost alive is left carrying.</summary>
        public static DutyDef ExitMapBest = null!;

        /// <summary>Run for the nearest edge — RimWorld's <c>PanicFlee</c> mental state's subtree, carried as a
        /// duty here. What <see cref="LordToil_PanicFlee"/> hands out.</summary>
        public static DutyDef PanicFlee = null!;
    }

    /// <summary>Questions about lords that other modules ask. Kept to what is read today.</summary>
    public static class LordUtility
    {
        /// <summary>
        /// Whether this pawn's standing orders are to leave the map — given up, fled, or lost by its lord and
        /// sent home. Read by <see cref="CombatPostureUtility.TargetAcquireRadiusFor"/>, which is what keeps a
        /// retreating raider from turning to fight everyone it passes.
        /// </summary>
        public static bool IsLeaving(Pawn pawn)
        {
            DutyDef? duty = pawn.mindState?.duty;
            return duty != null && (ReferenceEquals(duty, LordDutyDefOf.ExitMapBest) || ReferenceEquals(duty, LordDutyDefOf.PanicFlee));
        }

        /// <summary>The lord that owns <paramref name="pawn"/>, or null (RimWorld: <c>LordUtility.GetLord</c>).</summary>
        public static Lord? GetLord(this Pawn pawn) => pawn.Map?.lordManager.LordOf(pawn);
    }
}
