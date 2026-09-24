using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreWorld = SimWorld.World.World;
using CorpseThing = SimWorld.Things.Corpse;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;
using ThingFilter = global::SimWorld.Crafting.ThingFilter;
using Zone_Stockpile = global::SimWorld.Building.Zone_Stockpile;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>A dead citizen is not goods.</b> A citizen died, somebody carried the body to the granary, and
    /// <c>SettlementStockInitiative.BankStoredGoods</c> turned it into a count in the settlement's ledger and
    /// destroyed it — the <c>Corpse</c> and, through <c>Corpse.Destroy</c>, the person inside it. SimWorld is one
    /// settlement; its people are the whole cast, and losing one must never read as inventory.
    ///
    /// <para/>Two defences, each pinned on its own so that either can be seen to fail without the other:
    /// <list type="number">
    /// <item><b>The filter.</b> A new stockpile — the settlement's own granary, or one the player marks — starts
    /// with RimWorld's <c>DefaultStockpile</c> settings, which have never taken corpses (those go to a dumping
    /// stockpile or a grave). Before this the granary was painted with <c>SetAllowAll</c>, and since corpse
    /// defs are minted at load (#81) that meant every game's granary took bodies.</item>
    /// <item><b>The banking guard.</b> A thing holding a pawn is never banked, whatever the stockpile under it
    /// allows — because the filter is one line of defence and a player can repaint a stockpile to take
    /// anything.</item>
    /// </list>
    /// </summary>
    public class SettlementDeadTests : ContentTestBase
    {
        public SettlementDeadTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Chunk => Def("ChunkSandstone");

        private static ThingDef Wood => Def("WoodLog");

        private static ThingDef Meal => Def("MealSimple");

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Settlement PeopledSettlement(int citizens = 3)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Restinghome", 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            return settlement;
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, ThingDef def, int count)
        {
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>A stockpile somebody has repainted to take anything at all — bodies included. The case the
        /// banking guard exists for: no default filter can close it, because it is the player's own say-so.</summary>
        private static Zone_Stockpile StockpileTakingAnything(CoreMap map, params IntVec3[] cells)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in cells) map.zoneManager.AddCell(zone, c);
            zone.filter.SetAllowAll(null);
            return zone;
        }

        /// <summary>Somebody who lived and died on <paramref name="cell"/>: the body their death left there.</summary>
        private static CorpseThing BodyAt(CoreMap map, IntVec3 cell, ThingDef race, string name)
        {
            var pawn = new Pawn(race, name);
            GenSpawn.Spawn(pawn, cell, map);
            pawn.health.Kill(null, null);
            Assert.NotNull(pawn.corpse);
            return pawn.corpse!;
        }

        private static List<KeyValuePair<string, int>> Ledger(Settlement settlement) =>
            settlement.Stores
                .Where(kv => kv.Value != 0)
                .Select(kv => new KeyValuePair<string, int>(kv.Key.defName, kv.Value))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        /// <summary>A game with the player's settlement open — attention and a generated interior — the same
        /// two steps <c>MapCommandsTests</c> and <c>CitizenViewTests</c> take, and the only state in which
        /// <see cref="MapCommands"/> can reach a map at all.</summary>
        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            Assert.NotNull(settlement.InteriorMap);
            return settlement;
        }

        private static Pawn AnyCitizenOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.First(p => p.Spawned && p.Map == map);

        /// <summary>A standable, unzoned cell with nothing built on it, nearest-first from
        /// <paramref name="near"/>.</summary>
        private static IntVec3 FreeCellNear(CoreMap map, IntVec3 near)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = near + pattern[i];
                if (!GenGrid.InBounds(candidate, map) || !GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue;
                if (map.edificeGrid[candidate] != null) continue;
                if (HaulAIUtility.ExistingStackAt(map, candidate) != null) continue;
                return candidate;
            }
            throw new InvalidOperationException("test setup: no free cell near " + near);
        }

        private static void Kill(Settlement settlement, Pawn victim) =>
            Find.FamilyManager.HandleDeath(
                victim, DeathCause.Injury, settlement.Citizens.ToDictionary(p => p.thingIDNumber));

        // -------------------------------------------------------------------------------------------
        // End to end: what happens to a dead citizen's body, through the game's own loop.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// The defect as the player met it, driven by nothing but the tick loop: a citizen dies on an open map,
        /// and the settlement's own initiatives — granary painting, hauling, banking — run unprompted. Before the
        /// fix, on this seed, the hauler carried the body into the granary at tick 1,373 and the next banking
        /// pass (tick 1,500) destroyed it, person and all, and wrote <c>Corpse_Human: 1</c> into the ledger.
        /// Now nobody files it: it is not in any store, the ledger never lists it, and the person is still
        /// there to open.
        ///
        /// <para/><b>Why one citizen hauls and nothing else.</b> Hauling is the lowest natural priority, and on a
        /// fresh map everybody has harvesting, sowing and building to do first — measured, not one of nineteen
        /// citizens reached the body in 30,000 ticks with the default work grid, though every one of them could
        /// have. A player who wants their stores kept assigns a hauler, and that is what makes this a test-length
        /// run rather than one that waits for the harvest to be in.
        /// </summary>
        [Fact]
        public void A_citizen_who_dies_is_not_carried_off_and_filed_under_goods()
        {
            Settlement settlement = OpenedSettlement("dead-b");
            CoreMap map = settlement.InteriorMap!;
            Pawn victim = AnyCitizenOn(settlement, map);
            int id = victim.thingIDNumber;

            Pawn hauler = settlement.Citizens.First(p => p.Spawned && p.Map == map && p != victim);
            foreach (global::SimWorld.Work.WorkTypeDef workType in DefDatabase<global::SimWorld.Work.WorkTypeDef>.AllDefsListForReading)
            {
                hauler.workSettings.SetPriority(workType, workType.defName == "Hauling" ? 1 : 0);
            }

            Kill(settlement, victim);
            CorpseThing body = victim.corpse!;
            Assert.NotNull(body);

            RunTicks(6000);

            // Not vacuous: the settlement has a granary its hauler can walk into, so the only thing keeping the
            // body out of it is that it refuses bodies.
            Zone_Stockpile? granary = SettlementStockInitiative.GranaryOf(map);
            Assert.NotNull(granary);
            Assert.Contains(granary!.Cells, c => Reachability.CanReach(hauler, c, PathEndMode.OnCell));

            Assert.False(victim.Destroyed, "The person inside the body was destroyed.");
            Assert.False(body.Destroyed, "The body was destroyed.");
            Assert.Same(body, victim.corpse);
            Assert.False(
                HaulAIUtility.IsInValidStorage(body),
                "The body is resting in a stockpile as stored goods.");
            Assert.DoesNotContain(settlement.Stores.Keys, CorpseDefGenerator.IsCorpseDef);
            Assert.NotNull(GodViewSnapshot.Citizen(id));
        }

        // -------------------------------------------------------------------------------------------
        // Defence 1: a new stockpile does not take bodies.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlements_own_granary_does_not_take_a_body()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            SpawnStack(map, new IntVec3(3, 0, 3), Chunk, 5);
            SettlementStockInitiative.EnsureGranary(settlement, map);
            Zone_Stockpile granary = SettlementStockInitiative.GranaryOf(map)!;
            Assert.NotNull(granary);

            CorpseThing body = BodyAt(map, new IntVec3(15, 0, 15), Human, "Ada");
            var hauler = NewHuman("Hauler");
            GenSpawn.Spawn(hauler, new IntVec3(10, 0, 10), map);

            Assert.False(granary.filter.Allows(body.def), "The settlement's granary accepts " + body.def.defName + ".");
            Assert.False(
                HaulAIUtility.TryFindBestStockpileCell(hauler, body, out _),
                "A hauler found a place in the granary for a body.");

            // Still open to goods: the refusal is narrow.
            Thing looseChunk = SpawnStack(map, new IntVec3(16, 0, 3), Chunk, 3);
            Assert.True(HaulAIUtility.TryFindBestStockpileCell(hauler, looseChunk, out _));
        }

        /// <summary>
        /// The default refuses bodies and nothing else: set against "allow everything", the only defs that
        /// differ are exactly the corpse defs. The oracle is <c>CorpseDefGenerator.IsCorpseDef</c> — a corpse
        /// def's <c>thingClass</c> — not the category the filter excludes by, so a corpse def that somehow missed
        /// its category would show up here as a body the granary still takes.
        /// </summary>
        [Fact]
        public void The_granarys_default_refuses_every_kind_of_body_and_nothing_else()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            SpawnStack(map, new IntVec3(3, 0, 3), Chunk, 5);
            SettlementStockInitiative.EnsureGranary(settlement, map);
            ThingFilter granary = SettlementStockInitiative.GranaryOf(map)!.filter;

            var everything = new ThingFilter();
            everything.SetAllowAll(null);

            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;

            // Minted at load, not on the first death (#81): nothing in this test has killed anybody.
            Assert.Contains(all, CorpseDefGenerator.IsCorpseDef);

            foreach (ThingDef def in all)
            {
                bool body = CorpseDefGenerator.IsCorpseDef(def);
                bool expected = everything.Allows(def) && !body;
                Assert.True(
                    granary.Allows(def) == expected,
                    def.defName + (body ? " is a body and the granary takes it." : " is not a body and the granary refuses it."));
            }
        }

        [Fact]
        public void A_stockpile_the_player_marks_takes_goods_but_not_bodies_until_the_player_says_so()
        {
            Settlement settlement = OpenedSettlement("dead-player-stockpile");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            ThingDef humanBody = CorpseDefGenerator.CorpseDefFor(Human);
            ThingDef huskyBody = CorpseDefGenerator.CorpseDefFor(Husky);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { cell }).Outcome);
            var stockpile = Assert.IsType<Zone_Stockpile>(map.zoneManager.ZoneAt(cell));

            Assert.False(stockpile.filter.Allows(humanBody), "A stockpile the player marks takes human bodies by default.");
            Assert.False(stockpile.filter.Allows(huskyBody), "A stockpile the player marks takes animal bodies by default.");
            Assert.True(stockpile.filter.Allows(Wood));
            Assert.True(stockpile.filter.Allows(Chunk));

            // The player's own say-so opens it — RimWorld's storage tab, here the standing-rules command.
            Assert.Equal(
                MapCommandOutcome.Done,
                MapCommands.SetStockpileFilter(cell, new[] { humanBody.defName }).Outcome);
            Assert.True(stockpile.filter.Allows(humanBody));
        }

        /// <summary>
        /// The granary is sized to goods lying out with nowhere to go. A body is not goods and the granary
        /// will not take one, so it is not backlog either: a settlement whose only loose thing is its dead
        /// does not paint a granary over them.
        /// </summary>
        [Fact]
        public void A_body_lying_out_is_not_backlog_for_the_granary()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            var fellAt = new IntVec3(10, 0, 10);
            CorpseThing body = BodyAt(map, fellAt, Human, "Ada");

            Assert.DoesNotContain(body, SettlementStockInitiative.HomelessStacks(map));

            for (int i = 0; i < 5; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Empty(map.zoneManager.AllZones);
            Assert.True(body.Spawned);
            Assert.Equal(fellAt, body.Position);
        }

        // -------------------------------------------------------------------------------------------
        // Defence 2: banking never destroys a thing that holds a pawn.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Banking_never_destroys_a_body_even_in_a_stockpile_painted_to_take_one()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            var humanCell = new IntVec3(5, 0, 5);
            var huskyCell = new IntVec3(6, 0, 5);
            Zone_Stockpile pile = StockpileTakingAnything(map, humanCell, huskyCell);
            CorpseThing person = BodyAt(map, humanCell, Human, "Ada");
            CorpseThing dog = BodyAt(map, huskyCell, Husky, "Rex");

            // The filter is not what protects them here: this stockpile takes bodies, both are resting in it
            // as stored, and nobody holds a reservation on either — every other reason banking has to leave a
            // stack alone is absent.
            foreach (CorpseThing body in new[] { person, dog })
            {
                Assert.True(pile.filter.Allows(body.def));
                Assert.True(HaulAIUtility.IsInValidStorage(body));
                Assert.False(map.reservationManager.IsReserved(body));
            }

            int banked = SettlementStockInitiative.BankStoredGoods(settlement, map);
            for (int i = 0; i < 5; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(0, banked);
            foreach (CorpseThing body in new[] { person, dog })
            {
                Assert.False(body.Destroyed, body.def.defName + " was destroyed by banking.");
                Assert.True(body.Spawned);
                Assert.Equal(0, settlement.StoreCountOf(body.def));
            }
            Assert.Equal(humanCell, person.Position);
            Assert.DoesNotContain(settlement.Stores.Keys, CorpseDefGenerator.IsCorpseDef);
        }

        /// <summary>
        /// Not only is the body left alone — the person inside it is still a person the game can find. A
        /// dead citizen leaves the roster on the next sync, and from then on the one real copy of them is the
        /// one inside the body (<c>Settlement.ExposeData</c> says so in as many words); the god view reaches
        /// them only through it (<c>GodViewSnapshot.Citizen</c>). Destroying the body closed that door for good.
        /// </summary>
        [Fact]
        public void The_person_inside_is_still_there_and_can_still_be_opened_afterwards()
        {
            Settlement settlement = OpenedSettlement("dead-still-findable");
            CoreMap map = settlement.InteriorMap!;
            Pawn victim = AnyCitizenOn(settlement, map);
            int id = victim.thingIDNumber;
            string name = GodViewSnapshot.Citizen(id)!.Name;

            Kill(settlement, victim);
            CorpseThing body = victim.corpse!;
            Assert.NotNull(body);

            // The player marks a stockpile under the body and tells it to keep bodies: the route no default
            // filter can close, and exactly the one the guard is for.
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { body.Position }).Outcome);
            Assert.Equal(
                MapCommandOutcome.Done,
                MapCommands.SetStockpileFilter(body.Position, new[] { body.def.defName }).Outcome);
            Assert.True(HaulAIUtility.IsInValidStorage(body));

            SettlementStockInitiative.Run(settlement, map);

            // And through the game's own loop, long enough for the roster sync to let them go — after which
            // the body is the only way to reach them.
            RunTicks(SettlementTuning.CitizenMapSyncIntervalTicks * 2 + 1);
            Assert.DoesNotContain(settlement.Citizens, c => c.thingIDNumber == id);

            Assert.False(victim.Destroyed, "The person inside the body was destroyed.");
            Assert.True(body.Spawned);
            Assert.Same(body, victim.corpse);
            Assert.Same(victim, body.InnerPawn);
            Assert.Equal(0, settlement.StoreCountOf(body.def));

            CitizenView? view = GodViewSnapshot.Citizen(id);
            Assert.NotNull(view);
            Assert.True(view!.Dead);
            Assert.Equal(name, view.Name);
        }

        // -------------------------------------------------------------------------------------------
        // Ordinary goods: unchanged.
        // -------------------------------------------------------------------------------------------

        private static (Settlement Settlement, int Banked) BankGoods(bool withABodyAmongThem)
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            StockpileTakingAnything(map, new IntVec3(5, 0, 5), new IntVec3(6, 0, 5), new IntVec3(7, 0, 5), new IntVec3(8, 0, 5));
            SpawnStack(map, new IntVec3(5, 0, 5), Chunk, 12);
            SpawnStack(map, new IntVec3(6, 0, 5), Wood, 30);
            SpawnStack(map, new IntVec3(7, 0, 5), Meal, 9);
            if (withABodyAmongThem) BodyAt(map, new IntVec3(8, 0, 5), Human, "Ada");

            int banked = SettlementStockInitiative.BankStoredGoods(settlement, map);
            return (settlement, banked);
        }

        /// <summary>
        /// Everything that is not a body banks exactly as it did: the same goods, the same counts, the same
        /// total, whether or not a body lies in the same stockpile. The absolute figures are the existing
        /// behaviour <c>SettlementStockTests</c> pins (a non-food stack banks whole), restated here so this
        /// lane cannot have quietly moved them.
        /// </summary>
        [Fact]
        public void Ordinary_goods_beside_a_body_bank_exactly_as_they_would_without_it()
        {
            (Settlement withBody, int bankedWithBody) = BankGoods(withABodyAmongThem: true);
            (Settlement without, int bankedWithout) = BankGoods(withABodyAmongThem: false);

            Assert.Equal(bankedWithout, bankedWithBody);
            Assert.Equal(Ledger(without), Ledger(withBody));

            Assert.Equal(12, without.StoreCountOf(Chunk));
            Assert.Equal(30, without.StoreCountOf(Wood));
            Assert.True(bankedWithout >= 42, "Non-food goods resting in storage bank whole.");
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Across a save: the granary still refuses bodies (its filter is saved as the flattened set, so the
        /// default must survive as data, not be re-derived), a body resting in a stockpile that takes bodies
        /// comes back with its person re-linked, and a banking pass over the reloaded state still leaves it
        /// alone.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_keeps_the_body_the_person_and_a_granary_that_refuses_them()
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                "dead-scribe", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Rest", 2, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(4343), "Resthome");
            CoreMap map = settlement.EnterMap(world);

            var middle = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            SpawnStack(map, FreeCellNear(map, middle), Chunk, 14);
            SettlementStockInitiative.Run(settlement, map);
            Assert.NotNull(SettlementStockInitiative.GranaryOf(map));

            IntVec3 restingCell = FreeCellNear(map, middle + new IntVec3(12, 0, 12));
            StockpileTakingAnything(map, restingCell);
            CorpseThing body = BodyAt(map, restingCell, Human, "Ada");
            int innerId = body.InnerPawn!.thingIDNumber;
            ThingDef bodyDef = body.def;
            Assert.False(SettlementStockInitiative.GranaryOf(map)!.filter.Allows(bodyDef));

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Resthome");
            CoreMap reloadedMap = reloaded.InteriorMap!;

            Zone_Stockpile? granary = SettlementStockInitiative.GranaryOf(reloadedMap);
            Assert.NotNull(granary);
            Assert.False(granary!.filter.Allows(bodyDef), "A reloaded granary takes bodies.");
            Assert.True(granary.filter.Allows(Chunk), "A reloaded granary that accepts nothing would quietly stop storing.");

            CorpseThing reloadedBody = Assert.Single(
                reloadedMap.listerThings.AllThings.OfType<CorpseThing>(),
                c => c.InnerPawn != null && c.InnerPawn.thingIDNumber == innerId);
            Assert.Equal(restingCell, reloadedBody.Position);
            Assert.Same(reloadedBody, reloadedBody.InnerPawn!.corpse);
            Assert.True(reloadedBody.InnerPawn.Dead);

            SettlementStockInitiative.Run(reloaded, reloadedMap);

            Assert.True(reloadedBody.Spawned, "The reloaded body was banked.");
            Assert.False(reloadedBody.InnerPawn.Destroyed);
            Assert.DoesNotContain(reloaded.Stores.Keys, CorpseDefGenerator.IsCorpseDef);
        }
    }
}
