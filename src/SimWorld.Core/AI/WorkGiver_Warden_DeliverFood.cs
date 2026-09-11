using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Carries a meal to a hungry prisoner who can still feed themselves and puts it down where they are
    /// (RimWorld: <c>RimWorld.WorkGiver_Warden_DeliverFood</c>, driver <c>RimWorld.JobDriver_FoodDeliver</c> —
    /// here <see cref="JobDriver_FoodDeliver"/>). This is the last of the twenty-nine shipped
    /// <see cref="WorkGiverDef"/>s to get a <c>giverClass</c>, and it was left until now for a reason that has
    /// since stopped being true; that is worth stating plainly rather than quietly overwriting.
    ///
    /// <para/><b>What used to block it.</b> Two passes recorded the same objection, in
    /// <see cref="WorkGiver_Warden_Feed"/>'s doc, in <see cref="WorkGiver_FeedPatient"/>'s, in this def's own
    /// XML comment and in the spec: RimWorld's DeliverFood exists for a prisoner who <i>can</i> walk to food
    /// but has none reachable <i>in its own room</i>, and "this port has no room/prison-cell system" to draw
    /// that distinction with — so every prisoner this port could represent was either downed (already
    /// <see cref="WorkGiver_Warden_Feed"/>'s patient) or ambulatory and therefore already served by the
    /// ordinary <see cref="JobGiver_GetFood"/> tier of its own think tree. That reasoning was right about the
    /// mechanics and right about the gap. What it was waiting on is <see cref="Building.RoomTracker"/>, which
    /// has since landed: <see cref="RoomTracker.RoomAt"/> answers "which enclosed area is this cell in" for
    /// any cell on the map, which is exactly the primitive <c>FoodAvailableInRoomTo</c> needs. So the one
    /// screen that made DeliverFood a distinct giver upstream is now expressible here, and this is it.
    ///
    /// <para/><b>What this giver adds that <see cref="JobGiver_GetFood"/> does not.</b> Not "the prisoner
    /// would otherwise starve" — it would not; <see cref="JobGiver_GetFood"/> searches the whole map and this
    /// port confines nobody (a <see cref="Door"/>'s passability is <c>Standable</c>, see that class's own
    /// doc). What it adds is <i>where</i> the prisoner eats. Without it a hungry prisoner walks across the
    /// settlement to whatever stack is nearest — through the colony, into the kitchen — every time it gets
    /// hungry. With it a warden brings one meal into the room the prisoner is actually in, and from then on
    /// that room is the nearest food, so the prisoner eats where it is held. The steady state is one meal
    /// standing in each prisoner's room, restocked when it is eaten, which is precisely the loop RimWorld's
    /// own DeliverFood produces. The delivery and the prisoner's own trip can race, and when the prisoner
    /// wins the race the meal simply waits in the room for next time: nothing is consumed by delivering it.
    ///
    /// <para/><b>Rate: the room screen is what bounds it.</b> <see cref="FoodAvailableInRoomTo"/> is not a
    /// courtesy check, it is the whole self-limiting mechanism — a prisoner whose room already holds
    /// something edible produces no job at all, so this giver fires once per prisoner per emptied room, not
    /// once per hungry prisoner per scan.
    ///
    /// <para/><b>No target this giver reaches is anybody else's.</b> The three feeding givers partition
    /// cleanly, and none of the three shares a patient with another:
    /// <list type="bullet">
    /// <item><see cref="WorkGiver_Warden_Feed"/> takes prisoners that are <see cref="Pawn.Downed"/>; this one
    /// takes prisoners that are not. The same <see cref="Pawn.Downed"/> flag, read both ways round — the
    /// port's stand-in for RimWorld's own <c>WardenFeedUtility.ShouldBeFed</c> split, which that giver's doc
    /// already explains.</item>
    /// <item><see cref="WorkGiver_FeedPatient"/> (<c>DoctorFeedHumanlikes</c>) explicitly excludes anyone
    /// <see cref="CaptureUtility.FindHostFaction"/> calls a prisoner, so it never reaches this giver's
    /// patients at all.</item>
    /// </list>
    /// The one thing all three <i>do</i> compete for is the food itself, and that is settled by the
    /// <see cref="ReservationManager"/> rather than by hoping: every one of them picks its stack through a
    /// <c>FindFoodFor</c> that skips anything it cannot reserve, and every one of them claims that stack in
    /// its driver's first toil. A doctor cannot take the meal a warden is already carrying and a warden
    /// cannot take the doctor's, because the second of them to look does not see it as a candidate.
    ///
    /// <para/><b>Whole-stack claims, deliberately, even though partial ones exist.</b>
    /// <see cref="ReservationManager"/> can now split a stack between claimants
    /// (<see cref="Reservation.stackCount"/>), which would let a warden take one meal off a pile another job
    /// is also drawing from. This giver does not use that, and the reason is in the manager rather than in
    /// taste: two claims on one target are only ever compatible when they agree on <c>maxClaimants</c>
    /// (<see cref="ReservationManager.CanReserve"/> refuses outright on a mismatch), so a partial claim here
    /// would not interoperate with <see cref="JobDriver_Warden_Feed"/>'s whole-Thing claim — it would simply
    /// be refused by a different code path, with the same visible outcome and one more thing to explain.
    /// Sharing a pile between the feeding jobs means changing all three drivers together, which is a claim on
    /// files this module does not own; recorded here as a deliberate seam rather than half-done.
    /// </summary>
    public sealed class WorkGiver_Warden_DeliverFood : WorkGiver_Scanner
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
            if (pawn.faction == null || ReferenceEquals(prisoner, pawn)) return false;

            // Downed is WardenFeed's own patient: that giver puts the nutrition in directly, because someone
            // who cannot stand cannot pick a meal up off the floor.
            if (prisoner.Downed) return false;
            if (prisoner.needs.food == null || prisoner.needs.food.CurCategory == HungerCategory.Fed) return false;

            Faction? host = CaptureUtility.FindHostFaction(prisoner);
            if (host == null || !ReferenceEquals(host, pawn.faction)) return false;

            if (FoodAvailableInRoomTo(prisoner)) return false;
            if (FindFoodFor(pawn, prisoner) == null) return false;
            if (!Reachability.CanReach(pawn, prisoner, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, prisoner);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Pawn prisoner)) return null;
            Thing? food = FindFoodFor(pawn, prisoner);
            return food != null ? new Job(WardenDeliverFoodDefOf.DeliverFood, food, prisoner) : null;
        }

        /// <summary>
        /// True when something edible is already standing in the same <see cref="Room"/> as
        /// <paramref name="prisoner"/> (RimWorld: <c>WorkGiver_Warden_DeliverFood.FoodAvailableInRoomTo</c>).
        /// <b>Trimmed:</b> RimWorld totals the nutrition in the room against what every prisoner in it still
        /// needs, and counts what a warden is already carrying there; this asks only whether there is
        /// anything at all to eat in the room, which is the same question whenever the answer is "no" — and
        /// "no" is the only answer that produces a job.
        /// <para/>
        /// <b>A prisoner in no known room gets no delivery.</b> <see cref="RoomTracker"/> floods lazily, on a
        /// tick, so <see cref="RoomTracker.RoomAt"/> is null on a map that has never ticked. Reading that as
        /// "their room has no food" would make this giver fire on every hungry prisoner everywhere, which is
        /// exactly the unbounded behaviour the room screen exists to prevent — so an unknown room is read as
        /// "no room-scoped question to answer here" and the giver stands down. A real map ticks its room
        /// tracker every <c>Map.MapTick</c>, so this is a test-shaped edge, not a gameplay one.
        /// </summary>
        internal static bool FoodAvailableInRoomTo(Pawn prisoner)
        {
            Map.Map? map = prisoner.Map;
            if (map == null) return true;

            Room? room = map.roomTracker.RoomAt(prisoner.Position);
            if (room == null) return true;

            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.def.IsNutritionGivingIngestible) continue;
                if (ReferenceEquals(map.roomTracker.RoomAt(t.Position), room)) return true;
            }
            return false;
        }

        /// <summary>Nearest reachable, unclaimed nutrition-giving item that is <i>not</i> already in the
        /// prisoner's own room — the same trim of <c>FoodUtility.TryFindBestFoodSourceFor</c>
        /// <see cref="WorkGiver_Warden_Feed"/> and <see cref="WorkGiver_FeedPatient"/> both already use, with
        /// the one extra screen this giver's whole purpose rests on: carrying a meal from the prisoner's own
        /// room back into it would be a job that changes nothing.</summary>
        private static Thing? FindFoodFor(Pawn pawn, Pawn prisoner)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;
            Room? prisonerRoom = map.roomTracker.RoomAt(prisoner.Position);

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.def.IsNutritionGivingIngestible) continue;
                if (prisonerRoom != null && ReferenceEquals(map.roomTracker.RoomAt(t.Position), prisonerRoom)) continue;
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
