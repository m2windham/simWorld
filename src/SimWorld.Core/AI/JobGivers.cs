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
    /// Lies down and sleeps in place (RimWorld: <c>RimWorld.JobGiver_GetRest</c>, trimmed). Looks for a free
    /// bed via <see cref="RestUtility.FindBedFor"/> first, the way real RimWorld's own <c>JobGiver_GetRest</c>
    /// does; a pawn with no reachable, unclaimed bed sleeps on the ground exactly as this always did, at
    /// <see cref="Need_Rest.lastRestEffectiveness"/> 1 (that field's own documented ground default).
    /// </summary>
    public sealed class JobGiver_GetRest : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            if (pawn.needs.rest == null || pawn.Map == null) return null;
            // The household's own dwelling first, as RimWorld tries a pawn's owned bed first, while it is out of the
            // cold; then a bed that is (exposure4 lane, RestUtility.FindRestBedFor); then home anyway, then any bed.
            Thing? bed = RestUtility.FindRestBedFor(pawn, pawn);
            return bed != null ? new Job(JobDefOf.LayDown, bed) : new Job(JobDefOf.LayDown, pawn.Position);
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
    /// already orders <see cref="WorkGiverDef"/>s exactly that way; this walks one of the two lists it exposes
    /// (RimWorld: <c>RimWorld.JobGiver_Work</c>). The actual nearest-candidate scan is shared with
    /// <see cref="JobGiver_Edicts"/> via <see cref="WorkGiverScanUtility"/> rather than duplicated here.
    /// <para/>
    /// <b>Two instances, two tiers, RimWorld's shape.</b> <see cref="emergency"/> picks the list, as it does in
    /// RimWorld, and the humanlike tree carries this node twice: once with <c>emergency</c> set, above every
    /// need but starvation ("Emergency work" in RimWorld's own <c>Humanlike</c> tree), and once without it, in
    /// the routine-work slot. Until this port had the upper instance, the one node scanned the emergency list
    /// first and then the routine one, both below hunger, sleep and recreation — so a tired doctor went to bed
    /// beside a colonist bleeding to death and slept until rested. See <c>ThinkTrees_Humanlike.xml</c>.
    /// </summary>
    public sealed class JobGiver_Work : ThinkNode_JobGiver
    {
        /// <summary>Scan the emergency work givers (firefighting, urgent tending) instead of the routine ones
        /// (RimWorld: <c>JobGiver_Work.emergency</c>). Set from content.</summary>
        public bool emergency;

        protected override Job? TryGiveJob(Pawn pawn) =>
            emergency ? TryGiveEmergencyJob(pawn) : TryGiveRoutineJob(pawn);

        /// <summary>
        /// The emergency work givers alone. Public because it is also the question a sleeping pawn asks every
        /// <see cref="JobDriver_LayDown.LookForOtherJobsIntervalTicks"/> — see that driver.
        /// </summary>
        public static Job? TryGiveEmergencyJob(Pawn pawn)
        {
            if (!CanWork(pawn)) return null;
            return WorkGiverScanUtility.TryGiveJobInGivers(pawn, pawn.workSettings.WorkGiversInOrderEmergency);
        }

        private static Job? TryGiveRoutineJob(Pawn pawn)
        {
            if (!CanWork(pawn)) return null;
            // A day of rest stops routine work by decree (EdictDef.restDay). The emergency list above still runs.
            if (God.EdictWorkUtility.OnRestDay(pawn)) return null;
            // No gate for edicts here: the edict tier sits directly above and has already handed out any of its
            // work this pawn could take. Reaching this line means there was none, and RimWorld's JobGiver_Work
            // never idles a pawn for want of their first-priority work while lower work waits.
            return WorkGiverScanUtility.TryGiveJobInGivers(pawn, pawn.workSettings.WorkGiversInOrderNormal);
        }

        private static bool CanWork(Pawn pawn) =>
            pawn.RaceProps.Humanlike && pawn.workSettings != null && pawn.workSettings.EverWork && pawn.Map != null;
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

        /// <summary>RimWorld: the <c>PerceivedPathCostAt(c) &gt; 20</c> test in <c>RCellFinder.CanWanderToCell</c>.</summary>
        public const int MaxPerceivedPathCostToWander = 20;

        /// <summary>How many of the <see cref="MaxTries"/> apply that test. Half, so a map with no fire and no
        /// costly ground draws exactly as it always did.</summary>
        private const int StrictTries = MaxTries / 2;

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
                // RimWorld: RCellFinder.CanWanderToCell refuses, for its early tries, any cell whose perceived
                // path cost is over 20 — and a fire on or beside a cell is perceived as 150 to 1,000+ (see
                // Map.PathGrid.CalculatedCostAt). That is what keeps a wanderer, and a burning pawn running
                // about (JobGiver_RunRandom), out of a fire instead of into it. The later tries drop the
                // test, as RimWorld's do, so a pawn ringed by fire still moves. Recalled, not sourced.
                if (i < StrictTries && map.pathGrid.PerceivedPathCostAt(candidate) > MaxPerceivedPathCostToWander) continue;
                if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;
                return new Job(JobDefOf.GotoWander, candidate);
            }
            return null;
        }
    }
}
