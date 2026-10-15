using System;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>
    /// Finding a cell that means something to the game (RimWorld: <c>RimWorld.RCellFinder</c>), as opposed to
    /// <see cref="CellFinder"/>'s cells that merely satisfy a test. One method so far: where a group walks onto
    /// a map from.
    /// </summary>
    public static class RCellFinder
    {
        /// <summary>
        /// A random map-edge cell a pawn can arrive on and then get to the colony from (RimWorld:
        /// <c>RCellFinder.TryFindRandomPawnEntryCell</c>): standable, unroofed, and joined to the colony
        /// (<see cref="Reachability.CanReachColony"/>), plus whatever <paramref name="extraValidator"/> adds.
        /// This is the test the port's raid arrival did not apply — it took any cell of any edge — and the reason
        /// a squad was once spawned on rock.
        /// <para/><b>Two of RimWorld's clauses are not here, and neither is a loss.</b> The cell's room touching
        /// the map edge is true of every edge cell by construction. Fog is not modelled on this port's maps, so
        /// <c>allowFogged</c> has nothing to allow.
        /// </summary>
        public static bool TryFindRandomPawnEntryCell(
            out IntVec3 result, Map.Map map, Faction? colonyFaction, RandomStream rand, Predicate<IntVec3>? extraValidator = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Map.Map m = map;
            return CellFinder.TryFindRandomEdgeCellWith(
                c => GenGrid.Standable(c, m)
                    && !m.roofGrid.Roofed(c)
                    && m.reachability.CanReachColony(c, colonyFaction)
                    && (extraValidator == null || extraValidator(c)),
                m, rand, out result);
        }
    }
}
