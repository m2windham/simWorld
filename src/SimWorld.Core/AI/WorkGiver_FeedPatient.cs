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
    /// this shape:</b> it is not a feeding giver at all. Real RimWorld's <c>WardenDeliverFood</c> exists for a
    /// prisoner capable of walking to their own food but with none reachable inside their own room, and it
    /// <i>delivers</i> — puts a meal down for the prisoner to pick up — rather than putting nutrition in.
    /// This class's earlier remark that it "has no distinct case left to fill" was written when this port had
    /// no room system to draw that distinction with; <see cref="Building.RoomTracker"/> has since landed and
    /// <see cref="WorkGiver_Warden_DeliverFood"/> is now wired on top of it. It still does not overlap this
    /// giver by so much as one patient — this one excludes prisoners outright, and that is all it takes.
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
