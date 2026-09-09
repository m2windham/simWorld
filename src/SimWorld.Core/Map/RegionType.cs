using System;

namespace SimWorld.Map
{
    /// <summary>
    /// What kind of <see cref="Region"/> a region is (RimWorld: <c>Verse.RegionType</c>).
    /// <b>Trim</b>: RimWorld also has <c>ImpassableFreeAirExchange</c> and <c>Fence</c> variants, used by its
    /// airflow/gas and fence-gate mechanics respectively — neither exists in this port yet, so only the two
    /// variants every region actually needs today are ported. Add the rest alongside whichever module first
    /// needs them rather than carrying dead cases here.
    /// </summary>
    [Flags]
    public enum RegionType : byte
    {
        None = 0,

        /// <summary>An ordinary walkable area, flood-filled by <see cref="RegionMaker"/> across cells that
        /// share passability and are not a doorway.</summary>
        Normal = 1,

        /// <summary>A single doorway cell (RimWorld: a region centred on a <c>Building_Door</c>). Kept
        /// separate from the <see cref="Normal"/> regions on either side so a door's own state can change
        /// (this port's <see cref="Building.Door"/> does not yet model open/closed — see its own remarks)
        /// without forcing the rooms it connects to merge or split.</summary>
        Portal = 2,

        /// <summary>Every region type <see cref="RegionTraverser"/> is willing to path/BFS through by
        /// default (RimWorld: <c>Verse.RegionType.Set_Passable</c>).</summary>
        Set_Passable = Normal | Portal,
    }
}
