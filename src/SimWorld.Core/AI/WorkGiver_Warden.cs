using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a prisoner of the warden's own faction set to <see cref="PrisonerInteractionModeDefOf.AttemptRecruit"/>
    /// and issues one visit (RimWorld: the effect of <c>RimWorld.JobDriver_ChatWithPrisoner</c> reaching
    /// <c>InteractionWorker_RecruitAttempt</c> — this pass's own <see cref="WardenUtility.TryInteract"/>
    /// already collapsed that whole multi-round chat into "one visit either lowers resistance or, once it's
    /// already zero, recruits outright"; this giver is only the missing other half — the scan that finds a
    /// prisoner and starts that job in the first place, reached through this port's own
    /// <see cref="WorkGiver_Scanner"/> shape rather than RimWorld's social-interaction scheduler, which this
    /// codebase does not have). Wires the <c>WardenAttemptRecruit</c> WorkGiverDef the same way
    /// <see cref="WorkGiver_Miner"/> wires <c>Mine</c>.
    /// </summary>
    public sealed class WorkGiver_Warden_AttemptRecruit : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null || pawn.faction == null) yield break;
            List<Pawn_GuestTracker> prisoners = pawn.faction.prisoners;
            for (int i = 0; i < prisoners.Count; i++)
            {
                Pawn? p = prisoners[i].pawn;
                if (p != null) yield return p;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn prisoner) || !thing.Spawned || thing.Map != pawn.Map) return false;
            if (pawn.faction == null) return false;
            Faction? host = CaptureUtility.FindHostFaction(prisoner, out Pawn_GuestTracker? tracker);
            if (host == null || tracker == null || !ReferenceEquals(host, pawn.faction)) return false;
            if (tracker.interactionMode != PrisonerInteractionModeDefOf.AttemptRecruit) return false;
            if (!Reachability.CanReach(pawn, prisoner, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, prisoner);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(JobDefOf.PrisonerAttemptRecruit, thing);
    }

    /// <summary>
    /// Finds a downed, hungry prisoner of the warden's own faction and carries the nearest reachable food to
    /// them (RimWorld: a trim of <c>RimWorld.WorkGiver_Warden_Feed</c> — real RimWorld's own trigger is "in
    /// bed and needs medical rest" (<c>WardenFeedUtility.ShouldBeFed</c>); this port has no bed/room system at
    /// all, so <see cref="Pawn.Downed"/> stands in as the "cannot reach food on its own" condition, the same
    /// role a bed plays upstream. <b>A prisoner that is not downed already reaches food on its own</b> through
    /// the ordinary <see cref="JobGiver_GetFood"/> tier of its think tree — nothing in this port's job
    /// selection checks guest status or faction at all (a captured pawn keeps its original <see cref="Pawn.faction"/>;
    /// only a <see cref="Faction.prisoners"/> lookup says who holds it — see <c>Pawn_GuestTracker</c>'s own
    /// remarks), so a merely-hungry-but-ambulatory prisoner is already served exactly like a colonist would be.
    /// That is also why <c>WardenDeliverFood</c> — RimWorld's counterpart for a prisoner capable of
    /// self-service but with nothing reachable to eat — stays a <see cref="WorkGiver_Pending"/> placeholder in
    /// this pass's content: this port's job selection carries no per-room "is there food already reachable
    /// here" distinction for <see cref="WorkGiver_Warden_Feed"/> to fall back from in the first place (see this
    /// module's report).
    /// </summary>
    public sealed class WorkGiver_Warden_Feed : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null || pawn.faction == null) yield break;
            List<Pawn_GuestTracker> prisoners = pawn.faction.prisoners;
            for (int i = 0; i < prisoners.Count; i++)
            {
                Pawn? p = prisoners[i].pawn;
                if (p != null) yield return p;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn prisoner) || !thing.Spawned || thing.Map != pawn.Map) return false;
            if (pawn.faction == null || !prisoner.Downed) return false;
            if (prisoner.needs.food == null || prisoner.needs.food.CurCategory == HungerCategory.Fed) return false;
            Faction? host = CaptureUtility.FindHostFaction(prisoner);
            if (host == null || !ReferenceEquals(host, pawn.faction)) return false;
            if (FindFoodFor(pawn) == null) return false;
            if (!Reachability.CanReach(pawn, prisoner, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, prisoner);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Thing? food = FindFoodFor(pawn);
            return food != null ? new Job(JobDefOf.FeedPatient, food, thing) : null;
        }

        /// <summary>Nearest reachable, unclaimed nutrition-giving item on the warden's map, if any (RimWorld:
        /// a trim of <c>FoodUtility.TryFindBestFoodSourceFor</c> — nearest reachable rather than scored by
        /// preferability/perishability, matching <see cref="JobGiver_GetFood"/>'s own trim of that same call).</summary>
        private static Thing? FindFoodFor(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;
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
                if (!Reachability.CanReach(pawn, t, PathEndMode.Touch)) continue;
                best = t;
                bestDistSq = distSq;
            }
            return best;
        }
    }
}
