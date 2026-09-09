using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

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
    /// <c>RimWorld.ThingDefCountRangeClass</c>). Read by <see cref="StockGenerator_MultiDef"/> — see that
    /// class's own doc for why each entry's <see cref="count"/> is rolled per-generation rather than fixed.
    /// </summary>
    public class ThingDefCountRange
    {
        public ThingDef? def;
        public IntRange count;
    }

    /// <summary>
    /// A kind of trader — what they buy/sell in and how likely they are to show up (RimWorld:
    /// <c>RimWorld.TraderKindDef</c>). Per-trader pawn-kind rosters (RimWorld traders that are themselves a
    /// caravan of pawns to fight/flee) are out of scope: this port's trade model only ever needs
    /// <em>goods</em> to move, not the pawns carrying them (see <see cref="StockGenerator"/>'s own doc for
    /// why). Faction eligibility lives on <see cref="Factions.FactionDef.caravanTraderKinds"/> (RimWorld's
    /// own field of the same name), not here.
    /// </summary>
    public class TraderKindDef : Def
    {
        /// <summary>Relative weight this trader kind is picked among eligible kinds (RimWorld:
        /// <c>TraderKindDef.commonality</c>) — consumed by <see cref="Factions.FactionDef.RandomTraderKind"/>
        /// the same way <see cref="Factions.PawnGenOption.selectionWeight"/> feeds squad selection.</summary>
        public float commonality = 1f;

        /// <summary>Reached by sending a caravan up rather than one visiting the map (RimWorld's orbital traders). Flavor/gating flag only so far.</summary>
        public bool orbital;

        /// <summary>Can be asked to visit via a comms request. Flavor/gating flag only so far.</summary>
        public bool requestable = true;

        public TradeCurrency tradeCurrency = TradeCurrency.Silver;

        /// <summary>What this trader kind's stock is built from (RimWorld: <c>TraderKindDef.stockGenerators</c>); see <see cref="StockGenerator"/>.</summary>
        public List<StockGenerator>? stockGenerators;

        /// <summary>
        /// Rolls this trader kind's stock for one arrival by running every entry of
        /// <see cref="stockGenerators"/> through <see cref="RandomStream"/> <paramref name="rand"/> and
        /// concatenating the results (RimWorld: <c>TraderKindDef.GenerateStockFor</c>). Empty, never null,
        /// when this def has no generators.
        /// </summary>
        public List<(ThingDef def, int count)> GenerateStock(RandomStream rand)
        {
            var result = new List<(ThingDef, int)>();
            if (stockGenerators == null) return result;
            foreach (StockGenerator generator in stockGenerators)
            {
                result.AddRange(generator.Generate(rand));
            }
            return result;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (stockGenerators != null)
            {
                foreach (StockGenerator generator in stockGenerators)
                {
                    foreach (string error in generator.ConfigErrors()) yield return error;
                }
            }
        }
    }
}
