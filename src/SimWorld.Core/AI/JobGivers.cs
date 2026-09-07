using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// A think-tree leaf that either produces one job or nothing (RimWorld: <c>Verse.AI.ThinkNode_JobGiver</c>).
    /// Every concrete <c>JobGiver_*</c> below derives from this directly, matching RimWorld's own shape.
    /// </summary>
    public abstract class ThinkNode_JobGiver : ThinkNode
    {
        public sealed override ThinkResult TryIssueJobPackage(Pawn pawn)
        {
            Job? job = TryGiveJob(pawn);
            return job != null ? new ThinkResult(job, this) : ThinkResult.NoJob;
        }

        protected abstract Job? TryGiveJob(Pawn pawn);
    }

    /// <summary>
    /// Finds the nearest reachable, unclaimed, edible spawned Thing and issues a job to eat it (RimWorld:
    /// <c>RimWorld.JobGiver_GetFood</c>, trimmed). <b>Deviation:</b> real RimWorld scores candidates by a
    /// preferability/distance/perishability blend (<c>FoodUtility.BestFoodSourceOnMap</c>); this port picks
    /// the nearest reachable edible item outright, which is enough to prove the loop (find → path → eat →
    /// need falls) without porting that whole scoring function.
    /// </summary>
    public sealed class JobGiver_GetFood : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            Need_Food? food = pawn.needs.food;
            Map.Map? map = pawn.Map;
            if (food == null || map == null) return null;

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.def.IsNutritionGivingIngestible) continue;
                if (!map.reservationManager.CanReserve(pawn, t)) continue;
                int distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!Reachability.CanReach(pawn, t, PathEndMode.OnCell)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best != null ? new Job(JobDefOf.Ingest, best) : null;
        }
    }

    /// <summary>
    /// Lies down and sleeps in place (RimWorld: <c>RimWorld.JobGiver_GetRest</c>, trimmed). <b>Deviation:</b>
    /// real RimWorld first looks for an assigned or free bed via <c>RestUtility.FindBedFor</c>; no bed
    /// building exists in this pass yet, so every pawn sleeps on the ground at <see cref="Need_Rest.lastRestEffectiveness"/>
    /// 1 — already the documented ground-effectiveness default in <c>Need_Rest</c>.
    /// </summary>
    public sealed class JobGiver_GetRest : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (pawn.needs.rest == null || pawn.Map == null) return null;
            return new Job(JobDefOf.LayDown, pawn.Position);
        }
    }

    /// <summary>Pops the first queued player-forced job, if any (see this module's report on why this sits
    /// inside the tree rather than short-circuiting it, as real RimWorld's job queue does).</summary>
    public sealed class JobGiver_DirectedOrder : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) => pawn.jobs.DequeueDirectedOrder();
    }

    /// <summary>
    /// Scans by work priority, then natural priority, then priority within type — <see cref="Pawn_WorkSettings"/>
    /// already orders <see cref="WorkGiverDef"/>s exactly that way; this just walks the two lists it exposes,
    /// emergency first (RimWorld: <c>RimWorld.JobGiver_Work</c>).
    /// </summary>
    public sealed class JobGiver_Work : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike || !pawn.workSettings.EverWork || pawn.Map == null) return null;

            Job? job = TryGiveJobInGivers(pawn, pawn.workSettings.WorkGiversInOrderEmergency);
            return job ?? TryGiveJobInGivers(pawn, pawn.workSettings.WorkGiversInOrderNormal);
        }

        private static Job? TryGiveJobInGivers(Pawn pawn, IReadOnlyList<WorkGiverDef> givers)
        {
            for (int i = 0; i < givers.Count; i++)
            {
                if (!(givers[i].Worker is WorkGiver_Scanner scanner)) continue;
                if (scanner.ShouldSkip(pawn) || scanner.MissingRequiredCapacity(pawn)) continue;
                Job? job = TryFindJobOnScanner(pawn, scanner);
                if (job != null) return job;
            }
            return null;
        }

        private static Job? TryFindJobOnScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            Map.Map map = pawn.Map!;
            Thing? best = null;
            int bestDistSq = int.MaxValue;
            foreach (Thing t in scanner.PotentialWorkThingsGlobal(pawn))
            {
                if (!t.Spawned || t.Map != map) continue;
                int distSq = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!scanner.HasJobOnThing(pawn, t)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best != null ? scanner.JobOnThing(pawn, best) : null;
        }
    }

    /// <summary>
    /// Wanders to a random reachable, standable cell within <see cref="WanderRadius"/> (RimWorld:
    /// <c>RimWorld.JobGiver_WanderAnywhere</c>). Tries a bounded number of random offsets rather than
    /// scanning every cell in the radius, so an idle colony of pawns never pays an O(radius²) cost each
    /// time one of them looks for somewhere to wander.
    /// </summary>
    public class JobGiver_WanderAnywhere : ThinkNode_JobGiver
    {
        /// <summary>RimWorld's own wander radius is not a single sourced constant across contexts; picked to
        /// comfortably clear a small test map without wandering distractingly far on a large one.</summary>
        public const float WanderRadius = 7f;

        private const int MaxTries = 8;

        protected override Job? TryGiveJob(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;
            int count = GenRadial.NumCellsInRadius(WanderRadius);
            if (count <= 1) return null;

            for (int i = 0; i < MaxTries; i++)
            {
                IntVec3 offset = GenRadial.RadialPattern[Rand.Range(1, count)];
                IntVec3 candidate = pawn.Position + offset;
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                return new Job(JobDefOf.GotoWander, candidate);
            }
            return null;
        }
    }
}
