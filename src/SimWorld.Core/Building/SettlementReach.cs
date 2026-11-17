using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>
    /// Whether the band that owns a map could walk to a cell: the one rule every planner that sites
    /// something for a settlement (compound, blueprint draws, works, fields) shares.
    /// <para/>
    /// <b>Why.</b> A RimWorld player only designates what their colonists can reach, and sees at a glance
    /// when they cannot. The settlement plans for itself, and planned on walkability alone: on seed 2024
    /// (tile 284, AridShrubland, 46 sealed rock pockets) the band stood in one pocket while the compound,
    /// its fields and every frame were drawn in another, so 77 frames were never worked
    /// (<c>docs/perf/storyteller-pressure/stall-2024</c>). Asking region connectivity
    /// (<see cref="Reachability.CanReachCells"/>) from where a citizen stands keeps every plan on the
    /// band's own ground.
    /// <para/>
    /// A map no settlement owns, or one none of its citizens stands on yet, is not gated: there is nobody
    /// to measure from, and the plan is made again once somebody arrives.
    /// </summary>
    public static class SettlementReach
    {
        /// <summary>Where a citizen of the settlement owning <paramref name="map"/> stands on it, or null.</summary>
        public static IntVec3? BandCell(Map.Map map)
        {
            Settlement? settlement = HuntingInitiative.SettlementFor(map);
            if (settlement == null) return null;
            foreach (Pawn citizen in settlement.Citizens)
            {
                if (citizen.Spawned && citizen.Map == map) return citizen.Position;
            }
            return null;
        }

        /// <summary>Whether a walker on <paramref name="from"/> could reach <paramref name="cell"/>; true when
        /// there is no <paramref name="from"/>.</summary>
        public static bool CanReach(Map.Map map, IntVec3? from, IntVec3 cell) =>
            from == null || map.reachability.CanReachCells(from.Value, cell);
    }
}
