using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Economy
{
    /// <summary>
    /// One way a <see cref="TraderKindDef"/> fills its stock table (RimWorld: <c>RimWorld.StockGenerator</c>
    /// and its subclasses — <c>StockGenerator_SingleDef</c>, <c>StockGenerator_MultiDef</c>,
    /// <c>StockGenerator_Category</c>, <c>StockGenerator_BuySellableMapItems</c>, etc.). RimWorld's own
    /// generators build live <c>Thing</c> stacks and filter them against money thresholds
    /// (<c>minTradeMoney</c>/<c>maxTradeMoneyPerItem</c>/a distinct-item cap/etc.) against a whole live
    /// item/animal/pawn catalog. This port keeps the shape that matters — content picks a concrete
    /// generator by <c>Class=</c>, exactly like <see cref="Building.CompProperties"/> and
    /// <see cref="Factions.PawnGroupMaker"/>'s own options list — but drops the money-threshold filtering
    /// and yields plain (def, count) pairs rather than spawned Things: nothing in this trade model backs a
    /// trader's stock with real <c>Thing</c> stacks in the first place (see <see cref="ITrader.Goods"/> and
    /// <see cref="SettlementTrader.goods"/> — a settlement's own stock is a plain def→count store ledger,
    /// not a <c>ThingOwner</c>, per <c>World.Settlement</c>'s own design), so there is no live catalog here
    /// for a money filter to filter. A hand-authored <see cref="TraderKindDef"/> stock table already only
    /// lists what it means to sell, the same way RimWorld's <c>StockGenerator_MultiDef</c> table does.
    /// </summary>
    public abstract class StockGenerator
    {
        /// <summary>How many units to roll for a single def; meaning is per-subclass (see each one's own doc).</summary>
        public IntRange countRange = IntRange.One;

        /// <summary>What this generator adds to a trader's stock for one roll (RimWorld: <c>StockGenerator.GenerateThings</c>).</summary>
        public abstract IEnumerable<(ThingDef def, int count)> Generate(RandomStream rand);

        /// <summary>Content-load validation (RimWorld: <c>StockGenerator.ConfigErrors</c>); empty by default.</summary>
        public virtual IEnumerable<string> ConfigErrors()
        {
            yield break;
        }
    }

    /// <summary>
    /// One def at a count rolled from <see cref="StockGenerator.countRange"/> (RimWorld:
    /// <c>RimWorld.StockGenerator_SingleDef</c>).
    /// </summary>
    public sealed class StockGenerator_SingleDef : StockGenerator
    {
        public ThingDef? thingDef;

        public override IEnumerable<(ThingDef def, int count)> Generate(RandomStream rand)
        {
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (thingDef == null) yield break;
            int count = rand.Range(countRange);
            if (count > 0) yield return (thingDef, count);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (thingDef == null) yield return "StockGenerator_SingleDef has no thingDef.";
        }
    }

    /// <summary>
    /// A fixed table of defs, each independently rolled from its own <see cref="ThingDefCountRange.count"/>
    /// (RimWorld: <c>RimWorld.StockGenerator_MultiDef</c>, which generates every entry at a fixed count
    /// rather than a range — rolling each entry's own range here instead so a bulk-goods table's exact
    /// quantities vary between generations, matching how every other stock number in this game is a rolled
    /// range rather than a constant; this port's own translation of that one field).
    /// </summary>
    public sealed class StockGenerator_MultiDef : StockGenerator
    {
        public List<ThingDefCountRange>? thingDefs;

        public override IEnumerable<(ThingDef def, int count)> Generate(RandomStream rand)
        {
            if (rand == null) throw new ArgumentNullException(nameof(rand));
            if (thingDefs == null) yield break;
            foreach (ThingDefCountRange entry in thingDefs)
            {
                if (entry.def == null) continue;
                int count = rand.Range(entry.count);
                if (count > 0) yield return (entry.def, count);
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (thingDefs == null || thingDefs.Count == 0)
            {
                yield return "StockGenerator_MultiDef has no thingDefs.";
                yield break;
            }
            foreach (ThingDefCountRange entry in thingDefs)
            {
                if (entry.def == null) yield return "StockGenerator_MultiDef has an entry with no def.";
            }
        }
    }
}
