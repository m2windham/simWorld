using System;
using System.Collections.Generic;
using SimWorld.Caravans;
using SimWorld.World;
using SimWorld.World.Siting;

namespace SimWorld.Economy
{
    /// <summary>
    /// One settlement's access to coal — from its own deposits, or failing that from the nearest other
    /// settlement that has it, at a real trade markup (spec §5b.2: "geography shapes rather than dictates...
    /// caravans and trade can supply a settlement that lacks coal, at a cost"). This is the predicate/score
    /// a later era-transition or industrialisation check reads (out of scope here per this module's brief);
    /// <see cref="CoalSupply"/> only produces the number, exactly the way <see cref="SiteScorer"/> only
    /// produces a site score without deciding what reads it.
    /// </summary>
    public readonly struct CoalAccess
    {
        /// <summary>False when neither a local deposit nor any reachable settlement can supply coal at all — the settlement simply has none.</summary>
        public bool HasAccess { get; }

        /// <summary>True when access comes from this settlement's own tile/neighbours rather than a trade route.</summary>
        public bool IsLocal { get; }

        /// <summary>
        /// Silver cost to acquire one unit of Coal under this access: raw <see cref="TradeUtility.BaseMarketValue"/>
        /// when <see cref="IsLocal"/> (you already own the deposit — no trader markup applies), or
        /// <see cref="TradeUtility.GetPricePlayerBuy"/> marked up further by route distance otherwise. 0 when
        /// <see cref="HasAccess"/> is false.
        /// </summary>
        public float UnitCost { get; }

        /// <summary>The tile coal is actually drawn from: this settlement's own tile when local, otherwise the supplying settlement's. Null when <see cref="HasAccess"/> is false.</summary>
        public int? SourceTile { get; }

        private CoalAccess(bool hasAccess, bool isLocal, float unitCost, int? sourceTile)
        {
            HasAccess = hasAccess;
            IsLocal = isLocal;
            UnitCost = unitCost;
            SourceTile = sourceTile;
        }

        public static readonly CoalAccess None = new CoalAccess(false, false, 0f, null);

        public static CoalAccess Local(int tile, float unitCost) => new CoalAccess(true, true, unitCost, tile);

        public static CoalAccess Traded(int sourceTile, float unitCost) => new CoalAccess(true, false, unitCost, sourceTile);
    }

    /// <summary>
    /// Evaluates <see cref="CoalAccess"/> for a settlement tile (spec §5b.2). RimWorld has nothing resembling
    /// this — trade there never substitutes for a missing world-gen resource — so every threshold and the
    /// price-model shape below are this port's own judgement call. Deliberately takes a plain
    /// <see cref="WorldGrid"/> and an explicit candidate list rather than a full <c>World.World</c>, the same
    /// shape <see cref="TradePositionScorer"/> already uses, so a caller supplies exactly the settlements it
    /// wants considered rather than this reaching into world state on its own.
    /// </summary>
    public static class CoalSupply
    {
        /// <summary>
        /// Deposit magnitude (own tile or a neighbour, via <see cref="SiteScorer.BestNearbyMagnitude"/>)
        /// at/above which a settlement counts as having coal "in reach" locally at all — the same
        /// ramp-floor idiom <c>Siting.SiteTuning</c> uses for its own necessity floors. This port's own
        /// judgement call.
        /// </summary>
        public const float LocalCoalFloor = 0.15f;

        /// <summary>
        /// Extra multiplier added per unit of <see cref="WorldPathGrid.MovementCostBetween"/> the cheapest
        /// route to a supplying settlement costs, stacked on top of <see cref="TradeUtility.BuyPriceFactor"/>'s
        /// own markup — so a short hop between neighbours barely moves the price while a genuine long haul
        /// prices coal well above what sitting on a local deposit would. This port's own judgement call;
        /// RimWorld has no route-distance trade premium to source it from.
        /// </summary>
        public const float RoutePremiumPerMovementCost = 0.02f;

        /// <summary>
        /// Local deposits first; failing that, the cheapest-to-reach settlement (by <see cref="WorldPathFinder"/>
        /// route cost, the same pathing <c>Caravans.Caravan</c> itself travels by) among
        /// <paramref name="otherSettlementTiles"/> that has coal locally. <see cref="CoalAccess.None"/> when
        /// neither exists — including when every candidate is unreachable (e.g. across open water).
        /// </summary>
        public static CoalAccess Evaluate(WorldGrid grid, int settlementTile, IReadOnlyList<int> otherSettlementTiles)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (otherSettlementTiles == null) throw new ArgumentNullException(nameof(otherSettlementTiles));

            float local = SiteScorer.BestNearbyMagnitude(grid, settlementTile, DepositDefOf.Coal);
            if (local >= LocalCoalFloor)
            {
                return CoalAccess.Local(settlementTile, TradeUtility.BaseMarketValue(EconomyThingDefOf.Coal));
            }

            int? bestSource = null;
            float bestRouteCost = float.MaxValue;
            for (int i = 0; i < otherSettlementTiles.Count; i++)
            {
                int candidate = otherSettlementTiles[i];
                if (candidate == settlementTile) continue;

                float candidateLocal = SiteScorer.BestNearbyMagnitude(grid, candidate, DepositDefOf.Coal);
                if (candidateLocal < LocalCoalFloor) continue;

                if (!WorldPathFinder.FindPath(grid, settlementTile, candidate, out List<int> path) || path.Count < 2) continue;

                float routeCost = TotalMovementCost(grid, path);
                if (routeCost < bestRouteCost)
                {
                    bestRouteCost = routeCost;
                    bestSource = candidate;
                }
            }

            if (bestSource == null) return CoalAccess.None;

            float routeMultiplier = 1f + bestRouteCost * RoutePremiumPerMovementCost;
            float unitCost = TradeUtility.GetPricePlayerBuy(EconomyThingDefOf.Coal, PriceType.Normal, routeMultiplier);
            return CoalAccess.Traded(bestSource.Value, unitCost);
        }

        private static float TotalMovementCost(WorldGrid grid, List<int> path)
        {
            float total = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                total += WorldPathGrid.MovementCostBetween(grid, path[i], path[i + 1]);
            }
            return total;
        }
    }
}
