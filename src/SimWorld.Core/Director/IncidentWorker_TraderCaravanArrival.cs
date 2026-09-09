using System.Collections.Generic;
using System.Linq;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Real trader generation (RimWorld: <c>RimWorld.IncidentWorker_TraderCaravanArrival</c>), replacing
    /// <see cref="IncidentWorker_Placeholder"/> for <c>TraderCaravanArrival</c>. Picks a faction not hostile
    /// to the player that actually has something to sell — <see cref="FactionDef.caravanTraderKinds"/>
    /// non-empty — then a <see cref="TraderKindDef"/> from that faction's list
    /// (<see cref="FactionDef.RandomTraderKind"/>, weighted by <see cref="TraderKindDef.commonality"/>) and
    /// generates real stock for it through <see cref="TraderKindDef.GenerateStock"/> — the same
    /// <see cref="StockGenerator"/> pipeline any player-opened <see cref="TradeSession"/> reads, priced
    /// through the very same <see cref="TradeUtility"/> every other trade in this game goes through.
    /// <para/>
    /// <b>Where this stops — read before assuming a trader shows up and starts selling:</b> the exact
    /// caveat <see cref="IncidentWorker_RaidEnemy"/>'s own doc spells out for raiders applies here too.
    /// Nothing in the civilization-scale incident model (<see cref="IIncidentTarget"/> /
    /// <see cref="CivilizationTarget"/>) carries a reachable <c>World.World</c> or a specific
    /// <c>World.Settlement</c> for this trader to travel to or open a session against — those only exist
    /// once a caller supplies them (<see cref="SettlementTradeUtility"/> is exactly that: opening a session
    /// against a real settlement's store ledger). This worker is fully generated — a real
    /// <see cref="TraderKindDef"/>, a real faction, real priced stock — and handed to the caller through
    /// <see cref="LastTrader"/>/<see cref="LastTraderFaction"/>/<see cref="LastTraderKind"/>; actually
    /// walking a caravan onto the world map and opening a session at a specific settlement is Caravans/World's
    /// to build on top of this, the same seam <see cref="IncidentWorker_RaidEnemy"/> leaves open for a raid's
    /// map arrival.
    /// </summary>
    public sealed class IncidentWorker_TraderCaravanArrival : IncidentWorker
    {
        /// <summary>Test/inspection hook (mirrors <see cref="IncidentWorker_RaidEnemy.LastRaidFaction"/>'s own pattern): the faction the most recent successful firing generated a trader for.</summary>
        public Faction? LastTraderFaction { get; private set; }

        /// <summary>The trader kind the most recent successful firing picked.</summary>
        public TraderKindDef? LastTraderKind { get; private set; }

        /// <summary>The trader — with real, priced stock — the most recent successful firing generated.</summary>
        public ITrader? LastTrader { get; private set; }

        protected override bool CanFireNowSub(IncidentParms parms) => CandidateFactions().Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Faction? faction = ResolveFaction(parms);
            if (faction == null) return false;

            TraderKindDef? kind = faction.def.RandomTraderKind(Rand.Current);
            if (kind == null) return false;

            var trader = new SettlementTrader(kind, faction);
            trader.goods.AddRange(kind.GenerateStock(Rand.Current));

            parms.faction = faction;
            LastTraderFaction = faction;
            LastTraderKind = kind;
            LastTrader = trader;
            return true;
        }

        private static Faction? ResolveFaction(IncidentParms parms)
        {
            if (parms.faction != null) return parms.faction;
            List<Faction> candidates = CandidateFactions().ToList();
            return candidates.Count == 0 ? null : Rand.Element((IReadOnlyList<Faction>)candidates);
        }

        /// <summary>Not hostile to the player and actually has a trader kind to arrive as (RimWorld's own <c>RandomNonHostileFaction</c> pick has no "and can trade" half — this port's own tightening, since a fired incident that immediately fails to produce a trader is worse than not firing at all).</summary>
        private static IEnumerable<Faction> CandidateFactions()
        {
            Faction? player = Find.FactionManager.OfPlayer;
            if (player == null) yield break;
            foreach (Faction f in Find.FactionManager.GetFactions())
            {
                if (ReferenceEquals(f, player) || f.HostileTo(player)) continue;
                if (f.def.caravanTraderKinds == null || f.def.caravanTraderKinds.Count == 0) continue;
                yield return f;
            }
        }
    }
}
