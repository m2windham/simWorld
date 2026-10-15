using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// "Can anybody in the colony get to this cell" (RimWorld: <c>Verse.Reachability.CanReachColony</c> and
    /// <c>CanReachBiggestMapEdgeRoom</c>) — the test an arrival's entry cell has to pass
    /// (<see cref="RCellFinder.TryFindRandomPawnEntryCell"/>). Its own file, as the other halves of the AI
    /// module's partial classes are, so adding it edited nothing three other lanes are in.
    /// </summary>
    public sealed partial class Reachability
    {
        /// <summary>
        /// Whether <paramref name="c"/> is walkable and joined to the colony (RimWorld:
        /// <c>Reachability.CanReachFactionBase</c> with the player's faction), by the first of these to hold:
        /// <list type="number">
        /// <item>a pawn of <paramref name="colonyFaction"/> spawned on this map that can move can walk to it;</item>
        /// <item><see cref="CanReachBiggestMapEdgeRoom"/>: it is joined to the largest open area that touches
        /// the map's edge, which is what keeps a raid landing somewhere real on a map whose colonists are all
        /// down, dead or walled in.</item>
        /// </list>
        /// <b>Not ported: the middle leg.</b> RimWorld's second test asks whether any <i>colonist-owned
        /// building</i> can be touched from the cell. This port's buildings belong to nobody
        /// (<c>Duties_Assault.xml</c> says why), so there is no list of the colony's buildings to ask; on a map
        /// where a colonist is alive the first leg answers the same question, and on one where none is the third
        /// does.
        /// <para/>Passing <c>null</c> means the player's faction, RimWorld's <c>map.ParentFaction ?? Faction.OfPlayer</c>
        /// with the map's parent faction left out because this port's maps do not carry one — a caller aiming a
        /// raid at somebody else's settlement passes that settlement's faction.
        /// </summary>
        public bool CanReachColony(IntVec3 c, Faction? colonyFaction = null)
        {
            if (!GenGrid.InBounds(c, map) || !GenGrid.Walkable(c, map)) return false;

            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region? from = map.regionGrid.RegionAt(c);
            if (from == null) return false;

            Faction? colony = colonyFaction ?? Find.FactionManager?.OfPlayer;
            if (colony != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                HashSet<Region>? asked = null;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (!ReferenceEquals(pawn.faction, colony) || pawn.Dead || !pawn.Spawned) continue;
                    if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) continue;

                    // Two colonists in one region give one answer; a cell that nobody can reach would
                    // otherwise cost a search of the whole connected area per colonist.
                    Region? at = map.regionGrid.RegionAt(pawn.Position);
                    if (at == null) continue;
                    asked ??= new HashSet<Region>();
                    if (!asked.Add(at)) continue;
                    if (RegionTraverser.WithinRegions(from, at)) return true;
                }
            }

            return CanReachBiggestMapEdgeRoom(c);
        }

        /// <summary>
        /// Whether <paramref name="c"/> is joined to the largest connected open area that touches the map's edge
        /// (RimWorld: <c>Reachability.CanReachBiggestMapEdgeRoom</c>). RimWorld measures a <c>Room</c> by its
        /// regions; this port's regions are not grouped into rooms (<see cref="Region"/> says why), so the unit
        /// here is the connected area itself — the same set once a doorway is passable, which this port's
        /// regions already treat it as. The first of equally large areas wins, in region order, as RimWorld's
        /// first room does.
        /// </summary>
        public bool CanReachBiggestMapEdgeRoom(IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return false;
            map.regionAndRoomUpdater.RebuildIfNeeded();
            Region? from = map.regionGrid.RegionAt(c);
            if (from == null) return false;

            Region? biggest = BiggestMapEdgeArea();
            return biggest != null && RegionTraverser.WithinRegions(from, biggest);
        }

        /// <summary>One region of the largest connected area that touches the map's edge, or null if no
        /// region does. A walk of every region, so it is asked only after the cheap question has failed.</summary>
        private Region? BiggestMapEdgeArea()
        {
            IReadOnlyList<Region> all = map.regionGrid.AllRegions;
            var seen = new HashSet<Region>();
            var queue = new Queue<Region>();
            Region? best = null;
            int bestCount = 0;

            for (int i = 0; i < all.Count; i++)
            {
                Region start = all[i];
                if ((start.type & RegionType.Set_Passable) == 0 || !seen.Add(start)) continue;

                int count = 0;
                bool touchesEdge = false;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Region cur = queue.Dequeue();
                    count++;
                    if (!touchesEdge && TouchesMapEdge(cur)) touchesEdge = true;
                    IReadOnlyList<RegionLink> links = cur.Links;
                    for (int l = 0; l < links.Count; l++)
                    {
                        Region? other = links[l].GetOtherRegion(cur);
                        if (other == null || (other.type & RegionType.Set_Passable) == 0 || !seen.Add(other)) continue;
                        queue.Enqueue(other);
                    }
                }

                if (touchesEdge && count > bestCount)
                {
                    best = start;
                    bestCount = count;
                }
            }
            return best;
        }

        private bool TouchesMapEdge(Region region)
        {
            IReadOnlyList<IntVec3> cells = region.Cells;
            int maxX = map.Size.x - 1;
            int maxZ = map.Size.z - 1;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (c.x == 0 || c.z == 0 || c.x == maxX || c.z == maxZ) return true;
            }
            return false;
        }
    }
}
