using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks toward the nearest enemy that is too far away to fight yet (RimWorld:
    /// <c>RimWorld.JobGiver_AIGotoNearestHostile</c>, the tier <c>DutyDefOf.AssaultColony</c>'s think node
    /// carries under the fight tier). This is the missing half of "a raid arrives": both sides could see each
    /// other and both would fight, and nothing ever closed the distance between them.
    ///
    /// <para/><b>The measurement this exists for.</b> A real <c>RaidEnemy</c> onto a settlement founded the
    /// ordinary way — 30 citizens, 200x200 interior — put the squad down <b>103 cells</b> from the town
    /// centre, and over 10,000 ticks it closed <b>11</b> of them, all of it aimless drift from
    /// <see cref="JobGiver_WanderAnywhere"/> at the bottom of the tree. The first attack job of that raid was
    /// at tick 6,408, and it happened because a <i>citizen</i> working the map edge walked into the raiders:
    /// the raid never moved on the town at all. <see cref="CombatAITuning.TargetAcquireRadius"/> is 40 cells
    /// and the gap was 103, so every acquire scan on both sides correctly returned nothing, forever.
    ///
    /// <para/><b>Acquire radius is not this tier's radius.</b> The scan here is unbounded
    /// (<see cref="float.MaxValue"/>): "who is my enemy" and "who can I shoot from here" are different
    /// questions, and conflating them is precisely the bug above. Cost is not the reason the fight tier is
    /// bounded either — <see cref="AttackTargetsCache"/> makes a scan proportional to the hostile pawns
    /// present, not to the radius, so an unbounded scan of a map with no enemy on it still looks at nobody.
    ///
    /// <para/><b>Why it stops at the fight tier's radius rather than at the target's cell.</b> Once the enemy
    /// is inside <see cref="CombatPostureUtility.TargetAcquireRadiusFor"/> this tier contributes nothing and
    /// the combat tier above takes the pawn — including for an unarmed raider, whose radius is one cell, so
    /// this walks them all the way into arm's reach and hands over there. Two tiers, one handover, no second
    /// copy of "am I close enough".
    /// </summary>
    public class JobGiver_AIGotoNearestHostile : ThinkNode_JobGiver
    {
        /// <summary>
        /// How long one approach job may run before the pawn re-decides, through <see cref="Job.expiryInterval"/>.
        /// Not a RimWorld number (its own giver re-runs whenever the pawn next asks for a job, which for a
        /// lord-driven pawn is a different cadence entirely): the target is a moving pawn and this job is
        /// issued against the cell it stood on, so the ceiling is what keeps a squad marching at a ghost from
        /// marching at it forever. Deliberately the same hour <see cref="CombatAITuning.AttackJobExpiryTicks"/>
        /// uses, for the same reason it gives — an approach that has not arrived in an hour is one worth
        /// re-aiming, and re-deciding is cheap because the very next pass re-issues the same job if nothing
        /// has changed. What the test pins is that an approach job carries a ceiling at all, never the hour.
        /// </summary>
        public const int ApproachJobExpiryTicks = CombatAITuning.AttackJobExpiryTicks;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Pawn? enemy = ApproachUtility.AssaultTargetFor(pawn);
            if (enemy == null) return null;

            // Inside the fight tier's radius this tier is done: the combat tier above is what fires there,
            // and issuing a walk as well would have the pawn drop its attack job to take one step.
            float distance = (enemy.Position - pawn.Position).LengthHorizontal;
            if (distance <= CombatPostureUtility.TargetAcquireRadiusFor(pawn)) return null;

            return new Job(DutyJobDefOf.Goto, enemy.Position) { expiryInterval = ApproachJobExpiryTicks };
        }
    }

    /// <summary>
    /// Leaves the map (RimWorld: <c>RimWorld.JobGiver_ExitMapBest</c> / <c>LordToil_ExitMap</c>). The last
    /// tier of an assault duty, and the thing that gives a raid an ending: when there is nobody left on this
    /// map that this pawn would attack — every defender dead or down, or the town sealed off behind
    /// something it cannot path around — the raid is over for that raider and it walks off the edge it came
    /// in by.
    ///
    /// <para/><b>Why "no target" and not "we have taken enough casualties".</b> The second is the question a
    /// RimWorld <c>Trigger_FractionPawnsLost</c> answers for a whole Lord at once, and this port has no Lord
    /// to hold the roster it needs — see <see cref="DutyDef"/> for the full accounting of what that split
    /// costs. A raid here therefore fights to a finish and the survivors leave; it does not break and run.
    /// </summary>
    public class JobGiver_ExitMap : ThinkNode_JobGiver
    {
        /// <summary>How far along an edge to look for a cell this pawn can actually stand on and reach, from
        /// the point directly opposite it. Not sourced (RimWorld scans candidate edge cells through
        /// <c>CellFinder.TryFindRandomPawnExitCell</c>); bounded so a pawn boxed in against one edge gives up
        /// on that edge quickly and tries the next rather than walking the whole perimeter.</summary>
        private const int EdgeSearchSpan = 40;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;
            if (ApproachUtility.AssaultTargetFor(pawn) != null) return null;

            IntVec3 exit = ApproachUtility.BestExitCell(pawn, map, EdgeSearchSpan);
            if (!exit.IsValid) return null;
            return new Job(DutyJobDefOf.ExitMap, exit);
        }
    }

    /// <summary>Shared by the tiers of an assault duty, so "who am I here for" and "is there anybody left"
    /// are one question asked once rather than two that can disagree.</summary>
    public static class ApproachUtility
    {
        /// <summary>
        /// The enemy this pawn would go and assault, anywhere on the map, or null when there is none — which
        /// is the signal the duty is finished. Unbounded in range on purpose (see
        /// <see cref="JobGiver_AIGotoNearestHostile"/>), and it goes through the same
        /// <see cref="AttackTargetFinder.BestAttackTarget"/> the combat tier uses, so a squad never marches
        /// at somebody the fight tier would then refuse to engage — including the reachability test, which is
        /// what makes "the town is walled off" resolve as "go home" rather than as a permanent march.
        /// </summary>
        public static Pawn? AssaultTargetFor(Pawn pawn)
        {
            if (pawn.Map == null || !CombatPostureUtility.CanFight(pawn)) return null;
            return AttackTargetFinder.BestAttackTarget(pawn, float.MaxValue);
        }

        /// <summary>
        /// The nearest map-edge cell this pawn can stand on and reach, searched outward from the point of
        /// the nearest edge directly opposite it, then the other three edges in the same way. Invalid when
        /// every edge is walled off from where the pawn stands.
        /// </summary>
        public static IntVec3 BestExitCell(Pawn pawn, Map.Map map, int span)
        {
            // Nearest edge first, then the rest — a fixed order and never a random pick, since a raid
            // leaving has to play out the same way twice from the same seed (CLAUDE.md: determinism is a
            // feature). Selection-sorted in place over four ints rather than sorted into an array, so this
            // allocates nothing.
            int tried = 0;
            for (int rank = 0; rank < 4; rank++)
            {
                int nearest = -1;
                int nearestDistance = int.MaxValue;
                for (int edge = 0; edge < 4; edge++)
                {
                    if ((tried & (1 << edge)) != 0) continue;
                    int d = DistanceToEdge(pawn.Position, edge, map);
                    if (d >= nearestDistance) continue;
                    nearest = edge;
                    nearestDistance = d;
                }
                tried |= 1 << nearest;

                IntVec3 found = SearchEdge(pawn, map, nearest, span);
                if (found.IsValid) return found;
            }
            return IntVec3.Invalid;
        }

        /// <summary>Cells from <paramref name="at"/> to one edge: 0 = west, 1 = east, 2 = south, 3 = north.</summary>
        private static int DistanceToEdge(IntVec3 at, int edge, Map.Map map)
        {
            switch (edge)
            {
                case 0: return at.x;
                case 1: return map.Size.x - 1 - at.x;
                case 2: return at.z;
                default: return map.Size.z - 1 - at.z;
            }
        }

        /// <summary>One edge, walked outward from the cell directly opposite the pawn: 0 = west, 1 = east,
        /// 2 = south, 3 = north.</summary>
        private static IntVec3 SearchEdge(Pawn pawn, Map.Map map, int edge, int span)
        {
            IntVec3 at = pawn.Position;
            int maxX = map.Size.x - 1;
            int maxZ = map.Size.z - 1;
            bool vertical = edge <= 1;
            int fixedCoord = edge == 0 ? 0 : edge == 1 ? maxX : edge == 2 ? 0 : maxZ;
            int centre = vertical ? at.z : at.x;
            int limit = vertical ? maxZ : maxX;

            // offset 0 tries the same cell twice (sign 0 and 1); harmless, and cheaper than the branch that
            // would avoid it.
            for (int offset = 0; offset <= span; offset++)
            {
                for (int sign = 0; sign < 2; sign++)
                {
                    int along = centre + (sign == 0 ? offset : -offset);
                    if (along < 0 || along > limit) continue;
                    IntVec3 candidate = vertical ? new IntVec3(fixedCoord, along) : new IntVec3(along, fixedCoord);
                    if (!GenGrid.Standable(candidate, map)) continue;
                    if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }
    }
}
