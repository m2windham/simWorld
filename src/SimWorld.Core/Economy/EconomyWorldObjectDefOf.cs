using SimWorld.Defs;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// The <see cref="WorldObjectDef"/> a visiting trade caravan is placed as.
    ///
    /// <para/>In its own class rather than appended to <c>World/WorldDefOf.cs</c>'s
    /// <see cref="WorldObjectDefOf"/>, per CLAUDE.md's "add a file rather than edit a shared one":
    /// <c>DefOfHelper</c> binds by scanning every <c>[DefOf]</c> type, so a new binding costs a new file and
    /// nothing else, and a new file cannot lose a merge the way an edit to a contested one silently can.
    /// </summary>
    [DefOf]
    public static class EconomyWorldObjectDefOf
    {
        /// <summary>See <see cref="TraderCaravan"/>. Distinct from <see cref="WorldObjectDefOf.Caravan"/>,
        /// which is <c>Caravans.Caravan</c> — a band of pawns walking a path — for the reasons
        /// <see cref="TraderCaravan"/>'s own doc gives.</summary>
        public static WorldObjectDef TradeCaravan = null!;
    }
}
