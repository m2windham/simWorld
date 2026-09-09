using System;
using System.Collections.Generic;
using SimWorld.Caravans;
using SimWorld.Defs;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// Whether a caravan can currently move goods between two settlements, and at what price premium
    /// (SimWorld's own translation of "trade routes between civilizations" — RimWorld has no such concept
    /// at all, only a single colony visited by orbital/caravan traders; see docs/status.json's
    /// <c>economy</c> system <c>translation</c> field). Mirrors <see cref="CoalAccess"/>'s own shape closely
    /// on purpose: a real path through <see cref="WorldPathFinder"/>, priced by the same route-distance
    /// premium idiom <see cref="CoalSupply"/> already established — see
    /// <see cref="TradeRouteUtility.RoutePremiumPerMovementCost"/>'s own doc for why this reuses that shape
    /// with its own number rather than literally reusing <see cref="CoalSupply.RoutePremiumPerMovementCost"/>.
    /// </summary>
    public readonly struct TradeRouteAccess
    {
        /// <summary>False when no path exists at all between the two tiles (e.g. across open water) — the same "unreachable" case <see cref="CoalAccess.HasAccess"/> covers.</summary>
        public bool HasRoute { get; }

        /// <summary>Total <see cref="WorldPathGrid.MovementCostBetween"/> along the cheapest path — the same unit <see cref="Caravan"/> itself travels by. 0 when <see cref="HasRoute"/> is false.</summary>
        public float RouteCost { get; }

        /// <summary>Multiplier this route applies on top of an ordinary <see cref="TradeUtility"/> price (1 = no premium — e.g. the two settlements share a tile). 1 when <see cref="HasRoute"/> is false (callers should check <see cref="HasRoute"/> first; this is never used to imply a route where none exists).</summary>
        public float PriceMultiplier { get; }

        private TradeRouteAccess(bool hasRoute, float routeCost, float priceMultiplier)
        {
            HasRoute = hasRoute;
            RouteCost = routeCost;
            PriceMultiplier = priceMultiplier;
        }

        public static readonly TradeRouteAccess None = new TradeRouteAccess(false, 0f, 1f);

        public static TradeRouteAccess Connected(float routeCost, float priceMultiplier) => new TradeRouteAccess(true, routeCost, priceMultiplier);

        /// <summary>
        /// What one unit of <paramref name="def"/> would cost bought over this route — <see cref="TradeUtility.GetPricePlayerBuy"/>
        /// marked up by <see cref="PriceMultiplier"/>, the same composition <see cref="CoalSupply"/> uses
        /// for <see cref="CoalAccess.UnitCost"/>, generalized to any def rather than only Coal. 0 when
        /// <see cref="HasRoute"/> is false.
        /// </summary>
        public float UnitCostFor(ThingDef def, PriceType priceType = PriceType.Normal)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return HasRoute ? TradeUtility.GetPricePlayerBuy(def, priceType, PriceMultiplier) : 0f;
        }
    }

    /// <summary>
    /// Evaluates <see cref="TradeRouteAccess"/> between two settlements, gated on the explicit war state
    /// <see cref="Factions.Faction.WarWith"/> now carries: a caravan cannot run a trade route between two
    /// settlements whose factions are at war, the civilization-scale reading of "war closes the roads
    /// between belligerents". Peace (<see cref="Factions.Faction.MakePeace"/>) reopens a route immediately —
    /// there is no separate "reopen" step, since <see cref="RouteOpen"/> is evaluated fresh every call,
    /// never cached, exactly like <see cref="CoalSupply.Evaluate(WorldGrid, int, IReadOnlyList{int})"/> is.
    /// This is SimWorld's own translation; RimWorld has no route-closing mechanic to source it from.
    /// </summary>
    public static class TradeRouteUtility
    {
        /// <summary>
        /// Extra multiplier per unit of route movement cost, stacked on top of an ordinary buy price the
        /// same way <see cref="CoalSupply.RoutePremiumPerMovementCost"/> stacks on Coal's. Tuned smaller
        /// than CoalSupply's own number on purpose: Coal there prices an emergency substitute for a missing
        /// world-gen resource, worth almost any markup; an ordinary trade route is the normal way goods move
        /// between settlements and a few hops should not make trading prohibitively expensive. This port's
        /// own judgement call — RimWorld has no route-distance trade premium to source either number from.
        /// </summary>
        public const float RoutePremiumPerMovementCost = 0.01f;

        /// <summary>
        /// The cheapest real path between two settlement tiles, with no war/peace gating (see
        /// <see cref="RouteOpen"/> for that). <see cref="TradeRouteAccess.None"/> when no path exists at all.
        /// Two settlements on the same tile connect for free (0 cost, no markup) rather than going through
        /// the pathfinder at all.
        /// </summary>
        public static TradeRouteAccess Evaluate(WorldGrid grid, int fromTile, int toTile)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (fromTile == toTile) return TradeRouteAccess.Connected(0f, 1f);

            if (!WorldPathFinder.FindPath(grid, fromTile, toTile, out List<int> path) || path.Count < 2)
            {
                return TradeRouteAccess.None;
            }

            float routeCost = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                routeCost += WorldPathGrid.MovementCostBetween(grid, path[i], path[i + 1]);
            }
            return TradeRouteAccess.Connected(routeCost, 1f + routeCost * RoutePremiumPerMovementCost);
        }

        /// <summary>
        /// Whether a trade route between two settlements is open at all right now: a real path exists AND
        /// neither settlement's faction is at war with the other's (see the class doc). A factionless
        /// settlement (null <see cref="World.WorldObject.faction"/>) is never considered at war with anyone,
        /// so its routes are gated on reachability alone.
        /// </summary>
        public static bool RouteOpen(WorldGrid grid, Settlement from, Settlement to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.faction != null && to.faction != null && from.faction.WarWith(to.faction)) return false;
            return Evaluate(grid, from.tile, to.tile).HasRoute;
        }
    }
}
