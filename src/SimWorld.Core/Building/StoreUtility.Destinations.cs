using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Building
{
    public static partial class StoreUtility
    {
        /// <summary>
        /// The best place <paramref name="carrier"/> could take <paramref name="t"/> that is strictly better
        /// stored than <paramref name="currentPriority"/> (RimWorld: <c>StoreUtility.TryFindBestBetterStorageFor</c>):
        /// a stockpile cell in <paramref name="foundCell"/>, or an <see cref="IHaulDestination"/> in
        /// <paramref name="haulDestination"/> with the cell left invalid. <b>The higher priority wins; a
        /// destination wins a tie with no stockpile and loses one it does not beat</b> — RimWorld's own order,
        /// which is why a grave (Important) is chosen over a dumping stockpile (Normal) for a body both accept.
        /// </summary>
        public static bool TryFindBestBetterStorageFor(Thing t, Pawn carrier, StoragePriority currentPriority,
            out IntVec3 foundCell, out IHaulDestination? haulDestination)
        {
            foundCell = IntVec3.Invalid;
            haulDestination = null;

            StoragePriority cellPriority = StoragePriority.Unstored;
            if (StockpilePriority > currentPriority && HaulAIUtility.TryFindBestStockpileCell(carrier, t, out IntVec3 cell))
            {
                foundCell = cell;
                cellPriority = StockpilePriority;
            }

            bool haveDestination = TryFindBestBetterNonSlotGroupStorageFor(t, carrier, currentPriority, out IHaulDestination? best);

            if (cellPriority == StoragePriority.Unstored && !haveDestination)
            {
                foundCell = IntVec3.Invalid;
                return false;
            }
            if (haveDestination && (cellPriority == StoragePriority.Unstored || best!.GetStoreSettings().Priority > cellPriority))
            {
                foundCell = IntVec3.Invalid;
                haulDestination = best;
                return true;
            }
            return true;
        }

        /// <summary>
        /// The highest-priority destination, nearest first among equals, that accepts <paramref name="t"/>, is
        /// not claimed by someone else and can be reached, better stored than <paramref name="currentPriority"/>
        /// (RimWorld: <c>StoreUtility.TryFindBestBetterNonSlotGroupStorageFor</c>). Walks the map's
        /// <see cref="ThingRequestGroup.HaulDestination"/> list — graves, not things — so the cost is the number
        /// of them, never the number of things on the map. RimWorld's faction and forbidden tests are not
        /// asked: this port's Things carry no faction, and every destination on a settlement's map is its own.
        /// </summary>
        public static bool TryFindBestBetterNonSlotGroupStorageFor(Thing t, Pawn carrier, StoragePriority currentPriority,
            out IHaulDestination? haulDestination)
        {
            haulDestination = null;
            Map.Map? map = carrier.Map;
            if (map == null) return false;

            IntVec3 from = t.Spawned ? t.Position : carrier.Position;
            StoragePriority bestPriority = StoragePriority.Unstored;
            int bestDistSq = int.MaxValue;

            IReadOnlyList<Thing> candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulDestination);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!(candidates[i] is IHaulDestination destination)) continue;
                StoragePriority priority = destination.GetStoreSettings().Priority;
                if (priority <= currentPriority || priority < bestPriority) continue;

                int distSq = (from - candidates[i].Position).LengthHorizontalSquared;
                if (priority == bestPriority && distSq >= bestDistSq) continue;

                if (!destination.Accepts(t)) continue;
                if (!map.reservationManager.CanReserve(carrier, candidates[i])) continue;
                if (!Reachability.CanReach(carrier, candidates[i], PathEndMode.Touch)) continue;

                haulDestination = destination;
                bestPriority = priority;
                bestDistSq = distSq;
            }
            return haulDestination != null;
        }
    }
}
