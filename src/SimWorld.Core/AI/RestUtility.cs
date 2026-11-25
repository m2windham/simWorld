using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Finds a bed for a pawn to sleep in (RimWorld: a trim of <c>RimWorld.RestUtility.FindBedFor</c> —
    /// nearest reachable, unclaimed <see cref="ConstructionThingDefOf.Bed"/> rather than RimWorld's fuller
    /// scored search across medical/prisoner/temperature-comfort beds and owned-bed preference, none of which
    /// exist in this port yet: every <see cref="Bed"/> this settlement has built is unowned and open to
    /// anyone, the same "first reachable, unclaimed candidate wins" trim <see cref="WorkGiver_Warden"/>'s own
    /// <c>FindFoodFor</c> already uses for the identical reason).
    /// </summary>
    public static class RestUtility
    {
        /// <summary>
        /// Whether <paramref name="pawn"/> is currently resting in a bed (RimWorld: <c>RestUtility.InBed</c>,
        /// which reads <c>Pawn.CurrentBed()</c> — spawned, has a job, <c>GetPosture() == LayingInBed</c>, and a
        /// <c>Building_Bed</c> stands at the pawn's own cell with that pawn in one of its sleeping slots; 1.0
        /// decompile, <c>RimWorld.RestUtility.CurrentBed</c>/<c>InBed</c>).
        /// <para/>
        /// <b>Trimmed the same way <see cref="FindBedFor"/> already is.</b> This port has no posture system and
        /// no per-slot bed-occupant tracking (every <see cref="ConstructionThingDefOf.Bed"/> is unowned and
        /// open, per <see cref="FindBedFor"/>'s own doc), and a downed patient carried here is not given a job
        /// at all — <see cref="JobDriver_TakeToBed"/> only ever moves <c>patient.Position</c> to the bed's
        /// <see cref="BedUtility.GetSleepingSlotPos(Things.Thing, int)"/> and stops. So "in bed" here means
        /// exactly the fact that placement leaves behind: a Bed Thing standing on the pawn's own cell. That is
        /// the one honest reading available without inventing occupant state this module does not own, and it
        /// is enough to answer the question every caller actually asks with it — has this patient already been
        /// carried to a bed.
        /// </summary>
        public static bool InBed(this Pawn pawn) => pawn.CurrentBed() != null;

        /// <summary>
        /// The actual <see cref="ConstructionThingDefOf.Bed"/> standing on <paramref name="pawn"/>'s own cell,
        /// or null (RimWorld: <c>Pawn.CurrentBed()</c>, 1.0 decompile — trimmed the same way <see cref="InBed"/>
        /// already is; see that method's own doc for what "in bed" means with no posture or per-slot occupant
        /// tracking). The Thing itself, not just the fact of it, is what <see cref="Stats.StatPart_BedStat"/>
        /// and <see cref="Health.TendUtility.CalculateBaseTendQuality"/> need — each bed carries its own
        /// stat values, so which bed answers "how much" as well as "yes".
        /// </summary>
        public static Thing? CurrentBed(this Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Map == null) return null;
            IReadOnlyList<Thing> here = pawn.Map.thingGrid.ThingsListAt(pawn.Position);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].def == ConstructionThingDefOf.Bed) return here[i];
            }
            return null;
        }

        /// <summary>Nearest reachable, unclaimed spawned <see cref="ConstructionThingDefOf.Bed"/> on
        /// <paramref name="pawn"/>'s own map, or null when it has no map or no bed qualifies — the caller
        /// falls back to sleeping on the ground exactly as before this method existed.</summary>
        public static Thing? FindBedFor(Pawn pawn) => FindBedFor(pawn, pawn);

        /// <summary>
        /// The bed for <paramref name="sleeper"/>, walked to by <paramref name="traveler"/> (RimWorld:
        /// <c>RestUtility.FindBedFor(Pawn sleeper, Pawn traveler, ...)</c>, the overload <c>WorkGiver_RescueDowned</c>
        /// calls with the patient as sleeper and the rescuer as traveler). Whose bed it may be is the sleeper's
        /// question: a household's bed is only its own household's. Whether it can be had is the traveler's: the
        /// traveler reserves it and must reach it. Distance is measured from the sleeper, where RimWorld's search
        /// starts. Before this overload a rescuer looked for a bed for itself, so a baby picked up by a neighbour
        /// was laid in the neighbour's house, whose hearth nobody lights for a baby.
        /// </summary>
        public static Thing? FindBedFor(Pawn sleeper, Pawn traveler)
        {
            Map.Map? map = sleeper.Map;
            if (map == null || traveler.Map != map) return null;

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            int family = sleeper.relations?.familyId ?? Pawns.Pawn_RelationsTracker.None;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                Thing bed = beds[i];
                if (!bed.Spawned) continue;
                // RimWorld: RestUtility.IsValidBedFor refuses a bed that is on fire (bedThing.IsBurning()).
                if (bed.IsBurning()) continue;
                if (!map.reservationManager.CanReserve(traveler, bed)) continue;
                // A bed in a household's house is that household's (shelter lane): nobody else sleeps in it,
                // as a RimWorld pawn does not take a bed another colonist owns.
                Dwelling? home = DwellingUtility.DwellingOfBed(bed);
                if (home != null && home.householdId != family) continue;
                if (OccupiedByAnother(bed, sleeper)) continue;
                int distSq = (bed.Position - sleeper.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!Reachability.CanReach(traveler, bed, PathEndMode.OnCell)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>
        /// Somebody other than <paramref name="sleeper"/> already lies in <paramref name="bed"/> (RimWorld:
        /// <c>RestUtility.IsValidBedFor</c> refuses a bed with no unoccupied sleeping slot). A sleeper holds the
        /// bed by reservation, but a patient or a baby laid there holds none, so it is read off the cell.
        /// </summary>
        public static bool OccupiedByAnother(Thing bed, Pawn sleeper)
        {
            Map.Map? map = bed.Map;
            if (map == null) return false;
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(bed.GetSleepingSlotPos());
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Pawn p && !ReferenceEquals(p, sleeper) && p.Downed) return true;
            }
            return false;
        }
    }
}
