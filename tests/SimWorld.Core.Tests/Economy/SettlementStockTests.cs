using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreScenario = SimWorld.Scenario.Scenario;
using CoreWorld = SimWorld.World.World;

// SimWorld.Economy shares its leaf segment with this test namespace; alias the production types rather than
// relying on which one a bare name resolves to (CLAUDE.md).
using ConstructionInitiativeTuning = global::SimWorld.Building.ConstructionInitiativeTuning;
using Guild = global::SimWorld.Crafting.Guild;
using GuildDefOf = global::SimWorld.Crafting.GuildDefOf;
using GuildInitiative = global::SimWorld.Crafting.GuildInitiative;
using GuildInitiativeTuning = global::SimWorld.Crafting.GuildInitiativeTuning;
using GuildManager = global::SimWorld.Crafting.GuildManager;
using GuildTuning = global::SimWorld.Crafting.GuildTuning;
using SettlementStockInitiative = global::SimWorld.Economy.SettlementStockInitiative;
using SettlementStockTuning = global::SimWorld.Economy.SettlementStockTuning;
using Zone_Stockpile = global::SimWorld.Building.Zone_Stockpile;

namespace SimWorld.Tests.Economy
{
    /// <summary>
    /// <b>The seam between the two halves of the game, in the map-to-ledger direction</b> (spec §11.2).
    ///
    /// <para/>The defect: <c>Settlement.Stores</c> is the civilization-scale ledger every consumer of a
    /// settlement's wealth reads — trade, tribute, quest rewards, raid loot, the storyteller's wealth term,
    /// the settlement's own appetite for storage, and <c>Crafting.Guild</c> — and its only writers were
    /// scenario setup, trade, tribute, quest rewards, raiders taking things away, and the guild, which
    /// consumes from it. <b>Nothing that happened on a map ever became something the civilization had.</b> A
    /// citizen could mine granite, haul it and cut it into blocks, and at civilization scale the settlement
    /// was exactly as poor as before; the shipped <c>MasonsGuild</c> starved by construction.
    ///
    /// <para/>Two more links in the same chain were missing and are closed here because this one cannot work
    /// without them: nothing in <c>src/</c> had ever created a <c>Zone_Stockpile</c>, so
    /// <c>AI.WorkGiver_Haul</c> — built, tested and listed in content — could never produce a job in any game;
    /// and nothing in <c>src/</c> had ever called <c>GuildManager.Establish</c>, so
    /// <c>GuildManager.GuildManagerTick</c> ran over an empty list for the life of every game.
    ///
    /// <para/>What these pin: the no-double-counting invariant (<i>a unit is a Thing on a map or a count in the
    /// ledger, never both</i>), the three tiering states a settlement can be in, the two guards that keep the
    /// map's own systems working, and the end-to-end chain — chunk on the ground, hauled, banked, cut by a
    /// guild — driven by the real game loop with nothing called by hand.
    /// </summary>
    public class SettlementStockTests : ContentTestBase
    {
        public SettlementStockTests(CoreContentFixture content) : base(content)
        {
            Find.ResearchManager = new ResearchManager();
        }

        // -------------------------------------------------------------------------------------------
        // Fixtures.
        // -------------------------------------------------------------------------------------------

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Chunk => Def("ChunkSandstone");

        private static ThingDef Blocks => Def("BlocksSandstone");

        private static ThingDef Meal => Def("MealSimple");

        /// <summary>Read off the shipped recipe rather than restated: one guild iteration is one batch.</summary>
        private static int Blocks_Per_Batch =>
            DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed("Make_Blocks_Sandstone").products![0].count;

        private static CoreMap NewMap(int size = 20) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "Stockhome", 0);

