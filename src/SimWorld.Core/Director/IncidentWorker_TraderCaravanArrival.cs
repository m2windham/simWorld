using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Director
{
    /// <summary>
    /// Real trader generation <b>and a real arrival</b> (RimWorld:
    /// <c>RimWorld.IncidentWorker_TraderCaravanArrival</c>), replacing <see cref="IncidentWorker_Placeholder"/>
    /// for <c>TraderCaravanArrival</c>. Picks a faction not hostile to the player that actually has something
    /// to sell — <see cref="FactionDef.caravanTraderKinds"/> non-empty — then a <see cref="TraderKindDef"/>
    /// from that faction's list (<see cref="FactionDef.RandomTraderKind"/>, weighted by
    /// <see cref="TraderKindDef.commonality"/>), generates real stock for it through
    /// <see cref="TraderKindDef.GenerateStock"/>, and <b>lands it on a real settlement's tile for a real
    /// period</b> (<see cref="Economy.TraderArrival.Land"/>).
    ///
    /// <para/><b>What changed, and why it mattered.</b> This worker's previous doc named its own stopping
    /// point honestly: "actually walking a caravan onto the world map and opening a session at a specific
    /// settlement is Caravans/World's to build on top of this". Nothing built it, and the consequence was the
    /// exact shape <c>docs/design/player-first.md</c> §9 warns about — a finished mechanism with no caller.
    /// <see cref="Economy.SettlementTradeUtility.OpenSession"/> had none anywhere in <c>src/</c>: a caravan
    /// arrived, carrying priced goods, and there was no way to buy or sell any of it. The trader now goes
    /// somewhere and stays a while (<see cref="Economy.TraderCaravan"/>), which is what a session needs in
    /// order to be openable against anything.
    ///
    /// <para/><b>Where it lands.</b> <see cref="CivilizationTarget.ChooseTargetSettlement"/> — the mechanism
    /// this codebase already uses to decide where an incident that has to happen somewhere happens, weighted
    /// by population so a caravan is likelier to call at the capital than at a hamlet. No second, competing
    /// rule for the same question.
    ///
    /// <para/><b>What still only generates.</b> A firing against a target that is not a
    /// <see cref="CivilizationTarget"/>, or against one with no settlements, or with no world running, still
    /// succeeds and still produces a real priced trader on <see cref="LastTrader"/> — there is simply nowhere
    /// for it to stand, and <see cref="LastArrival"/> is null to say so. That is the state a bare test pose
    /// puts the director in, not one a running game reaches; a civilization with no settlements is not a
    /// civilization a caravan could visit. The incident is not failed for it, because failing would make the
    /// director retry an incident whose only problem is that there is no world yet.
    /// </summary>
    public sealed class IncidentWorker_TraderCaravanArrival : IncidentWorker
    {
        /// <summary>Test/inspection hook (mirrors <see cref="IncidentWorker_RaidEnemy.LastRaidFaction"/>'s own pattern): the faction the most recent successful firing generated a trader for.</summary>
        public Faction? LastTraderFaction { get; private set; }

        /// <summary>The trader kind the most recent successful firing picked.</summary>
        public TraderKindDef? LastTraderKind { get; private set; }

        /// <summary>The trader — with real, priced stock — the most recent successful firing generated. The
        /// same object as <see cref="LastArrival"/> whenever one actually landed, so a caller never has to
        /// reconcile two views of one trader.</summary>
        public ITrader? LastTrader { get; private set; }

        /// <summary>The caravan the most recent successful firing actually put on the map, or null when there
        /// was nowhere to put one — see the class doc's last paragraph.</summary>
        public TraderCaravan? LastArrival { get; private set; }

        protected override bool CanFireNowSub(IncidentParms parms) => CandidateFactions().Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Faction? faction = ResolveFaction(parms);
            if (faction == null) return false;

            TraderKindDef? kind = faction.def.RandomTraderKind(Rand.Current);
            if (kind == null) return false;

            // Rolled once, whichever branch below takes it: the stock a caravan carries must not depend on
            // whether there happened to be somewhere for it to stand, or two identically-seeded games would
            // diverge on the first firing against a world that had not been built yet.
            List<(ThingDef def, int count)> stock = kind.GenerateStock(Rand.Current);

            parms.faction = faction;
            LastTraderFaction = faction;
            LastTraderKind = kind;

            LastArrival = TryLand(parms, faction, kind, stock);
            if (LastArrival != null)
            {
                LastTrader = LastArrival;
            }
            else
            {
                var trader = new SettlementTrader(kind, faction);
                trader.goods.AddRange(stock);
                LastTrader = trader;
            }
            return true;
        }

        /// <summary>Puts the caravan on the settlement the director picks, or returns null when this firing
        /// has no civilization with settlements behind it (see the class doc).</summary>
        private static TraderCaravan? TryLand(
            IncidentParms parms, Faction faction, TraderKindDef kind, List<(ThingDef def, int count)> stock)
        {
            if (!(parms.target is CivilizationTarget civilization)) return null;

            Settlement? host = civilization.ChooseTargetSettlement(Rand.Current);
            if (host == null) return null;

            return TraderArrival.Land(host, kind, faction, stock, Rand.Current);
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
