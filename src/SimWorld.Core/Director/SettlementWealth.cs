using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Pawns;
using SimWorld.Stats;
using SimWorld.World;

namespace SimWorld.Director
{
    /// <summary>
    /// Wealth in silver, split the way the storyteller splits it (RimWorld: the three terms of
    /// <c>Verse.Map.PlayerWealthForStoryteller</c>, <c>WealthWatcher.WealthItems</c>, <c>WealthBuildings</c>
    /// and <c>WealthPawns</c>). The three are kept apart because they are different kinds of reason: items are
    /// what is worth taking, buildings are what a raid has to get through, pawns are who the settlement is.
    /// All three are <i>unweighted</i>; <see cref="ForStoryteller"/> applies the storyteller's weighting.
    /// </summary>
    public readonly struct WealthBreakdown
    {
        public WealthBreakdown(float items, float buildings, float pawns)
        {
            Items = items;
            Buildings = buildings;
            Pawns = pawns;
        }

        /// <summary>Market value of goods: in storage, lying about, and in the hands and on the backs of the settlement's people.</summary>
        public float Items { get; }

        /// <summary>Market value of what the settlement has built (and of its floors, once any can be laid).</summary>
        public float Buildings { get; }

        /// <summary>Market value of the settlement's people and tame animals, each as an individual.</summary>
        public float Pawns { get; }

        /// <summary><c>items + 0.5 × buildings + pawns</c> — the figure <see cref="CivilizationTarget.PlayerWealthForStoryteller"/> reports.</summary>
        public float ForStoryteller => Items + Buildings * WealthWatcher.BuildingsFactorForStoryteller + Pawns;

        public static WealthBreakdown operator +(WealthBreakdown a, WealthBreakdown b) =>
            new WealthBreakdown(a.Items + b.Items, a.Buildings + b.Buildings, a.Pawns + b.Pawns);

        public override string ToString() =>
            "items " + Items.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
            + ", buildings " + Buildings.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
            + ", pawns " + Pawns.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What one settlement is worth to the storyteller, by whichever of two routes its state allows.
    ///
    /// <para/><b>A settlement whose map exists is counted off the map</b> (<see cref="Watched"/>): RimWorld's own
    /// <see cref="WealthWatcher"/> — the goods on the ground, the buildings its faction built, the people and
    /// animals standing on it, on RimWorld's recount cadence — plus the half of the settlement that is not on
    /// the map. <b>Translation:</b> RimWorld has no such half, because everything it owns is a Thing on its
    /// map. Here a unit of goods is "either a Thing on a settlement's interior map or a count in its
    /// <see cref="Settlement.Stores"/>, never both" (<see cref="Economy.SettlementStockInitiative"/>), so the
    /// goods are the two halves summed — leave the ledger out and banking a stockpile would make the settlement
    /// poorer — and a citizen the tiering system has taken off the map (<see cref="PawnTier"/>) is still the
    /// settlement's, so the people are those on the map plus those who are not. Buildings are the map alone: the
    /// structure ledger is not consulted while a map exists (<see cref="Settlement.StructureCount"/>).
    ///
    /// <para/><b>A settlement nobody has opened is estimated from its ledgers</b> (<see cref="Estimate"/>), the
    /// same three terms by the only means it has: <see cref="Settlement.Stores"/> for the goods,
    /// <see cref="Settlement.Structures"/> at each def's market value for the buildings, and its citizens priced
    /// one by one for the people. <b>This is a translation, recorded:</b> RimWorld has one map per colony and
    /// always the Things to count; a civilization of many settlements cannot hold every one of them as a map.
    /// The two routes are meant to agree for the same settlement, and a test opens one and compares them.
    ///
    /// <para/><b>The Statistical cohort is not priced</b> on either route: a bare count has no health, skill or
    /// age to price, and the storyteller's own roster (<see cref="CivilizationTarget.PlayerPawnsForStoryteller"/>)
    /// does not count them either, so wealth and head count describe the same people.
    /// </summary>
    public static class SettlementWealth
    {
        /// <summary>The route the settlement's state allows: its map if it has one, its ledgers if not.</summary>
        public static WealthBreakdown Of(Settlement settlement)
        {
            if (settlement == null) throw new System.ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            return map != null ? Watched(settlement, map) : Estimate(settlement);
        }

        /// <summary>A settlement with a map: the map's <see cref="WealthWatcher"/> plus the ledger and the people off the map.</summary>
        public static WealthBreakdown Watched(Settlement settlement, Map.Map map)
        {
            WealthWatcher watcher = map.wealthWatcher;
            float items = watcher.WealthItems + StoresValue(settlement);
            float pawns = watcher.WealthPawns;

            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn citizen = citizens[i];
                if (citizen.Dead || (citizen.Spawned && ReferenceEquals(citizen.Map, map))) continue;
                pawns += WealthWatcher.MarketValueOf(citizen);
                items += WealthWatcher.GetEquipmentApparelAndInventoryWealth(citizen);
            }

            return new WealthBreakdown(items, watcher.WealthBuildings, pawns);
        }

        /// <summary>A settlement with no map: its ledgers and its roster.</summary>
        public static WealthBreakdown Estimate(Settlement settlement)
        {
            if (settlement == null) throw new System.ArgumentNullException(nameof(settlement));

            float items = StoresValue(settlement);

            float buildings = 0f;
            foreach (KeyValuePair<ThingDef, int> entry in settlement.Structures)
            {
                buildings += settlement.StructureCount(entry.Key) * entry.Key.GetStatValue(StatDefOf.MarketValue);
            }

            float pawns = 0f;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (citizens[i].Dead) continue;
                pawns += WealthWatcher.MarketValueOf(citizens[i]);
                items += WealthWatcher.GetEquipmentApparelAndInventoryWealth(citizens[i]);
            }

            return new WealthBreakdown(items, buildings, pawns);
        }

        /// <summary>What the ledger holds, at the statBases-only value trade has always used for a stored good.</summary>
        private static float StoresValue(Settlement settlement)
        {
            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> entry in settlement.Stores)
            {
                total += TradeUtility.BaseMarketValue(entry.Key) * entry.Value;
            }
            return total;
        }
    }
}
