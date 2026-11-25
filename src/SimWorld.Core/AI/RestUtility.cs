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
        /// <para/>
        /// <b>A bed out of the cold first</b> (exposure4 lane). RimWorld searches twice, first for a bed whose room is
        /// no danger to the sleeper and only then for any (<c>for (int k = 0; k &lt; 2; k++) maxDanger = k != 0 ?
        /// Danger.Deadly : Danger.None</c>, the cell's <c>GetDangerFor</c> read off <c>Region.DangerFor</c>: none while
        /// the room's temperature is inside the sleeper's <c>SafeTemperatureRange</c>; B18 decompile, read). This port
        /// had only the second search, so a child in one shirt went to sleep in a dormitory at -3 C beside nothing
        /// warmer. See <see cref="IsSafeTemperatureBed"/> for what "no danger" means here.
        /// </summary>
        public static Thing? FindBedFor(Pawn sleeper, Pawn traveler) =>
            FindBedFor(sleeper, traveler, safeOnly: true) ?? FindBedFor(sleeper, traveler, safeOnly: false);

        /// <summary>One pass of <see cref="FindBedFor(Pawn, Pawn)"/>: with <paramref name="safeOnly"/>, only a bed that
        /// is <see cref="IsSafeTemperatureBed"/> for the sleeper (RimWorld's <c>Danger.None</c> pass).</summary>
        public static Thing? FindBedFor(Pawn sleeper, Pawn traveler, bool safeOnly)
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
                int distSq = (bed.Position - sleeper.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!map.reservationManager.CanReserve(traveler, bed)) continue;
                // A bed in a household's house is that household's (shelter lane): nobody else sleeps in it,
                // as a RimWorld pawn does not take a bed another colonist owns.
                Dwelling? home = DwellingUtility.DwellingOfBed(bed);
                if (home != null && home.householdId != family) continue;
                if (OccupiedByAnother(bed, sleeper)) continue;
                if (safeOnly && !IsSafeTemperatureBed(sleeper, bed)) continue;
                if (!Reachability.CanReach(traveler, bed, PathEndMode.OnCell)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>
        /// Whether <paramref name="bed"/> is no danger to <paramref name="sleeper"/> from the air (RimWorld:
        /// <c>Region.DangerFor</c> is <c>Danger.None</c> while the room's temperature is inside the pawn's
        /// <c>SafeTemperatureRange</c>, its comfortable range widened by 10 C each side; B18 decompile, read). Also
        /// safe: a bed too cold now in a room this settlement heats for whoever lies in it
        /// (<see cref="SettlementClimate.WillWarm"/>), since its hearth is lit for them once they are in it. A room's
        /// temperature is read as the air at the bed's sleeping slot, which in this port is the room's.
        /// </summary>
        public static bool IsSafeTemperatureBed(Pawn sleeper, Thing bed)
        {
            Map.Map? map = bed.Map;
            if (map == null) return false;
            float air = RotUtility.AmbientTemperatureAt(map, bed.GetSleepingSlotPos());
            FloatRange safe = Health.TemperatureInjuryUtility.SafeTemperatureRange(sleeper);
            if (safe.Includes(air)) return true;
            return air < safe.min && SettlementClimate.WillWarm(map, bed);
        }

        /// <summary>
        /// Where <paramref name="sleeper"/> lies down, carried or walking (<paramref name="traveler"/>): the
        /// household's own bed while it is safe from the cold, else a bed out of the cold anywhere it may sleep, else
        /// the household's bed anyway, else the nearest. RimWorld takes a pawn's owned bed whatever its room
        /// (<c>RestUtility.FindBedFor</c> checks only <c>IsValidBedFor</c> on it). Translation (exposure4 lane): a
        /// household's house is its owned bed here, and a house a quake has opened stands at the frost for days; a
        /// RimWorld player whose bedroom wall fell would sleep the colonist elsewhere, so the settlement does. With
        /// nowhere safer, home is still home.
        /// </summary>
        public static Thing? FindRestBedFor(Pawn sleeper, Pawn traveler)
        {
            Thing? home = DwellingUtility.FindDwellingBedFor(sleeper, traveler);
            if (home != null && IsSafeTemperatureBed(sleeper, home)) return home;
            return FindBedFor(sleeper, traveler, safeOnly: true) ?? home ?? FindBedFor(sleeper, traveler, safeOnly: false);
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
