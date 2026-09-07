using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.Economy
{
    /// <summary>Anything the player can open a trade session with (RimWorld: <c>RimWorld.ITrader</c>). Implemented by <see cref="SettlementTrader"/>; a caravan-side trader lands with the Things/AI systems.</summary>
    public interface ITrader
    {
        TraderKindDef TraderKind { get; }

        /// <summary>Goods this trader currently offers, and how many of each.</summary>
        IEnumerable<(ThingDef def, int count)> Goods { get; }

        Faction? Faction { get; }
    }
}
