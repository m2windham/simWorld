using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Economy
{
    /// <summary>What a trader deals in (RimWorld: <c>RimWorld.TraderKindDef</c>'s <c>tradeCurrency</c>).</summary>
    public enum TradeCurrency
    {
        Silver,
        Favor,
    }

    /// <summary>
    /// One entry of a stock table: how many of a ThingDef a trader of this kind tends to carry (RimWorld:
    /// <c>RimWorld.StockGenerator</c>, simplified to a plain count range). Not yet consumed by anything —
    /// generating actual stock needs real Things, which land with the Things/Crafting systems; this is the
    /// data shape a future <c>StockGenerator</c> would read.
    /// </summary>
    public class ThingDefCountRange
    {
        public ThingDef? def;
        public IntRange count;
    }

    /// <summary>
    /// A kind of trader — what they buy/sell in and how likely they are to show up (RimWorld:
    /// <c>RimWorld.TraderKindDef</c>). Faction restrictions and per-trader pawn-kind rosters are out of
    /// scope: those need the Pawn Generation and Factions-caravan systems this port doesn't wire up yet.
    /// </summary>
    public class TraderKindDef : Def
    {
        /// <summary>Relative weight this trader kind is picked among eligible kinds. Not yet consumed — no trader-arrival selection system exists yet.</summary>
        public float commonality = 1f;

        /// <summary>Reached by sending a caravan up rather than one visiting the map (RimWorld's orbital traders). Flavor/gating flag only so far.</summary>
        public bool orbital;

        /// <summary>Can be asked to visit via a comms request. Flavor/gating flag only so far.</summary>
        public bool requestable = true;

        public TradeCurrency tradeCurrency = TradeCurrency.Silver;

        /// <summary>Placeholder stock table (see <see cref="ThingDefCountRange"/>) for a future stock-generation system.</summary>
        public List<ThingDefCountRange>? stockGenerators;
    }
}