        private static Settlement PeopledSettlement(int citizens = 3)
        {
            Settlement settlement = PlainSettlement();
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

        /// <summary>A stockpile holding <paramref name="count"/> of <paramref name="def"/> on one cell — the
        /// state a citizen's own hauling leaves behind, arranged directly so a unit test does not have to pay
        /// for a pawn walking across a map.</summary>
        private static Thing Stockpiled(CoreMap map, IntVec3 cell, ThingDef def, int count)
        {
            var zone = new Zone_Stockpile();
            map.zoneManager.RegisterZone(zone);
            map.zoneManager.AddCell(zone, cell);
            zone.filter.SetAllowAll(null);
            return SpawnStack(map, cell, def, count);
        }

        private static int OnMap(CoreMap map, ThingDef def)
        {
            int total = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) total += stacks[i].stackCount;
            }
            return total;
        }

        // -------------------------------------------------------------------------------------------
        // The seam itself.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void Nothing_a_settlement_mined_could_reach_its_ledger_and_now_it_can()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            Stockpiled(map, new IntVec3(5, 0, 5), Chunk, 12);

            // The state every game in this port started and stayed in: a town with stone in its store hut and
            // a civilization that owned none of it.
            Assert.Equal(0, settlement.StoreCountOf(Chunk));

            int banked = SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(12, banked);
            Assert.Equal(12, settlement.StoreCountOf(Chunk));
        }

        /// <summary>
        /// The invariant, stated as arithmetic: goods are <b>moved</b>, never copied. If the ledger could be
        /// credited without the map being debited, a chunk would be sellable through trade and cuttable at a
        /// bench at the same time — which is the failure mode this whole design is arranged against.
        /// </summary>
        [Fact]
        public void Goods_are_moved_and_never_copied_so_nothing_can_be_spent_twice()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            Stockpiled(map, new IntVec3(5, 0, 5), Chunk, 30);
            settlement.AddStore(Chunk, 5); // the civilization already held some, from trade

            int totalBefore = OnMap(map, Chunk) + settlement.StoreCountOf(Chunk);
            Assert.Equal(35, totalBefore);

            // Many passes, not one: a rule that double-counts usually does it on the second look.
            for (int i = 0; i < 25; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(totalBefore, OnMap(map, Chunk) + settlement.StoreCountOf(Chunk));
            Assert.Equal(0, OnMap(map, Chunk));
            Assert.Equal(35, settlement.StoreCountOf(Chunk));
        }

        [Fact]
        public void Loose_goods_are_left_on_the_map_for_the_maps_own_systems()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            // Not in any stockpile: the chunk beside the stonecutter's table, which WorkGiver_DoBill needs
            // within its own search radius of the bench.
            SpawnStack(map, new IntVec3(9, 0, 9), Chunk, 8);

            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(0, settlement.StoreCountOf(Chunk));
            Assert.Equal(8, OnMap(map, Chunk));
        }

        [Fact]
        public void A_reserved_stack_is_never_banked_out_from_under_the_citizen_using_it()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            Thing stack = Stockpiled(map, new IntVec3(5, 0, 5), Chunk, 10);

            Pawn mason = NewHuman("Mason");
            GenSpawn.Spawn(mason, new IntVec3(4, 0, 5), map);
            Assert.True(map.reservationManager.Reserve(mason, stack));

            Assert.Equal(0, SettlementStockInitiative.BankStoredGoods(settlement, map));
            Assert.Equal(10, OnMap(map, Chunk));

            map.reservationManager.Release(mason, stack);
            Assert.Equal(10, SettlementStockInitiative.BankStoredGoods(settlement, map));
        }

        /// <summary>
        /// The larder rule, pinned as a relation rather than a figure: whatever
        /// <c>HuntingInitiative.NutritionWanted</c> says the settlement wants in hand stays physically on the
        /// map, because the map is where people eat. The surplus above it banks. Nothing here asserts four
        /// days or 1.6 nutrition — those are that class's numbers, and this reads them from it.
        /// </summary>
        [Fact]
        public void A_settlement_keeps_its_larder_on_the_map_and_banks_only_the_surplus()
        {
            Settlement settlement = PeopledSettlement(3);
            CoreMap map = NewMap();
            foreach (Pawn citizen in settlement.Citizens) GenSpawn.Spawn(citizen, new IntVec3(1, 0, 1), map);

            float perMeal = Meal.ingestible!.nutrition;
            float wanted = HuntingInitiative.NutritionWanted(settlement, map);
            int mealsWanted = (int)(wanted / perMeal);
            int surplus = 7;

            Stockpiled(map, new IntVec3(5, 0, 5), Meal, mealsWanted + surplus);

            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.True(settlement.StoreCountOf(Meal) > 0, "A settlement with more food than it wants in hand should bank the surplus.");
            Assert.True(
                HuntingInitiative.NutritionAvailable(null, map) >= wanted - perMeal,
                "The larder the settlement wants in hand must stay on the map, where people eat it.");
            Assert.Equal(mealsWanted + surplus, OnMap(map, Meal) + settlement.StoreCountOf(Meal));
        }

        [Fact]
        public void A_settlement_with_nothing_to_spare_banks_none_of_its_food()
        {
            Settlement settlement = PeopledSettlement(5);
            CoreMap map = NewMap();
            foreach (Pawn citizen in settlement.Citizens) GenSpawn.Spawn(citizen, new IntVec3(1, 0, 1), map);

            Stockpiled(map, new IntVec3(5, 0, 5), Meal, 2);

            SettlementStockInitiative.BankStoredGoods(settlement, map);

            Assert.Equal(0, settlement.StoreCountOf(Meal));
            Assert.Equal(2, OnMap(map, Meal));
        }

        // -------------------------------------------------------------------------------------------
        // The granary.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlement_with_goods_and_nowhere_to_put_them_paints_its_own_granary()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            // Every Zone_Stockpile in this repository used to be made by a test; a game made none, so
            // WorkGiver_Haul could never produce a job.
            Assert.Empty(map.zoneManager.AllZones);

            SpawnStack(map, new IntVec3(9, 0, 9), Chunk, 5);
            SettlementStockInitiative.EnsureGranary(settlement, map);

            Zone_Stockpile granary = Assert.IsType<Zone_Stockpile>(Assert.Single(map.zoneManager.AllZones));
            Assert.True(granary.CellCount > 0);
            Assert.True(granary.filter.Allows(Chunk));
        }

        [Fact]
        public void An_empty_map_is_given_no_granary_at_all()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();

            for (int i = 0; i < 10; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Empty(map.zoneManager.AllZones);
        }

        /// <summary>
        /// The granary has no target size — it is the settlement's own backlog. What is pinned is the trend
        /// (more goods lying out with nowhere to go asks for more cells), the bound (never more than one
        /// pass's worth at a time) and the termination (it stops once the backlog is served), never a figure.
        /// </summary>
        [Fact]
        public void The_granary_tracks_the_backlog_grows_by_at_most_one_pass_and_then_stops()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap(40);

            for (int i = 0; i < 12; i++) SpawnStack(map, new IntVec3(20 + i, 0, 30), Chunk, 3);

            int addedFirst = SettlementStockInitiative.EnsureGranary(settlement, map);
            Assert.True(addedFirst > 0);
            Assert.True(addedFirst <= SettlementStockTuning.MaxGranaryCellsPerPass);

            int smallCells = SettlementStockInitiative.GranaryOf(map)!.CellCount;

            // A bigger backlog asks for a bigger granary...
            for (int i = 0; i < 12; i++) SpawnStack(map, new IntVec3(20 + i, 0, 32), Chunk, 3);
            SettlementStockInitiative.EnsureGranary(settlement, map);
            int biggerCells = SettlementStockInitiative.GranaryOf(map)!.CellCount;
            Assert.True(biggerCells > smallCells, "More goods with nowhere to go should ask for more storage.");

            // ...and it stops the moment the settlement has room for everything it owns. Nothing here hauls —
            // there is no citizen in this test — so what terminates the growth is storage the settlement
            // already has and has not filled, which is the honest stopping condition: a granary is sized to
            // what has nowhere to go, not to what has not been carried there yet.
            for (int i = 0; i < 30; i++) SettlementStockInitiative.Run(settlement, map);
            int settled = SettlementStockInitiative.GranaryOf(map)!.CellCount;
            for (int i = 0; i < 10; i++) SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(settled, SettlementStockInitiative.GranaryOf(map)!.CellCount);
            Assert.True(
                SettlementStockInitiative.EmptyStockpileCells(map) >= SettlementStockInitiative.HomelessStacks(map).Count,
                "A granary that stopped growing while goods still had nowhere to go would leave a settlement permanently behind.");
        }

        [Fact]
        public void One_pass_never_paints_more_than_a_storage_huts_worth_of_cells()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap(60);

            // A backlog far larger than one pass may answer.
            for (int x = 0; x < 40; x++)
            {
                for (int z = 0; z < 5; z++) SpawnStack(map, new IntVec3(x, 0, 40 + z), Chunk, 1);
            }

            int added = SettlementStockInitiative.EnsureGranary(settlement, map);

            Assert.Equal(SettlementStockTuning.MaxGranaryCellsPerPass, added);
            Assert.Equal(ConstructionInitiativeTuning.GoodsPerStorageHut, SettlementStockTuning.MaxGranaryCellsPerPass);
        }

        // -------------------------------------------------------------------------------------------
        // Tiering: the three states a settlement can be in (spec §11.2/§11.3).
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_settlement_nobody_has_entered_has_no_map_and_that_is_not_an_error()
        {
            Settlement settlement = PeopledSettlement();
            Assert.Null(settlement.InteriorMap);

            SettlementStockInitiative.TickSettlement(settlement); // must not throw

            // And above all must not have grown one: a settlement nobody is watching stays weightless.
            Assert.Null(settlement.InteriorMap);
        }

        /// <summary>
        /// A generated but unwatched settlement: its citizens have been demoted out of Full tier and taken off
        /// the map (<c>Settlement.SyncCitizenSpawns</c>), so nobody hauls anything new in — but what is already
        /// resting in the granary is still the settlement's, and is still banked.
        /// </summary>
        [Fact]
        public void An_unwatched_settlements_granary_still_banks_what_is_already_in_it()
        {
            Settlement settlement = PeopledSettlement();
            CoreMap map = NewMap();
            foreach (Pawn citizen in settlement.Citizens) citizen.tier.Notify_AttentionChanged(false);
            Assert.DoesNotContain(settlement.Citizens, c => c.Spawned);

            Stockpiled(map, new IntVec3(5, 0, 5), Chunk, 9);
            SettlementStockInitiative.Run(settlement, map);

            Assert.Equal(9, settlement.StoreCountOf(Chunk));
        }

        // -------------------------------------------------------------------------------------------
        // Scribe.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// This module keeps no state of its own — every decision is re-derived from the map and the ledger on
        /// the next pass — so what a round trip has to preserve is the granary (which saves with its map, via
        /// <c>ZoneManager.ExposeData</c>) and the ledger (which saves with the settlement). What it has to
        /// prove is that a reloaded settlement neither re-banks what it already banked nor forgets its store.
        /// </summary>
        [Fact]
        public void Scribe_round_trip_preserves_the_granary_and_the_ledger_without_re_banking()
        {
            CoreWorld world = WorldGenerator.GenerateWorld(
                "stock-scribe", 0.3f, OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, "Stock", 2, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, 20, new RandomStream(4242), "Granaryhome");
            CoreMap map = settlement.EnterMap(world);

            Stockpiled(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), Chunk, 14);
            SettlementStockInitiative.Run(settlement, map);
            int bankedBefore = settlement.StoreCountOf(Chunk);
            int granaryCellsBefore = SettlementStockInitiative.GranaryOf(map)!.CellCount;
            Assert.True(bankedBefore > 0 && granaryCellsBefore > 0);

            string xml = Scribe.SaveToString(world, "world");
            CoreWorld loaded = Scribe.Load<CoreWorld>(xml, "world", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Settlement reloaded = loaded.worldObjects.OfType<Settlement>().First(s => s.name == "Granaryhome");
            CoreMap reloadedMap = reloaded.InteriorMap!;

            Assert.Equal(bankedBefore, reloaded.StoreCountOf(Chunk));
            Zone_Stockpile? granary = SettlementStockInitiative.GranaryOf(reloadedMap);
            Assert.NotNull(granary);
            Assert.Equal(granaryCellsBefore, granary!.CellCount);
            Assert.True(granary.filter.Allows(Chunk), "A reloaded granary that accepts nothing would quietly stop storing.");

            // Re-running over unchanged state banks nothing a second time — the invariant across a save.
            SettlementStockInitiative.Run(reloaded, reloadedMap);
            Assert.Equal(bankedBefore, reloaded.StoreCountOf(Chunk));
        }

        // -------------------------------------------------------------------------------------------
        // The guild: who staffs it, and the labour partition that stops a settlement working twice.
        // -------------------------------------------------------------------------------------------

        private static void KnowMasonry() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));

        [Fact]
        public void Nothing_used_to_establish_a_guild_and_now_a_settlement_does()
        {
            Settlement settlement = PeopledSettlement();
            settlement.AddStatisticalPeople(120);
            var guilds = new GuildManager();
            KnowMasonry();

            Assert.Empty(guilds.Guilds);

            GuildInitiative.Run(settlement, guilds);

            Guild guild = Assert.Single(guilds.GuildsIn(settlement));
            Assert.Same(GuildDefOf.MasonsGuild, guild.Def);
            Assert.NotEmpty(guild.Bills.Bills);

            // Idempotent: a second pass neither establishes a second guild nor queues a second bill.
            int bills = guild.Bills.Bills.Count;
            for (int i = 0; i < 5; i++) GuildInitiative.Run(settlement, guilds);
            Assert.Single(guilds.GuildsIn(settlement));
            Assert.Equal(bills, guild.Bills.Bills.Count);
        }

        [Fact]
        public void A_civilization_that_has_not_learned_the_trade_establishes_no_guild_for_it()
        {
            Settlement settlement = PeopledSettlement();
            settlement.AddStatisticalPeople(120);
            var guilds = new GuildManager();

            GuildInitiative.Run(settlement, guilds);

            Assert.Empty(guilds.Guilds);
        }

        [Fact]
        public void A_settlement_with_nobody_below_Full_tier_has_no_guild_to_staff()
        {
            Settlement settlement = PeopledSettlement(4); // every citizen defaults to Full
            var guilds = new GuildManager();
            KnowMasonry();

            Assert.All(settlement.Citizens, c => Assert.Equal(PawnTier.Full, c.tier.Tier));

            GuildInitiative.Run(settlement, guilds);

            Assert.Empty(guilds.Guilds);
        }

        /// <summary>
        /// The labour partition, which is the tiering invariant: a citizen either works jobs on a map or works
        /// in a guild, never both. Without it a settlement gets its stonecutting twice out of the same people
        /// — once at the bench through <c>WorkGiver_DoBill</c> and once in the guild.
        /// </summary>
        [Fact]
        public void A_guild_seats_nobody_who_is_working_on_a_map_and_releases_anyone_promoted_onto_one()
        {
            Settlement settlement = PeopledSettlement(0);
            var guilds = new GuildManager();
            KnowMasonry();

            Pawn craftsman = NewHuman("Craftsman");
            settlement.AddCitizen(craftsman);
            craftsman.tier.Notify_AttentionChanged(false);

            GuildInitiative.Run(settlement, guilds);
            Guild guild = Assert.Single(guilds.GuildsIn(settlement));
            Assert.Contains(craftsman, guild.Members);

            // Attention promotes them to Full: they are now standing on the interior working jobs, so the
            // guild must let them go rather than count their labour a second time.
            craftsman.tier.Notify_AttentionChanged(true);
            Assert.Equal(PawnTier.Full, craftsman.tier.Tier);

            GuildInitiative.Run(settlement, guilds);
            Assert.DoesNotContain(craftsman, guild.Members);
            Assert.Null(craftsman.workSettings.Role);
        }

        [Fact]
        public void A_guild_never_takes_a_citizen_who_already_holds_another_role()
        {
            Settlement settlement = PeopledSettlement(0);
            var guilds = new GuildManager();
            KnowMasonry();

            Pawn steward = NewHuman("Steward");
            settlement.AddCitizen(steward);
            steward.tier.Notify_AttentionChanged(false);
            global::SimWorld.Work.RoleDef stewardRole =
                DefDatabase<global::SimWorld.Work.RoleDef>.GetNamed("Steward");
            steward.workSettings.SetRole(stewardRole);
            settlement.AddStatisticalPeople(50);

            GuildInitiative.Run(settlement, guilds);

            Assert.Same(stewardRole, steward.workSettings.Role);
            Guild guild = Assert.Single(guilds.GuildsIn(settlement));
            Assert.DoesNotContain(steward, guild.Members);
            Assert.True(guild.StatisticalMembers > 0, "The cohort should take the seat the office holder could not.");
        }

        /// <summary>
        /// The staffing count is derived from <c>StonecuttingTuning.TablesWanted</c> — the same question asked
        /// at map scale, with the arithmetic written down there. What is pinned here is the band that
        /// derivation was chosen to land in, which is <c>GuildTuning.WorkPerMemberPerInterval</c>'s own stated
        /// intent: "a batch takes a few intervals rather than one".
        /// </summary>
        [Fact]
        public void A_guilds_staffing_makes_a_batch_take_a_few_intervals_and_not_one()
        {
            Settlement settlement = PeopledSettlement(0);
            settlement.AddStatisticalPeople(400);
            var guilds = new GuildManager();
            KnowMasonry();

            GuildInitiative.Run(settlement, guilds);
            Guild guild = Assert.Single(guilds.GuildsIn(settlement));
            settlement.AddStore(Chunk, 200);

            int intervals = 0;
            while (settlement.StoreCountOf(Blocks) == 0 && intervals < 60)
            {
                intervals++;
                guild.GuildTick(intervals * GuildTuning.GuildIntervalTicks);
            }

            Assert.True(intervals > 1, "A batch finished in a single interval would make a guild instant.");
            Assert.True(intervals < 30, "A batch that takes thirty intervals is an industry that never delivers.");

            // A four-hundred-strong town does not get four hundred masons: the count is flat, deliberately.
            Assert.Equal(GuildInitiativeTuning.CraftsmenPerGuild, guild.StatisticalMembers);
        }

        // -------------------------------------------------------------------------------------------
        // End to end, through the real game loop, with nothing called by hand.
        // -------------------------------------------------------------------------------------------

        private static CoreScenario TribalStart() => ScenarioDefOf.TribalStart.scenario;

        /// <summary>
        /// The chain, driven by <see cref="Game"/>'s own tick hooks: stone lying on a settlement's interior the
        /// way mining leaves it, a citizen who hauls it into a granary the settlement painted itself, a seam
        /// that banks it into the civilization's ledger, and a guild the settlement established itself that
        /// cuts it into blocks. Nothing below calls an initiative, a work giver, a job driver or a guild tick;
        /// the test spawns the stone, gives the civilization the knowledge, and ticks.
        ///
        /// <para/>The chunks in the ledger going <i>down</i> is what makes this the guild's work and not the
        /// bench's: a stonecutter's table consumes chunks off the map, and the only thing in this game that can
        /// take one out of <c>Settlement.Stores</c> is <c>Guild.ConsumeIngredients</c>.
        /// </summary>
        [Fact]
        public void A_civilization_that_owned_nothing_its_settlements_made_now_mines_hauls_banks_and_cuts_stone()
        {
            Game game = Game.NewGame(TribalStart(), "stock-end-to-end", subdivisionOverride: 3, soloStart: true);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            CoreMap map = game.EnterSettlement(settlement);
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));

            // The town is bigger than the handful of people the camera follows — the ordinary shape of a real
            // settlement (spec §11.3), and the tier a guild's craftsmen come from.
            settlement.AddStatisticalPeople(200);

            // What mining leaves on the ground, put down near the citizens so the haul is a short one.
            Pawn anyone = settlement.Citizens.First(c => c.Spawned);
            for (int i = 0; i < 6; i++)
            {
                IntVec3 cell = anyone.Position + new IntVec3(i % 3, 0, i / 3);
                if (GenGrid.InBounds(cell, map) && GenGrid.Standable(cell, map)) SpawnStack(map, cell, Chunk, 4);
            }

            Assert.Equal(0, settlement.StoreCountOf(Chunk));
            Assert.Equal(0, settlement.StoreCountOf(Blocks));

            int peakChunksBanked = 0;
            for (int i = 0; i < 14000; i++)
            {
                game.TickManager.DoSingleTick();
                peakChunksBanked = System.Math.Max(peakChunksBanked, settlement.StoreCountOf(Chunk));
            }

            Assert.True(peakChunksBanked > 0,
                "A settlement whose citizens hauled stone into its own granary should have banked some of it into the civilization's ledger.");

            Guild guild = Assert.Single(game.Guilds.GuildsIn(settlement));
            Assert.Same(GuildDefOf.MasonsGuild, guild.Def);

            // The load-bearing assertion, and the one that makes this the guild's work rather than a bench's:
            // a stonecutter's table consumes chunks off the map, and the only thing in this game that can take
            // one *out of the ledger* is Guild.ConsumeIngredients. A ledger chunk count that ever fell is
            // therefore proof of at least one completed guild iteration — and one iteration is a whole batch.
            int chunksSpentFromTheLedger = peakChunksBanked - settlement.StoreCountOf(Chunk);
            Assert.True(chunksSpentFromTheLedger > 0,
                "The masons' guild should have drawn stone out of the civilization's ledger; it banked "
                    + peakChunksBanked + " chunks, still holds " + settlement.StoreCountOf(Chunk)
                    + " and has " + guild.WorkAccumulated + " work banked.");
            Assert.True(settlement.StoreCountOf(Blocks) >= Blocks_Per_Batch,
                "A completed guild iteration puts a whole batch of blocks into the ledger.");
        }
    }
}
