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
    /// Finds a downed, hungry humanlike of the doctor's own faction — never a prisoner, see below — and
    /// carries the nearest reachable food to them (RimWorld: <c>RimWorld.WorkGiver_FeedPatient</c>).
    /// <para/>
    /// <b>Collapsed onto one mechanism, not copied:</b> this reuses <see cref="JobDefOf.FeedPatient"/> and
    /// <see cref="JobDriver_Warden_Feed"/> completely unchanged — that driver already carries food to whatever
    /// Pawn target B names and feeds them, with nothing prisoner-specific inside it at all (only
    /// <see cref="WorkGiver_Warden_Feed"/>'s own <em>scan</em> is prisoner-only). This class is that same
    /// mechanism's other target filter, not a second copy of it: it scans every spawned pawn for one that is
    /// downed, hungry, and of the doctor's own faction, and explicitly excludes anyone
    /// <see cref="CaptureUtility.FindHostFaction"/> says is a prisoner — a downed, hungry prisoner is already
    /// <see cref="WorkGiver_Warden_Feed"/>'s job, and letting both givers match the same patient would only
    /// race two doctors for one reservation, never cover a case neither already does.
    /// <para/>
    /// <b>Why <c>WardenDeliverFood</c> (RimWorld's other feeding <c>WorkGiverDef</c>) is not a third copy of
    /// this shape:</b> real RimWorld's <c>WardenDeliverFood</c> exists for a prisoner capable of walking to
    /// their own food but with none reachable inside their own cell/room — a distinction that needs a
    /// room/prison-cell system this port does not have. Every prisoner this port can even represent is either
    /// downed (this giver's own sibling, <see cref="WorkGiver_Warden_Feed"/>, already covers "downed and
    /// hungry") or ambulatory, in which case it already reaches food entirely on its own through the ordinary
    /// <see cref="JobGiver_GetFood"/> tier — nothing in this port's job selection checks guest status or
    /// faction at all. So <c>WardenDeliverFood</c> has no distinct case left to fill and stays the
    /// <c>WorkGiver_Pending</c> placeholder its content shipped with — see this module's report.
    /// </summary>
    public sealed class WorkGiver_FeedPatient : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++) yield return pawns[i];
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn patient) || ReferenceEquals(patient, pawn)) return false;
            if (!patient.RaceProps.Humanlike) return false;
            if (patient.Dead || !patient.Downed || !patient.Spawned || patient.Map != pawn.Map) return false;
            if (patient.needs.food == null || patient.needs.food.CurCategory == HungerCategory.Fed) return false;
            if (pawn.faction == null || !ReferenceEquals(patient.faction, pawn.faction)) return false;
            if (CaptureUtility.FindHostFaction(patient) != null) return false; // WardenFeed's own patient
            if (FindFoodFor(pawn) == null) return false;
            if (!Reachability.CanReach(pawn, patient, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, patient);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Thing? food = FindFoodFor(pawn);
            return food != null ? new Job(JobDefOf.FeedPatient, food, thing) : null;
        }

        /// <summary>Nearest reachable, unclaimed nutrition-giving item on the doctor's map, if any — the same
        /// trim of <c>FoodUtility.TryFindBestFoodSourceFor</c> <see cref="WorkGiver_Warden_Feed"/>'s own
        /// identical helper already uses. Duplicated rather than shared: that file belongs to a different
        /// lane's already-landed <c>WardenFeed</c> WorkGiverDef, not one of this lane's five (see this
        /// module's report).</summary>
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
