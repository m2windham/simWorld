using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Bringing a baby to safety from the cold (RimWorld, Biotech: <c>ChildcareUtility</c> and
    /// <c>WorkGiver_BringBabyToSafety</c>, recalled, not read; the RimWorld wiki's Baby and Baby sleeping spot pages,
    /// read for the exposure3 lane: "any colonist (even those incapable of childcare or hauling) can try to bring the
    /// baby somewhere safe, like an available crib", and "If a crib's room is too hot or cold, colonists will try and
    /// place the baby in a safe area"). A baby cannot walk to warmth (<see cref="LifeStageDef.alwaysDowned"/>), so
    /// somebody has to carry it there; <see cref="WorkGiver_RescueDowned"/> is the carry, and this says when and to
    /// which bed.
    /// <para/>
    /// <b>Why.</b> The 600-day arc on 40/201 lost its babies to exposure in one way: a household still waiting for
    /// a house had a baby, the baby lay in the compound's dormitory, which no hearth ever heats, at a few degrees
    /// above freezing, and nobody moved it for three days, because the rescue only ever carries a patient home and it
    /// had no home. RimWorld's player would never leave a baby in an unheated barracks; here the settlement moves it.
    /// <para/>
    /// <b>Translation.</b> RimWorld looks for a crib or region whose temperature is safe for the baby. This port has
    /// beds rather than cribs and decides heat by the people in a house (<see cref="SettlementClimate"/>), so a bed is
    /// safe when the air at it is already comfortable for the baby, or when it stands in a finished house whose hearth
    /// has fuel to burn: the hearth answers whoever lies in that house, the guest baby too, so the room will be warmed
    /// for it. Its own household's house comes first, as it always has; another household's spare bed is the
    /// fallback, as a RimWorld colonist lays a baby in whichever crib is safe.
    /// </summary>
    public static class BabySafetyUtility
    {
        /// <summary>A baby lying, in a bed or on the ground, where it is not safe from the cold: the air is outside its
        /// comfortable range, and it is not in a bed a lit hearth will warm.</summary>
        public static bool NeedsMovingForTemperature(Pawn baby)
        {
            if (!IsBabyOnMap(baby)) return false;
            return !IsSafeFor(baby, baby.CurrentBed(), baby.Position);
        }

        /// <summary>A baby (down by its life stage, so it moves only when carried) spawned on a map.</summary>
        public static bool IsBabyOnMap(Pawn pawn) =>
            pawn.Spawned && pawn.Map != null && pawn.health != null && pawn.health.LifeStageForcesDowned;

        /// <summary>Whether <paramref name="bed"/> is safe for <paramref name="baby"/> to be laid in now.</summary>
        public static bool IsSafeBedFor(Pawn baby, Thing bed) => IsSafeFor(baby, bed, bed.GetSleepingSlotPos());

        /// <summary>The nearest bed (from the baby) that is safe for <paramref name="baby"/>, nobody else lies in, and
        /// <paramref name="traveler"/> can reserve and reach; null when there is none. Never the bed it lies in.</summary>
        public static Thing? SafeBedFor(Pawn baby, Pawn traveler)
        {
            Map.Map? map = baby.Map;
            if (map == null || traveler.Map != map) return null;
            Thing? current = baby.CurrentBed();
            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                Thing bed = beds[i];
                if (!bed.Spawned || ReferenceEquals(bed, current) || bed.IsBurning()) continue;
                int distSq = (bed.Position - baby.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!IsSafeFor(baby, bed, bed.GetSleepingSlotPos())) continue;
                if (!map.reservationManager.CanReserve(traveler, bed)) continue;
                if (RestUtility.OccupiedByAnother(bed, baby)) continue;
                if (!Reachability.CanReach(traveler, bed, PathEndMode.OnCell)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }

        private static bool IsSafeFor(Pawn baby, Thing? bed, IntVec3 cell)
        {
            Map.Map map = baby.Map!;
            FloatRange comfy = TemperatureInjuryUtility.ComfortableTemperatureRange(baby);
            float air = RotUtility.AmbientTemperatureAt(map, cell);
            if (comfy.Includes(air)) return true;
            if (air > comfy.max || bed == null) return false;
            return SettlementClimate.WillWarm(map, bed);
        }
    }
}
