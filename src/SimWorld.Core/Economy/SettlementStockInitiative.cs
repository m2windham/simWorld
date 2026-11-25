using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
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
    /// working on it: a chunk beside the stonecutter's table, a meal somebody dropped, the
    /// ingredients a crafter has put down beside a bench. Three further guards,
    /// each reading something this codebase already has rather than inventing it:
    /// <list type="bullet">
    /// <item>A stack anybody has <b>reserved</b> is never banked (<c>Map.ReservationManager.IsReserved</c>) —
    /// a citizen who has taken a job on a thing is using it, and that is exactly what a reservation says.</item>
    /// <item>A settlement keeps its <b>larder on the map</b>. Ingestibles are banked only above what
    /// <c>AI.HuntingInitiative.NutritionWanted</c> says the settlement wants in hand, measured against the map
    /// half alone — because the map is where people eat (<c>AI.JobGiver_GetFood</c> sends a hungry pawn to a
    /// Thing), so banking a town's whole larder would starve it while the ledger said it was rich. The reserve
    /// is that class's number, not a second one invented here.</item>
    /// <item>A thing that <b>holds a pawn</b> — a body — is never banked, whatever the stockpile under it
    /// allows (<see cref="HoldsPawn"/>). Banking moves by destroying, and destroying a <c>Things.Corpse</c>
    /// destroys the person inside it: the one copy of a dead citizen the game still keeps once the roster has
    /// let them go. A settlement's dead are its people, not its stock. The granary refusing bodies
    /// (<see cref="EnsureGranary"/>) keeps them out of storage in the first place; this holds even where a
    /// player has painted a stockpile to take them.</item>
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
            int banked = BankStoredGoods(settlement, map);

            // Banking keeps NutritionWanted on the map and takes the surplus; issuing puts back the shortfall
            // against the same threshold. Run in this order the two compose into one statement — the map ends
            // a pass holding what the settlement wants in hand, and neither can undo the other, because
            // banking only ever takes what is above the line and issuing only ever adds up to it.
            IssueFromStores(settlement, map);
            IssueMedicineFromStores(settlement, map);
            // And what a standing bill on the map is short of; banking already leaves bill ingredients alone, so
            // this cannot undo it either.
            SettlementBillStock.IssueBillIngredientsFromStores(settlement, map);

            return banked;
        }

        /// <summary>
        /// The crossing this class was missing: <b>ledger to map</b>. Moves stored food out of
        /// <paramref name="settlement"/>'s ledger and onto <paramref name="map"/> until the map holds what
        /// <c>AI.HuntingInitiative.NutritionWanted</c> says the settlement wants in hand. Returns the units
        /// issued.
        ///
        /// <para/><b>Why it has to exist.</b> This class's own invariant is that a unit is either a
        /// <c>Thing</c> on a map or a count in <see cref="Settlement.Stores"/>, never both, and
        /// <see cref="BankStoredGoods"/> was the only thing that moved one across — in one direction. The
        /// class doc said as much: "all that was missing was the rule for crossing". Half of it was written.
        ///
        /// <para/><b>What the missing half cost.</b> <c>SettlementLarder.WouldEatFromStores</c> refuses any
        /// <c>Spawned</c> citizen, on the correct principle that a citizen standing on a map eats things on
        /// that map. But <c>SettlementLarder.ProvisionFoundingBand</c> sizes a new settlement's rations by
        /// counting every non-dead citizen, spawned ones included, and puts all of it in the ledger — and no
        /// map-generation step places any food at all. So a watched settlement's people were locked out of
        /// rations stocked on their behalf, and fell back to harvesting wild berries through a work job that
        /// sits below the need tiers in the think tree. Measured by <c>tools/bench --suite probe</c> on day
        /// one of a 25-strong TribalStart: watched citizens at 0.20 food against unwatched at 0.73, with the
        /// watched settlement's ledger <i>fuller</i> by exactly the 40 nutrition the unwatched ones had eaten.
        /// A settlement accumulating food its citizens cannot reach, and the gap widening daily.
        ///
        /// <para/><b>Why not simply let a spawned citizen eat from the ledger.</b> That was the one-line fix
        /// and it is the wrong one: it teleports food into a stomach, skips the walking, the hauling and the
        /// cooking, and makes Full tier a more expensive way to get the same answer as the abstraction. The
        /// point of attending a settlement is that its economy becomes real. So the rations become real too,
        /// and somebody still has to go and eat them.
        ///
        /// <para/><b>Bounded by the deficit, not by the ledger.</b> Opening a settlement a century in finds a
        /// ledger holding thousands of units; this issues against the shortfall, which is a few days of food
        /// for the mouths present, so a large ledger cannot dump itself onto the map. What it cannot place —
        /// a map with nowhere standable left — it leaves in the ledger rather than destroying.
        ///
        /// <para/><b>Deterministic.</b> <c>SettlementLarder.BestStoredFood</c> is a total order with no draw
        /// in it, and cells are walked in <c>GenRadial</c>'s fixed pattern, so nothing here touches the shared
        /// <see cref="Rand"/> stream and two identically-seeded runs issue identically. The probe depends on
        /// that.
        /// </summary>
        public static int IssueFromStores(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            float deficit = HuntingInitiative.NutritionWanted(settlement, map)
                - HuntingInitiative.NutritionAvailable(null, map);
            if (deficit <= 0f) return 0;

            int issued = 0;
            while (deficit > 0f)
            {
                ThingDef? def = SettlementLarder.BestStoredFood(settlement);
                if (def == null) break;

                float per = def.ingestible?.nutrition ?? 0f;
                if (per <= 0f) break;

                int have = settlement.StoreCountOf(def);
                if (have <= 0) break;

                int take = Math.Min((int)Math.Ceiling(deficit / per), have);
                if (take <= 0) break;

                int placed = PlaceIssued(def, take, map);
                if (placed <= 0) break;

                // Debit in the same step that spawns, exactly as banking credits in the same step that
                // destroys. That is what makes the invariant true rather than merely intended.
                settlement.AddStore(def, -placed);
                deficit -= placed * per;
                issued += placed;

                // Short of what was asked for means the map ran out of room, not that another def would fare
                // better; trying the next one would spin.
                if (placed < take) break;
            }

            return issued;
        }

        /// <summary>
        /// The same crossing for <b>medicine</b>: everything the ledger holds goes onto the map, and
        /// <see cref="BankableUnits"/> never takes it back. Returns the units issued.
        ///
        /// <para/><b>The broken link.</b> A doctor looks for medicine among the items on the map
        /// (<c>Health.MedicineUtility.FindBestMedicine</c>, as RimWorld's does), and the founding band's
        /// herbal medicine was put in the ledger (<c>Sim.Game.NewGame</c>) with nothing to carry it across —
        /// only food was ever issued. Traced over twenty days of a threat-free band of twenty
        /// (<c>docs/perf/storyteller-pressure</c>): medicine on the map 0 and in the ledger 20 on every one
        /// of them, while thirteen people died of infections tended bare-handed. Had a unit been hauled into
        /// storage it would have been banked straight back.
        ///
        /// <para/><b>All of it, not a reserve.</b> Food is issued against a figure the settlement already
        /// has (<c>NutritionWanted</c>). There is no such figure for medicine and RimWorld offers none — a
        /// colony's medicine is simply on its map — so this invents no threshold: a settlement with anyone
        /// alive keeps its medicine where a doctor can reach it. That means a watched settlement's medicine
        /// stops being visible to the ledger's readers (trade, tribute, raid loot); the count is small and
        /// the alternative is a number nobody can source.
        ///
        /// <para/>Deterministic for the same reason <see cref="IssueFromStores"/> is: defs are taken in
        /// defName order and cells in <c>GenRadial</c>'s fixed pattern, with no draw.
        /// </summary>
        public static int IssueMedicineFromStores(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));

            bool anyoneAlive = false;
            IReadOnlyList<Pawns.Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count && !anyoneAlive; i++) anyoneAlive = !citizens[i].Dead;
            if (!anyoneAlive) return 0;

            var medicines = new List<ThingDef>();
            foreach (KeyValuePair<ThingDef, int> held in settlement.Stores)
            {
                if (held.Value > 0 && held.Key.IsMedicine) medicines.Add(held.Key);
            }
            medicines.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));

            int issued = 0;
            for (int i = 0; i < medicines.Count; i++)
            {
                int placed = PlaceIssued(medicines[i], settlement.StoreCountOf(medicines[i]), map);
                if (placed <= 0) continue;
                settlement.AddStore(medicines[i], -placed);
                issued += placed;
            }
            return issued;
        }

        /// <summary>Spawns up to <paramref name="count"/> units of <paramref name="def"/> around the granary
        /// (or the map's middle when there is not one yet), one stack per cell and never above the def's own
        /// stack limit. Returns how many units actually landed. Only on cells the band can walk to
        /// (<see cref="SettlementReach"/>): food issued into a sealed rock pocket was counted as on the map,
        /// so nothing more was issued, while nobody could reach it to eat.</summary>
        private static int PlaceIssued(ThingDef def, int count, Map.Map map)
        {
            IntVec3? band = SettlementReach.BandCell(map);
            IntVec3 anchor = IssueAnchor(map, band);
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;

            int remaining = count;
            int placed = 0;
            for (int i = 0; i < pattern.Count && remaining > 0; i++)
            {
                IntVec3 cell = anchor + pattern[i];
                if (!GenGrid.InBounds(cell, map) || !GenGrid.Standable(cell, map)) continue;
                if (!SettlementReach.CanReach(map, band, cell)) continue;

                int stack = Math.Min(remaining, Math.Max(1, def.stackLimit));
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = stack;
                GenSpawn.Spawn(thing, cell, map);

                remaining -= stack;
                placed += stack;
            }

            return placed;
        }

        /// <summary>Where issued food lands: the granary if the settlement has painted one, otherwise the
        /// middle of the map — or, when the band stands where it cannot reach either, beside the band. Left loose rather than zoned — <see cref="EnsureGranary"/> is what decides where
        /// a settlement's storage goes, and a hungry citizen reaches a loose stack exactly as well
        /// (<c>AI.JobGiver_GetFood</c> scans items, not stockpiles).</summary>
        private static IntVec3 IssueAnchor(Map.Map map, IntVec3? band)
        {
            Zone_Stockpile? granary = GranaryOf(map);
            if (granary != null && granary.CellCount > 0 && SettlementReach.CanReach(map, band, granary.Cells[0]))
            {
                return granary.Cells[0];
            }
            var middle = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            return SettlementReach.CanReach(map, band, middle) ? middle : band!.Value;
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

            // What a standing bill on this map would take stays where its crafter can reach it, as medicine
            // does: banked, cloth for a tailor's bill or chunks for a stonecutter's would leave the map, and
            // the bill could never be worked. Food keeps its own rule, the nutrition reserve below.
            List<RecipeDef> billed = StandingBillRecipes(map);
            // And what a blueprint or frame on this map still needs, for the same reason: banked, the wood for a
            // planned house leaves the map and the ledger cannot hand it back, so the house waits on felling for
            // timber the settlement already cut. In RimWorld a stockpiled log stays where a builder can fetch it.
            HashSet<ThingDef> construction = ConstructionMaterialsWanted(map);
            // And what a lit hearth on this map burns. Banked, the logs that should have fed a household's fire
            // in winter left the map, and the fire burned out with its switch still on: every hearth of the
            // exposure arcs stood lit and empty while the baby beside it froze. RimWorld's colonists refuel a
            // campfire from the stockpile; here that wood has to stay in it while a fire wants it.
            HashSet<ThingDef> fuel = FuelWanted(map);

            int banked = 0;
            for (int z = 0; z < zones.Count; z++)
            {
                if (!(zones[z] is Zone_Stockpile stockpile)) continue;
                IReadOnlyList<IntVec3> cells = stockpile.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    Thing? stack = HaulAIUtility.ExistingStackAt(map, cells[c]);
                    if (stack == null || !stack.Spawned || stack.stackCount <= 0) continue;
                    if (HoldsPawn(stack)) continue;
                    if (!stockpile.filter.Allows(stack.def)) continue;
                    if (map.reservationManager.IsReserved(stack)) continue;
                    if (!stack.def.IsNutritionGivingIngestible && AnyTakes(billed, stack.def)) continue;
                    // And what such a bill makes to be worn or wielded: a crafted bow or coat is hauled to
                    // storage before anyone takes it up, and banked there it leaves the map, so nobody is ever
                    // armed, the bill never sees its count reached, and it eats material without end.
                    if ((stack.def.IsWeapon || stack.def.IsApparel) && AnyMakes(billed, stack.def)) continue;
                    if (construction.Contains(stack.def)) continue;
                    if (fuel.Contains(stack.def)) continue;

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

        /// <summary>
        /// Whether <paramref name="thing"/> holds a pawn, and so must never be banked: banking destroys what it
        /// moves, and a <see cref="Corpse"/> takes its <see cref="Corpse.InnerPawn"/> with it when destroyed
        /// (<see cref="Corpse.Destroy"/>). Asked of the instance, not the def or the stockpile's filter, so it
        /// holds wherever a body comes to rest — including a stockpile a player has told to take bodies.
        ///
        /// <para/><b>Why it reads <see cref="Corpse"/> rather than a holder interface.</b> RimWorld asks this
        /// through <c>IThingHolder</c>/<c>ThingOwner</c>. This port has no container layer: nothing holds a Thing
        /// inside another Thing (<see cref="Corpse"/>'s own doc), and the corpse's
        /// <see cref="Corpse.InnerPawn"/>/<c>Pawn.corpse</c> pair is the only holding relation in the codebase.
        /// An interface with one implementer would be a guess at the shape of a layer nobody has built. So this
        /// is the one place that knows; when something else comes to hold a pawn (a grave, a casket), this is
        /// what grows to ask it.
        /// </summary>
        private static bool HoldsPawn(Thing thing) => thing is Corpse corpse && corpse.InnerPawn != null;

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
        /// <summary>Every fuel a refuelable building on <paramref name="map"/> burns while it is on or asked to
        /// be (<see cref="CompFlickable"/>): a hearth switched off for the summer holds nothing back.</summary>
        private static HashSet<ThingDef> FuelWanted(Map.Map map)
        {
            var wanted = new HashSet<ThingDef>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i] as ThingWithComps;
                CompRefuelable? refuelable = building?.GetComp<CompRefuelable>();
                if (refuelable == null) continue;
                CompFlickable? flick = building!.GetComp<CompFlickable>();
                if (flick != null && !flick.SwitchIsOn && !flick.WantSwitchOn) continue;
                foreach (ThingDef def in refuelable.Props.fuelFilter.AllowedThingDefs) wanted.Add(def);
            }
            return wanted;
        }

        /// <summary>Every material a <see cref="Blueprint"/> on <paramref name="map"/> will need or a
        /// <see cref="Frame"/> on it still lacks (<see cref="Frame.MaterialStillNeeded"/>).</summary>
        private static HashSet<ThingDef> ConstructionMaterialsWanted(Map.Map map)
        {
            var wanted = new HashSet<ThingDef>();
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (!(blueprints[i] is Blueprint bp)) continue;
                foreach (ThingDefCountClass entry in bp.TotalMaterialCost()) wanted.Add(entry.thingDef);
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (!(frames[i] is Frame frame)) continue;
                foreach (ThingDefCountClass entry in frame.TotalMaterialCost())
                {
                    if (frame.MaterialStillNeeded(entry.thingDef) > 0) wanted.Add(entry.thingDef);
                }
            }
            return wanted;
        }

        /// <summary>The recipe of every bill standing on a bench on <paramref name="map"/> that is not
        /// suspended.</summary>
        private static List<RecipeDef> StandingBillRecipes(Map.Map map)
        {
            var recipes = new List<RecipeDef>();
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                if (!buildings[b].Spawned || (buildings[b] as ThingWithComps)?.GetComp<CompBillGiver>() is not CompBillGiver comp) continue;
                IReadOnlyList<Bill> bills = comp.BillStack.Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    if (!bills[i].suspended && bills[i].recipe != null) recipes.Add(bills[i].recipe);
                }
            }
            return recipes;
        }

        private static bool AnyTakes(List<RecipeDef> recipes, ThingDef def)
        {
            for (int r = 0; r < recipes.Count; r++)
            {
                List<IngredientCount>? ingredients = recipes[r].ingredients;
                if (ingredients == null) continue;
                for (int i = 0; i < ingredients.Count; i++)
                {
                    if (ingredients[i].filter.Allows(def)) return true;
                }
            }
            return false;
        }

        private static bool AnyMakes(List<RecipeDef> recipes, ThingDef def)
        {
            for (int r = 0; r < recipes.Count; r++)
            {
                if (recipes[r].ProducedThingDef == def) return true;
            }
            return false;
        }

        private static int BankableUnits(Thing stack, ref float nutritionSurplus)
        {
            ThingDef def = stack.def;
            // Medicine stays where a doctor can pick it up; see IssueMedicineFromStores.
            if (def.IsMedicine) return 0;
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
        /// <para/><b>Where.</b> Beside the settlement's compound when it has one: the first pass seeds the
        /// granary at the open cell nearest the compound's door outside the ground the village keeps, and growth
        /// never paints that ground. A settlement without a compound puts it around the goods, exactly as
        /// <c>Crafting.StonecutterInitiative</c> puts the bench where the stone is and for the same reason (a
        /// store nobody can reach is a store nobody uses), seeded at the first homeless stack. Later passes grow
        /// outward from that seed through <see cref="GenRadial.RadialPattern"/> — nearest-first, deterministic, no draw on
        /// the shared <see cref="Rand"/> stream for a decision that has a perfectly good ordered answer.
        ///
        /// <para/><b>What it accepts.</b> What any new stockpile accepts: RimWorld's
        /// <see cref="StorageSettingsPreset.DefaultStockpile"/> — every kind of goods, and no bodies. The granary
        /// is not where a settlement decides which goods are worth keeping — <see cref="BankStoredGoods"/> is,
        /// and it is the one that knows about larders and reservations; a narrower filter would only make goods
        /// pile up outside a store that had room. But a body is not goods. It used to be painted with
        /// <c>SetAllowAll</c>, which since corpse defs are minted at load meant every granary in every game took
        /// the settlement's dead, and banking then destroyed them (see <see cref="HoldsPawn"/>). RimWorld's
        /// ordinary stockpile has never taken a body: they go to a dumping stockpile or a grave.
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

            SettlementCompound? compound = map.settlementCompound;
            // A store the band cannot walk to is no store: on a map broken into sealed rock pockets the compound
            // can be planned in one pocket and the band founded in another, so the granary is drawn only on
            // ground a citizen of this settlement can reach.
            Pawn? walker = null;
            foreach (Pawn citizen in settlement.Citizens)
            {
                if (citizen.Spawned && citizen.Map == map) { walker = citizen; break; }
            }
            IntVec3 seed;
            if (GranaryOf(map) is Zone_Stockpile existing && existing.CellCount > 0) seed = existing.Cells[0];
            else if (compound == null) seed = homeless[0].Position;
            else if (!TryFindGranaryCell(map, compound.DoorCell, compound, walker, out seed)) return 0;

            Zone_Stockpile granary = GranaryOf(map) ?? NewGranary(map);
            int added = 0;
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count && added < wanted; i++)
            {
                IntVec3 candidate = seed + pattern[i];
                if (!CanBeGranary(map, candidate, compound, walker)) continue;
                if (map.zoneManager.AddCell(granary, candidate)) added++;
            }
            return added;
        }

        /// <summary>The nearest cell to <paramref name="origin"/> a granary could take: where a settlement with a
        /// compound seeds its store, beside the village rather than at whatever loose stack the lister names first
        /// (on the host's seed that was a hundred cells off, and the store's home-area margin then took in a ruin).</summary>
        private static bool TryFindGranaryCell(Map.Map map, IntVec3 origin, SettlementCompound compound, Pawn? walker, out IntVec3 cell)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                cell = origin + pattern[i];
                if (CanBeGranary(map, cell, compound, walker)) return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>Open, unzoned ground (one zone per cell, ZoneManager's own rule) that the compound does not
        /// keep for its walls, street, yard or plots, and that <paramref name="walker"/> (a citizen, when one is on
        /// the map) can walk to. Never water: shallow water is standable, and RimWorld lets a player put a
        /// stockpile there, but it rots what lies in it (<c>TerrainDef.extraDeteriorationFactor</c>), so a
        /// settlement choosing its own store keeps it dry.</summary>
        private static bool CanBeGranary(Map.Map map, IntVec3 c, SettlementCompound? compound, Pawn? walker) =>
            GenGrid.InBounds(c, map) && GenGrid.Standable(c, map) && !map.terrainGrid.TerrainAt(c).IsWater &&
            map.zoneManager.ZoneAt(c) == null &&
            (compound == null || !compound.Reserves(CellRect.SingleCell(c))) &&
            (walker == null || Reachability.CanReach(walker, c, PathEndMode.OnCell));

        private static Zone_Stockpile NewGranary(Map.Map map)
        {
            var granary = new Zone_Stockpile { label = SettlementStockTuning.GranaryLabel };
            map.zoneManager.RegisterZone(granary);
            granary.filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            return granary;
        }

        /// <summary>
        /// Every spawned haulable stack on <paramref name="map"/> that is not already resting in storage and
        /// that the granary would take — the settlement's backlog, and the only thing that sizes its granary.
        /// Ordered by <c>ListerThings</c>' own stable order, so the seed cell two identical runs pick is the same
        /// cell.
        ///
        /// <para/><b>Only what the granary would take.</b> Read against the granary's own filter, or the one a
        /// new granary would be painted with. A body lying where it fell is not backlog for a store that refuses
        /// it: counted, it would grow the granary by a cell nothing can fill for as long as the body lies there,
        /// and — were it the first loose thing on the map — seed the granary on top of the dead.
        /// </summary>
        public static List<Thing> HomelessStacks(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Crafting.ThingFilter granaryTakes = GranaryOf(map)?.filter ?? NewGranaryFilter();

            var found = new List<Thing>();
            IReadOnlyList<Thing> haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < haulables.Count; i++)
            {
                Thing t = haulables[i];
                if (!t.Spawned || t.stackCount <= 0) continue;
                if (HaulAIUtility.IsInValidStorage(t)) continue;
                if (!granaryTakes.Allows(t.def)) continue;
                found.Add(t);
            }
            return found;
        }

        private static Crafting.ThingFilter NewGranaryFilter()
        {
            var filter = new Crafting.ThingFilter();
            filter.SetFromPreset(StorageSettingsPreset.DefaultStockpile);
            return filter;
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
