using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Economy
{
    /// <summary>
    /// <b>The seam between the two halves of the game, in the map-to-ledger direction</b> (spec §11.2's
    /// "civilization-wide sight, settlement-deep touch"). What a watched settlement mines, grows and crafts
    /// becomes something the civilization <i>has</i>.
    ///
    /// <para/><b>The defect this closes.</b> <see cref="Settlement.Stores"/> is the civilization-scale ledger
    /// and every consumer of a settlement's wealth reads it — trade (<c>SettlementTradeUtility</c>), tribute
    /// (<c>Quests.QuestPart_Tribute</c>), quest rewards (<c>Quests.QuestRewardSink</c>), raid loot
    /// (<c>Director.SettlementRaidResolver</c>), the storyteller's wealth term
    /// (<c>Director.CivilizationTarget.PlayerWealthForStoryteller</c>), the settlement's own appetite for
    /// storage (<c>Building.SettlementConstructionInitiative</c>) and <c>Crafting.Guild</c>. Before this
    /// class, its <i>writers</i> were scenario setup, trade, tribute, quest rewards, raiders taking things
    /// away, and the guild — which consumes from it. <b>Nothing that happened on a map ever became something
    /// the civilization had.</b> A citizen could mine granite, haul it and cut it into blocks, and at
    /// civilization scale the settlement was exactly as poor as before; the shipped <c>MasonsGuild</c> lists
    /// <c>Make_Blocks_Sandstone</c> and starved by construction, because no chunk could ever reach the ledger
    /// it draws from.
    ///
    /// <para/><b>Direction, and only one of them.</b> Map → ledger. The other direction (the ledger supplying
    /// a map — a builder drawing blocks out of the civilization's stock) needs a delivery mechanism this port
    /// has no shape for at all: goods would have to materialise on a map from an abstract count, which is
    /// either a caravan system or a spawn-from-nowhere. Map → ledger needs no new mechanism, because the
    /// goods already exist as Things and the ledger already exists as counts; all that was missing was the
    /// rule for crossing. It is also the half that makes a watched settlement's work matter at civilization
    /// scale, which is the whole point of watching it.
    ///
    /// <para/><b>The invariant, and it is the design.</b> <i>A unit of goods is either a <see cref="Thing"/>
    /// on a settlement's interior map or a count in that settlement's <see cref="Settlement.Stores"/>, never
    /// both.</i> <see cref="BankStoredGoods"/> is the only thing in this codebase that moves a unit across,
    /// and it moves by destroying the map-side stack in the same operation that credits the ledger — it never
    /// copies, never surveys, never estimates. So a chunk cannot be spent twice: trade cannot sell the chunk a
    /// mason is about to cut, because by the time the ledger lists it there is no chunk on the map any more.
    /// The two existing readers that sum both halves — <c>AI.HuntingInitiative.NutritionAvailable</c> and this
    /// class's own nutrition reserve — are correct <i>because</i> the halves are disjoint.
    ///
    /// <para/><b>Why not RimWorld's <c>WealthWatcher</c> shape.</b> RimWorld recomputes colony wealth on a slow
    /// cadence and is explicit that the result is an estimate; <c>PlayerWealthForStoryteller</c> here is the
    /// same kind of question and already answers it from <see cref="Settlement.Stores"/> alone. A survey is the
    /// right shape for a <i>read</i> and the wrong shape for a <i>ledger</i>: summing every Thing on every map
    /// into <c>Stores</c> every tick would be O(things) forever and would double-count everything the ledger
    /// already held the moment trade or a guild touched it. Banking moves instead of summing, so it is O(the
    /// settlement's own stockpile cells) on a rare tick and cannot double-count at all.
    ///
    /// <para/><b>What crosses: goods resting in storage, and nothing else.</b> A unit is banked when it is
    /// sitting in one of the settlement's stockpiles (<c>Building.Zone_Stockpile</c>, which
    /// <c>AI.HaulAIUtility.IsInValidStorage</c> already defines as "stored"), which is the same thing a
    /// civilization means by its stock. Everything lying loose is left alone, so the map's own systems keep
    /// working on it: a chunk beside the stonecutter's table, a meal somebody dropped, the ingredients
    /// <c>Crafting.WorkGiver_DoBill</c> needs within its own search radius of a bench. Two further guards, both
    /// reading rules this codebase already has rather than inventing one:
    /// <list type="bullet">
    /// <item>A stack anybody has <b>reserved</b> is never banked (<c>Map.ReservationManager.IsReserved</c>) —
    /// a citizen who has taken a job on a thing is using it, and that is exactly what a reservation says.</item>
    /// <item>A settlement keeps its <b>larder on the map</b>. Ingestibles are banked only above what
    /// <c>AI.HuntingInitiative.NutritionWanted</c> says the settlement wants in hand, measured against the map
    /// half alone — because the map is where people eat (<c>AI.JobGiver_GetFood</c> sends a hungry pawn to a
    /// Thing), so banking a town's whole larder would starve it while the ledger said it was rich. The reserve
    /// is that class's number, not a second one invented here.</item>
    /// </list>
    ///
    /// <para/><b>The granary, and why this class paints one.</b> Nothing in <c>src/</c> had ever created a
    /// <c>Zone_Stockpile</c> — every one in the repository was made by a test — so <c>AI.WorkGiver_Haul</c>,
    /// which is fully built, listed in content and counted among the wired givers, could never produce a job in
    /// any game: <c>HaulAIUtility.TryFindBestStockpileCell</c> had nowhere to point. That is the same shape of
    /// defect as "nothing ever created a bill" (<c>Crafting.StonecutterInitiative</c>) one system over, and it
    /// is load-bearing here, because a settlement with nowhere to store things has nothing for this class to
    /// bank. So the settlement paints its own granary, in the established "RimWorld asks the player and there
    /// is no player" shape — and its size is not a constant but the settlement's own backlog; see
    /// <see cref="EnsureGranary"/>.
    ///
    /// <para/><b>The tiering has a defined answer for all three states</b> (spec §11.2/§11.3), and none of them
    /// grows a map:
    /// <list type="number">
    /// <item><b>A settlement with a live, watched map.</b> Full-tier citizens stand on it, mine, haul into the
    /// granary; this banks what rests there. The full path.</item>
    /// <item><b>A settlement with a generated but unwatched map.</b> Its citizens have been demoted and
    /// despawned (<c>God.AttentionManager</c>, <c>Settlement.SyncCitizenSpawns</c>), so nobody hauls and
    /// nothing new enters the granary — but whatever is already resting there is still the settlement's, and is
    /// still banked. The pass costs a walk of that settlement's stockpile cells and nothing more.</item>
    /// <item><b>A settlement nobody has ever entered.</b> <see cref="Settlement.InteriorMap"/> is null and this
    /// is a no-op — never a triggered generation, exactly as <c>SettlementConstructionInitiative</c> and
    /// <c>StonecutterInitiative</c> read it. Its ledger is its entire stock, which is precisely what
    /// <c>Crafting.Guild</c> was designed for: "this is what happens to the other ninety-nine towns".</item>
    /// </list>
    ///
    /// <para/><b>No state of its own.</b> Every decision is re-derived from the map and the ledger on each
    /// gated pass, so there is nothing here to Scribe. The granary persists because zones already save with the
    /// map they belong to (<c>Building.ZoneManager.ExposeData</c>), and the ledger persists because
    /// <see cref="Settlement.ExposeData"/> already writes it.
    /// </summary>
    public static class SettlementStockInitiative
    {
        // -----------------------------------------------------------------------------------------------
        // Entry points.
        // -----------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c> and
        /// cheaply short-circuited by the gate below on every tick but the rare one it fires. A silent no-op
        /// with no world running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements) TickSettlement(settlement);
        }

        /// <summary>One settlement, reading its own <see cref="Settlement.InteriorMap"/>. Null (never entered)
        /// is tiering state 3 above — a real, expected answer, not an error.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            if (Find.TickManager.TicksGame % SettlementStockTuning.IntervalTicks != 0) return;
            Run(settlement, map);
        }

        /// <summary>The ungated pass. Public so a test (or a future caller entering a settlement scope) can
        /// drive one without arranging for the tick number to land on the interval.</summary>
        public static int Run(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            EnsureGranary(settlement, map);
            return BankStoredGoods(settlement, map);
        }

        // -----------------------------------------------------------------------------------------------
        // The seam itself.
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Moves every bankable unit resting in <paramref name="map"/>'s stockpiles into
        /// <paramref name="settlement"/>'s ledger and returns how many units crossed. <b>Moves</b>: the
        /// map-side stack is reduced (and destroyed when it empties) by exactly what the ledger is credited, in
        /// the same step, which is what makes the class invariant true rather than merely intended.
        /// </summary>
        public static int BankStoredGoods(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            // A map with no storage on it has nothing to bank, and the reserve below costs a walk of every
            // item on the map — so settle that first: a settlement that has not painted a granary yet (and
            // most, most of the time, have nothing to put in one) pays only this.
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            if (!HasStockpile(zones)) return 0;

            // One reserve figure for the whole pass, computed against the map half alone (see the class doc:
            // measuring it against the union would let the ledger's own growth authorise emptying the larder).
            float nutritionSurplus = HuntingInitiative.NutritionAvailable(null, map)
                - HuntingInitiative.NutritionWanted(settlement, map);

            int banked = 0;
            for (int z = 0; z < zones.Count; z++)
            {
                if (!(zones[z] is Zone_Stockpile stockpile)) continue;
                IReadOnlyList<IntVec3> cells = stockpile.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    Thing? stack = HaulAIUtility.ExistingStackAt(map, cells[c]);
                    if (stack == null || !stack.Spawned || stack.stackCount <= 0) continue;
                    if (!stockpile.filter.Allows(stack.def)) continue;
                    if (map.reservationManager.IsReserved(stack)) continue;

                    int take = BankableUnits(stack, ref nutritionSurplus);
                    if (take <= 0) continue;

                    // Credit and remove in one step. Nothing between these two lines may observe the unit in
                    // both places, which is the whole no-double-counting argument.
                    settlement.AddStore(stack.def, take);
                    stack.stackCount -= take;
                    if (stack.stackCount <= 0) stack.Destroy();
                    banked += take;
                }
            }
            return banked;
        }

        private static bool HasStockpile(IReadOnlyList<Zone> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Stockpile pile && pile.CellCount > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// How much of <paramref name="stack"/> the settlement is willing to let leave the map. Everything, for
        /// anything that is not food; for food, only what is above the larder the settlement wants in hand —
        /// drawn down from <paramref name="nutritionSurplus"/> as it is spent, so one pass cannot bank the same
        /// surplus twice across several stacks.
        /// </summary>
        private static int BankableUnits(Thing stack, ref float nutritionSurplus)
        {
            ThingDef def = stack.def;
            if (!def.IsNutritionGivingIngestible) return stack.stackCount;

            float perUnit = def.ingestible!.nutrition;
            if (perUnit <= 0f) return stack.stackCount;
            if (nutritionSurplus < perUnit) return 0;

            int affordable = (int)Math.Floor(nutritionSurplus / perUnit);
            int take = Math.Min(affordable, stack.stackCount);
            nutritionSurplus -= take * perUnit;
            return take;
        }

        // -----------------------------------------------------------------------------------------------
        // The granary.
        // -----------------------------------------------------------------------------------------------

        /// <summary>The settlement's own stockpile on this map, or null before it has painted one.</summary>
        public static Zone_Stockpile? GranaryOf(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Stockpile pile && pile.label == SettlementStockTuning.GranaryLabel) return pile;
            }
            return null;
        }

        /// <summary>
        /// Grows the settlement's granary to meet its own backlog, and returns how many cells it added.
        ///
        /// <para/><b>There is no target size.</b> The settlement wants one cell for every stack of goods that
        /// is lying out with nowhere to go — <see cref="HomelessStacks"/> minus the empty stockpile cells that
        /// could already take one. That is derived entirely from live state and invents no capacity constant;
        /// it is self-limiting, because banking drains the granary and hauling fills it, so the steady size
        /// tracks the <i>rate</i> a settlement produces goods at, never the total it has ever produced. A
        /// settlement with nothing lying around never paints a cell.
        ///
        /// <para/><b>Where.</b> Around the goods, exactly as <c>Crafting.StonecutterInitiative</c> puts the
        /// bench where the stone is and for the same reason (a store nobody can reach is a store nobody uses):
        /// the first pass seeds the granary at the first homeless stack, and later passes grow outward from
        /// that seed through <see cref="GenRadial.RadialPattern"/> — nearest-first, deterministic, no draw on
        /// the shared <see cref="Rand"/> stream for a decision that has a perfectly good ordered answer.
        ///
        /// <para/><b>What it accepts.</b> Everything. The granary is not where a settlement decides what is
        /// worth keeping — <see cref="BankStoredGoods"/> is, and it is the one that knows about larders and
        /// reservations. A narrow filter here would only make goods pile up outside a store that had room.
        /// </summary>
        public static int EnsureGranary(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            List<Thing> homeless = HomelessStacks(map);
            if (homeless.Count == 0) return 0;

            int wanted = Math.Min(
                homeless.Count - EmptyStockpileCells(map),
                SettlementStockTuning.MaxGranaryCellsPerPass);
            if (wanted <= 0) return 0;

            Zone_Stockpile granary = GranaryOf(map) ?? NewGranary(map);
            IntVec3 seed = granary.CellCount > 0 ? granary.Cells[0] : homeless[0].Position;

            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < wanted; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue; // one zone per cell (ZoneManager's own rule)
                if (map.zoneManager.AddCell(granary, candidate)) added++;
            }
            return added;
        }

        private static Zone_Stockpile NewGranary(Map.Map map)
        {
            var granary = new Zone_Stockpile { label = SettlementStockTuning.GranaryLabel };
            map.zoneManager.RegisterZone(granary);
            granary.filter.SetAllowAll(null);
            return granary;
        }

        /// <summary>Every spawned haulable stack on <paramref name="map"/> that is not already resting in
        /// storage — the settlement's backlog, and the only thing that sizes its granary. Ordered by
        /// <c>ListerThings</c>' own stable order, so the seed cell two identical runs pick is the same
        /// cell.</summary>
        public static List<Thing> HomelessStacks(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var found = new List<Thing>();
            IReadOnlyList<Thing> haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < haulables.Count; i++)
            {
                Thing t = haulables[i];
                if (!t.Spawned || t.stackCount <= 0) continue;
                if (HaulAIUtility.IsInValidStorage(t)) continue;
                found.Add(t);
            }
            return found;
        }

        /// <summary>Stockpile cells on <paramref name="map"/> with no item on them — storage the settlement
        /// already has and has not filled, which is the half of the backlog it does not need to paint over
        /// again.</summary>
        public static int EmptyStockpileCells(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int free = 0;
            IReadOnlyList<Zone> zones = map.zoneManager.AllZones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (!(zones[z] is Zone_Stockpile pile)) continue;
                IReadOnlyList<IntVec3> cells = pile.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (HaulAIUtility.ExistingStackAt(map, cells[c]) == null) free++;
                }
            }
            return free;
        }
    }
}
