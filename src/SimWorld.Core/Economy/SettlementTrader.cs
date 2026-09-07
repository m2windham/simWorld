using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.Economy
{
    /// <summary>
    /// A settlement's side of a trade (RimWorld: a <c>Settlement</c>'s <c>ITrader</c> implementation backed
    /// by its <c>trader</c> comp). Stock is a plain list here — real stock generation from
    /// <see cref="TraderKindDef.stockGenerators"/> needs real Things and lands with those systems.
    /// </summary>
    public class SettlementTrader : ITrader
    {
        public TraderKindDef TraderKind { get; }

        public Faction? Faction { get; }

        public readonly List<(ThingDef def, int count)> goods = new List<(ThingDef def, int count)>();

        public SettlementTrader(TraderKindDef traderKind, Faction? faction)
        {
            TraderKind = traderKind ?? throw new ArgumentNullException(nameof(traderKind));
            Faction = faction;
        }

        public IEnumerable<(ThingDef def, int count)> Goods => goods;
    }
}
