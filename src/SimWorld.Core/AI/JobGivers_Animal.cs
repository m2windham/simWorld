using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Paths a wild, skittish animal away from the nearest humanlike it can see (RimWorld:
    /// <c>RimWorld.JobGiver_AnimalFlee</c>, trimmed — RimWorld's real version consults an attack-targets cache
    /// scoped to hostile factions; Combat is out of this lane's boundary, so this port flees from any nearby
    /// Humanlike at all, which is right for an untamed wild animal and a harmless no-op for anything that
    /// never gets this far — see <see cref="ShouldFlee"/>).
    /// </summary>
    public sealed class JobGiver_AnimalFlee : ThinkNode_JobGiver
    {
        /// <summary>Not RimWorld-sourced (see <see cref="AnimalTuning"/>): how far a fleeing animal looks for
        /// a threat.</summary>
        public const float FleeDetectionRadius = 9f;

        private const int MaxTries = 8;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null || !ShouldFlee(pawn)) return null;

            Pawn? threat = NearestThreat(pawn, map);
            if (threat == null) return null;

            IntVec3 dest = FindFleeDestination(pawn, map, threat.Position);
            return dest.IsValid ? new Job(JobDefOf.GotoWander, dest) : null;
        }

        /// <summary>Only an untamed animal wild enough to bother (RimWorld: a tamed/player-owned animal does
        /// not flee its own colonists).</summary>
        public static bool ShouldFlee(Pawn pawn) =>
            pawn.RaceProps.Animal && pawn.faction == null && pawn.RaceProps.wildness >= AnimalTuning.FleeWildnessThreshold;

        private static Pawn? NearestThreat(Pawn pawn, Map.Map map)
        {
            Pawn? best = null;
            int bestDistSq = int.MaxValue;
            float radiusSq = FleeDetectionRadius * FleeDetectionRadius;
            IReadOnlyList<Thing> pawnsOnMap = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < pawnsOnMap.Count; i++)
            {
                if (!(pawnsOnMap[i] is Pawn other) || !other.RaceProps.Humanlike || other.Dead) continue;
                int distSq = (other.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq > radiusSq || distSq >= bestDistSq) continue;
                best = other;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>A standable, reachable cell strictly farther from <paramref name="threatPos"/> than the
        /// pawn's current position, tried a bounded number of times rather than scanned exhaustively — the
        /// same shape as <see cref="JobGiver_WanderAnywhere"/>, whose radius this reuses.</summary>
        private static IntVec3 FindFleeDestination(Pawn pawn, Map.Map map, IntVec3 threatPos)
        {
            int startDistSq = (pawn.Position - threatPos).LengthHorizontalSquared;
            int count = GenRadial.NumCellsInRadius(JobGiver_WanderAnywhere.WanderRadius);
            if (count <= 1) return IntVec3.Invalid;

            for (int i = 0; i < MaxTries; i++)
            {
                IntVec3 offset = GenRadial.RadialPattern[Rand.Range(1, count)];
                IntVec3 candidate = pawn.Position + offset;
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if ((candidate - threatPos).LengthHorizontalSquared <= startDistSq) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                return candidate;
            }
            return IntVec3.Invalid;
        }
    }
}
