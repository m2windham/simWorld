using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.World;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// Arrival, presence and departure for <see cref="TraderCaravan"/> — "is a trader here, and where".
    ///
    /// <para/>Everything below is a read of <see cref="CoreWorld.worldObjects"/> against the current tick.
    /// There is no registry, no cache and no state of this module's own, which is what makes a save carrying
    /// a present trader correct by construction: the caravans <i>are</i> the state, they are already saved
    /// with the world, and "which of them is here" is recomputed from their own arrival and departure ticks
    /// every time it is asked (<see cref="TraderCaravan.PresentAt"/>).
    /// </summary>
    public static class TraderArrival
    {
        /// <summary>
        /// Puts a caravan carrying <paramref name="stock"/> on <paramref name="host"/>'s tile for a stay
        /// rolled from <see cref="TraderArrivalTuning.StayDurationTicks"/>, and returns it. Null — nothing
        /// added — when there is no world running, or when <paramref name="host"/> is already hosting a
        /// caravan (see below).
        ///
        /// <para/><b>One caravan per settlement at a time.</b> A settlement is named to the host by its world
        /// tile, exactly as <c>God.View.GodCommands.FocusSettlement</c> names one and for the same reason
        /// (<see cref="God.AttentionManager"/>'s own doc), so a second caravan on the same tile would give the
        /// command surface an ambiguous handle — "buy from the trader at tile 12" with two answers. Rather
        /// than inventing a caravan id the player has no way to see, an arrival at an occupied settlement
        /// simply does not land: the roads there are already busy. The incident still counts as having fired,
        /// and a trader was still generated — see
        /// <c>Director.IncidentWorker_TraderCaravanArrival.LastTrader</c>.
        ///
        /// <para/><b>Every draw goes through <paramref name="rand"/>.</b> The stay length is the only roll
        /// here, and it is taken from the caller's stream rather than any ambient one, so two identically
        /// seeded games get caravans that arrive and leave on the same ticks (CLAUDE.md: determinism is a
        /// feature).
        /// </summary>
        public static TraderCaravan? Land(
            Settlement host,
            TraderKindDef traderKind,
            Faction? faction,
            IEnumerable<(ThingDef def, int count)> stock,
            RandomStream rand)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (traderKind == null) throw new ArgumentNullException(nameof(traderKind));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            CoreWorld? world = Find.World;
            if (world == null) return null;

            // Before the occupancy test, not after: a caravan whose stay ran out is not "there", so it must
            // not be what keeps the next one from landing.
            PruneDeparted();
            if (At(host.tile) != null) return null;

            int now = Find.TickManager.TicksGame;
            var caravan = new TraderCaravan(
                EconomyWorldObjectDefOf.TradeCaravan,
                host.tile,
                faction,
                traderKind,
                now,
                now + Math.Max(1, rand.Range(TraderArrivalTuning.StayDurationTicks)));

            foreach ((ThingDef def, int count) in stock)
            {
                if (def != null && count > 0) caravan.AddStock(def, count);
            }

            world.worldObjects.Add(caravan);
            return caravan;
        }

        /// <summary>
        /// The caravan standing on <paramref name="settlementTile"/> right now, or null. The one place a tile
        /// is turned back into a trader, so the read model and the command surface can never disagree about
        /// whether one is there — the same single-lookup discipline
        /// <see cref="God.AttentionManager.SettlementAt"/> keeps for settlements.
        /// </summary>
        public static TraderCaravan? At(int settlementTile)
        {
            CoreWorld? world = Find.World;
            if (world == null) return null;

            int now = Find.TickManager.TicksGame;
            IReadOnlyList<WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is TraderCaravan caravan && caravan.tile == settlementTile && caravan.PresentAt(now))
                {
                    return caravan;
                }
            }
            return null;
        }

        /// <summary>Every caravan standing somewhere right now, in world-object order — the same stable order
        /// <c>God.View.GodViewSnapshot.Capture</c> lists settlements in.</summary>
        public static List<TraderCaravan> PresentNow()
        {
            var found = new List<TraderCaravan>();
            CoreWorld? world = Find.World;
            if (world == null) return found;

            int now = Find.TickManager.TicksGame;
            IReadOnlyList<WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is TraderCaravan caravan && caravan.PresentAt(now)) found.Add(caravan);
            }
            return found;
        }

        /// <summary>
        /// Drops every caravan whose stay has run out from <see cref="CoreWorld.worldObjects"/>, and returns
        /// how many left. Pure garbage collection: a departed caravan is already invisible to every reader
        /// here, because every one of them asks <see cref="TraderCaravan.PresentAt"/> rather than asking
        /// whether the object still exists.
        ///
        /// <para/><b>Why this is not done from <see cref="WorldObject.Tick"/>, which would be the obvious
        /// place.</b> <see cref="CoreWorld.WorldTick"/> walks <c>worldObjects</c> forward by index; an object
        /// removing itself during its own tick shifts the list under that loop and costs the next object its
        /// tick for that pass. For a <see cref="Settlement"/> that is not cosmetic — its growth sweep is gated
        /// on <c>TicksGame % DemographyIntervalTicks == 0</c>, so one skipped tick can drop a whole year of
        /// demography, silently and rarely, which is the worst shape a defect can have. Trading a guaranteed
        /// bug for a bounded amount of litter is the right way round.
        ///
        /// <para/><b>And the litter is bounded at one.</b> This runs from every path that writes — an arrival
        /// (<see cref="Land"/>) and every trade command — and never from a read, because a snapshot that
        /// mutated the simulation would break the one promise <c>God.View.GodViewSnapshot</c>'s class doc
        /// makes about itself. Arrivals are the only thing that creates a caravan and each one prunes first,
        /// so at most one departed caravan can be waiting at any time: an inert record of a def, a tile, a
        /// faction and two ints.
        /// </summary>
        public static int PruneDeparted()
        {
            CoreWorld? world = Find.World;
            if (world == null) return 0;

            int now = Find.TickManager.TicksGame;
            List<WorldObject> objects = world.worldObjects;
            int removed = 0;
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                // Gone, specifically — not merely "not present". The two differ for a caravan whose arrival
                // tick is still ahead of the clock, which nothing produces today (an arrival always lands at
                // the current tick) but which a rewound clock in a test or a hand-built pose can reach; such a
                // caravan is waiting, not litter, and deleting it would be this sweep quietly cancelling an
                // arrival.
                if (objects[i] is TraderCaravan caravan && now >= caravan.DepartureTick)
                {
                    objects.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
