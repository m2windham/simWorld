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
        /// <summary>Nearest reachable, unclaimed spawned <see cref="ConstructionThingDefOf.Bed"/> on
        /// <paramref name="pawn"/>'s own map, or null when it has no map or no bed qualifies — the caller
        /// falls back to sleeping on the ground exactly as before this method existed.</summary>
        public static Thing? FindBedFor(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return null;

            Thing? best = null;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                Thing bed = beds[i];
                if (!bed.Spawned) continue;
                if (!map.reservationManager.CanReserve(pawn, bed)) continue;
                int distSq = (bed.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq >= bestDistSq) continue;
                if (!Reachability.CanReach(pawn, bed, PathEndMode.OnCell)) continue;
                best = bed;
                bestDistSq = distSq;
            }
            return best;
        }
    }
}
