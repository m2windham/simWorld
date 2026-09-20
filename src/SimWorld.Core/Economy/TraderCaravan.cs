using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// A trade caravan that has actually arrived somewhere and is standing on a world tile for a while — the
    /// piece the trade system was missing entirely.
    ///
    /// <para/><b>The defect this closes.</b> <c>Director.IncidentWorker_TraderCaravanArrival</c> generated a
    /// real trader with real, priced stock and handed it back through a test hook, and its own class doc said
    /// what was missing in as many words: "actually walking a caravan onto the world map and opening a session
    /// at a specific settlement is Caravans/World's to build on top of this". Nothing ever built it, so
    /// <see cref="SettlementTradeUtility.OpenSession"/> had no caller anywhere in <c>src/</c>: a caravan
    /// arrived, carrying goods, and nothing could ever be bought or sold.
    /// <c>docs/design/player-first.md</c> §2 says a new kind of player influence means building the causal
    /// path rather than exposing a setter, and §9 says a mechanism with no caller is not a feature. This is
    /// that path's first link: <b>a trader that <i>is</i> somewhere, for a period, and then leaves.</b>
    ///
    /// <para/><b>Why a <see cref="WorldObject"/>, and not a <c>Caravans.Caravan</c>.</b> Being a
    /// <see cref="WorldObject"/> buys three things this needs and would otherwise have to reinvent: a tile
    /// (<see cref="WorldObject.tile"/>), a faction (<see cref="WorldObject.faction"/>), and — the load-bearing
    /// one — a save. <see cref="World.World.worldObjects"/> is deep-saved polymorphically (the element's
    /// runtime type is written as a <c>Class</c> attribute), which is exactly the argument
    /// <see cref="Settlement"/>'s own doc makes for subclassing rather than composing. <c>Caravans.Caravan</c>
    /// is the wrong base despite the name: it models a band of <c>Pawn</c>s walking a path and eating, and
    /// <see cref="TraderKindDef"/>'s own doc already rules per-trader pawn rosters out of this port's trade
    /// model — "this port's trade model only ever needs <em>goods</em> to move, not the pawns carrying them".
    /// A visiting trader is stock and a deadline, not a marching column.
    ///
    /// <para/><b>Presence is a pure function of the tick number, not a flag this object maintains.</b>
    /// <see cref="PresentAt"/> compares a tick against <see cref="ArrivalTick"/>/<see cref="DepartureTick"/>
    /// and nothing else, so a caravan is gone the exact instant its stay runs out whether or not any sweep has
    /// run, and a save that is loaded a year later loads a caravan that is correctly already gone with no
    /// post-load fixup. That is also why this type has no <see cref="WorldObject.Tick"/> override at all:
    /// there is nothing for a tick to advance.
    ///
    /// <para/><b>Why departure does not remove this object from inside a tick.</b>
    /// <see cref="World.World.WorldTick"/> walks <c>worldObjects</c> forward by index, so an object that
    /// removed itself during its own <c>Tick</c> would shift the list under that loop and cost the <i>next</i>
    /// object its tick for that pass — and for a <see cref="Settlement"/> whose growth sweep is gated on
    /// <c>TicksGame % interval == 0</c>, a single skipped tick is a whole missed year of demography. Removal
    /// is therefore garbage collection done from outside the loop: see
    /// <see cref="TraderArrival.PruneDeparted"/> for where, and for the bound on how long a departed caravan
    /// can linger as an inert record (it is at most one, and it is invisible to every reader, because every
    /// reader asks <see cref="Present"/>).
    ///
    /// <para/><b>Stock is ordered, not a dictionary.</b> Two identically-seeded games must produce identical
    /// traders (CLAUDE.md: determinism is a feature), and the order goods are offered in reaches the player
    /// through <c>God.View.GodViewSnapshot.VisitingTraders</c>. Parallel lists preserve generation order
    /// exactly; a <c>Dictionary</c> only preserves it by implementation accident, and stops the moment an
    /// entry is removed. A sold-out good keeps its slot at zero rather than being removed, so the order a
    /// player is looking at never rearranges itself under them mid-visit.
    /// </summary>
    public sealed class TraderCaravan : WorldObject, ITrader
    {
        private TraderKindDef traderKind = null!;

        /// <summary>Kept parallel with <see cref="stockCounts"/>, in generation order; see the class doc.</summary>
        private List<ThingDef> stockDefs = new List<ThingDef>();

        private List<int> stockCounts = new List<int>();

        private int arrivalTick;

        private int departureTick;

        /// <summary>For Scribe's deep-load construction.</summary>
        public TraderCaravan()
        {
        }

        public TraderCaravan(
            WorldObjectDef def,
            int tile,
            Faction? faction,
            TraderKindDef traderKind,
            int arrivalTick,
            int departureTick)
            : base(def, tile, faction)
        {
            this.traderKind = traderKind ?? throw new ArgumentNullException(nameof(traderKind));
            this.arrivalTick = arrivalTick;
            this.departureTick = departureTick;
        }

        // ---- ITrader ----

        public TraderKindDef TraderKind => traderKind;

        /// <summary>The civilization this caravan belongs to — <see cref="WorldObject.faction"/> read through
        /// the interface, rather than a second field that could disagree with the one the world already saves.</summary>
        public Faction? Faction => faction;

        /// <summary>
        /// What is actually on the table right now, in generation order, skipping anything sold out. A
        /// sold-out line is skipped rather than reported as zero because <see cref="TradeSession.SetupWith"/>
        /// would otherwise seed a <see cref="Tradeable"/> the player can only look at — the same reason
        /// <see cref="Settlement.SetStoreCount"/> drops an entry that falls to zero.
        /// </summary>
        public IEnumerable<(ThingDef def, int count)> Goods
        {
            get
            {
                for (int i = 0; i < stockDefs.Count && i < stockCounts.Count; i++)
                {
                    if (stockCounts[i] > 0) yield return (stockDefs[i], stockCounts[i]);
                }
            }
        }

        // ---- presence ----

        public int ArrivalTick => arrivalTick;

        /// <summary>The first tick this caravan is no longer here. Exclusive, so a stay of exactly one day is
        /// one day and not one day plus a tick.</summary>
        public int DepartureTick => departureTick;

        /// <summary>Whether this caravan is standing on its tile at <paramref name="tick"/>. The single
        /// definition of "here"; nothing else in this module decides it, so the read model and the command
        /// surface can never disagree about whether a trader is available.</summary>
        public bool PresentAt(int tick) => tick >= arrivalTick && tick < departureTick;

        public bool Present => PresentAt(Find.TickManager.TicksGame);

        /// <summary>How long the player has left to decide, floored at zero.</summary>
        public int TicksUntilDeparture => Math.Max(0, departureTick - Find.TickManager.TicksGame);

        // ---- stock ----

        public int StockOf(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            int index = stockDefs.IndexOf(def);
            return index >= 0 && index < stockCounts.Count ? stockCounts[index] : 0;
        }

        /// <summary>
        /// Sets an exact count, adding the def at the end of the order if the caravan did not carry it.
        ///
        /// <para/>A negative count is clamped to zero rather than stored: this is written back from a
        /// completed <see cref="TradeDeal"/>, and a caravan that owed goods would be a quietly wrong number
        /// rather than a refusal anybody could see. The refusals that keep a deal from getting there in the
        /// first place live on the command surface (<c>God.View.GodCommands</c>), where they can explain
        /// themselves.
        ///
        /// <para/>A def that falls to zero keeps its slot — see the class doc on ordering.
        /// </summary>
        public void SetStock(ThingDef def, int count)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (count < 0) count = 0;

            int index = stockDefs.IndexOf(def);
            if (index < 0)
            {
                if (count == 0) return;
                stockDefs.Add(def);
                stockCounts.Add(count);
                return;
            }
            stockCounts[index] = count;
        }

        /// <summary>Adds <paramref name="count"/> more of <paramref name="def"/>, merging a def a stock
        /// generator produced twice into one line rather than offering it on two.</summary>
        public void AddStock(ThingDef def, int count) => SetStock(def, StockOf(def) + count);

        /// <summary>Everything this caravan is carrying, sold out entries included, in generation order — the
        /// view's own ordering source. <see cref="Goods"/> is the trading surface; this is the inventory.</summary>
        public IReadOnlyList<ThingDef> StockOrder => stockDefs;

        // ---- Scribe ----

        public override void ExposeData()
        {
            base.ExposeData();

            TraderKindDef? kind = traderKind;
            Scribe_Defs.Look(ref kind, "traderKind");
            traderKind = kind!;

            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0);
            Scribe_Values.Look(ref departureTick, "departureTick", 0);

            List<ThingDef>? defs = stockDefs;
            Scribe_Collections.Look(ref defs, "stockDefs", LookMode.Def);
            stockDefs = defs ?? new List<ThingDef>();

            List<int>? counts = stockCounts;
            Scribe_Collections.Look(ref counts, "stockCounts", LookMode.Value);
            stockCounts = counts ?? new List<int>();

            // Two parallel lists can only ever be read together, so a save that lost one of them (an older
            // format, a hand-edited file) is trimmed to the pairs that survived rather than left to throw on
            // the first StockOf. Every accessor above already bounds itself the same way; this keeps the two
            // lists honest as well as safe.
            while (stockDefs.Count > stockCounts.Count) stockDefs.RemoveAt(stockDefs.Count - 1);
            while (stockCounts.Count > stockDefs.Count) stockCounts.RemoveAt(stockCounts.Count - 1);
        }

        public override string ToString() =>
            (traderKind?.defName ?? "TraderCaravan") + "@" + tile;
    }
}
