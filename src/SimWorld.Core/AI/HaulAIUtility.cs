using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Shared hauling logic between <see cref="WorkGiver_Haul"/> and <see cref="JobDriver_HaulToCell"/>
    /// (RimWorld: <c>RimWorld.HaulAIUtility</c> plus the pieces of <c>RimWorld.StoreUtility</c> a single
    /// storage kind still needs).
    /// <para/>
    /// <b>What counts as haulable:</b> exactly <see cref="ThingDef.EverHaulable"/> — any spawned
    /// Item-category Thing. RimWorld draws a further <c>alwaysHaulable</c>/<c>EverHaulable</c> distinction
    /// (a weapon someone is holding, or gear worn, is <c>EverHaulable</c> but not haulable <i>right now</i>);
    /// this port has no equipment/apparel tracker for an item to be "held" by instead of spawned loose on
    /// the map, so that distinction has nothing to bite on yet — every spawned Item is a candidate whenever
    /// it is not already resting somewhere valid (<see cref="IsInValidStorage"/>).
    /// <para/>
    /// <b>Where things go:</b> only a <see cref="Zone_Stockpile"/> cell whose <see cref="Crafting.ThingFilter"/>
    /// allows the item's def and that has room for at least one unit of it. RimWorld's storage system also
    /// covers shelves/containers and ranks several candidate stockpiles by priority; neither exists in this
    /// port (one kind of storage, no priority tiers), so <see cref="TryFindBestStockpileCell"/> returning
    /// false — no stockpile, or every matching one already full — is a genuine, honest answer: it means
    /// there is nowhere for this item to go, not a bug to work around with an invented dumping cell.
    /// </summary>
    public static class HaulAIUtility
    {
        /// <summary>True when <paramref name="thing"/> already sits on a cell of a <see cref="Zone_Stockpile"/>
        /// whose filter allows it — already stored, nothing to haul (RimWorld: <c>StoreUtility.IsInValidBestStorage</c>,
        /// trimmed to this port's single storage kind).</summary>
        public static bool IsInValidStorage(Thing thing)
        {
            Map.Map? map = thing.Map;
            if (map == null) return false;
            return map.zoneManager.ZoneAt(thing.Position) is Zone_Stockpile stockpile && stockpile.filter.Allows(thing.def);
        }

        /// <summary>The one Item-category Thing already sitting on <paramref name="cell"/>, if any. A
        /// stockpile cell this port hauls into holds at most one distinct item stack at a time.</summary>
        public static Thing? ExistingStackAt(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> occupants = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i].def.category == ThingCategory.Item) return occupants[i];
            }
            return null;
        }

        /// <summary>How many more units of <paramref name="def"/> <paramref name="cell"/> could hold right now:
        /// the full stack limit if the cell holds no item, the remaining headroom if it already holds a stack
        /// of the same def, or zero if it holds a different item.</summary>
        public static int CapacityAt(Map.Map map, IntVec3 cell, ThingDef def)
        {
            Thing? existing = ExistingStackAt(map, cell);
            if (existing == null) return def.stackLimit;
            return existing.def == def ? def.stackLimit - existing.stackCount : 0;
        }

        /// <summary>
        /// Nearest reachable, reservable stockpile cell with room for at least one unit of <paramref name="thing"/>'s
        /// def — nearest to <paramref name="thing"/> itself, not to <paramref name="pawn"/> (RimWorld's own
        /// <c>StoreUtility</c> minimises the haul distance, not the pawn's detour to fetch it). False (with
        /// <paramref name="cell"/> left <see cref="IntVec3.Invalid"/>) when nothing qualifies — see this
        /// class's own remarks on why that is a legitimate outcome, not a search failure to retry.
        /// </summary>
        public static bool TryFindBestStockpileCell(Pawn pawn, Thing thing, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Map.Map? map = pawn.Map;
            if (map == null) return false;

            IntVec3 best = IntVec3.Invalid;
            int bestDistSq = int.MaxValue;
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!(zones[i] is Zone_Stockpile stockpile) || !stockpile.filter.Allows(thing.def)) continue;
                IReadOnlyList<IntVec3> cells = stockpile.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    IntVec3 candidate = cells[c];
                    if (CapacityAt(map, candidate, thing.def) <= 0) continue;
                    if (!GenGrid.Standable(candidate, map)) continue;
                    if (!map.reservationManager.CanReserve(pawn, candidate)) continue;
                    if (!Reachability.CanReach(pawn, candidate, PathEndMode.OnCell)) continue;

                    int distSq = (candidate - thing.Position).LengthHorizontalSquared;
                    if (distSq >= bestDistSq) continue;
                    best = candidate;
                    bestDistSq = distSq;
                }
            }
            cell = best;
            return best.IsValid;
        }
    }
}
