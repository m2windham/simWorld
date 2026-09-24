using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreFilth = SimWorld.Filth.Filth;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The standing-rule half of the command surface: <see cref="MapCommands.QueueBill"/>,
    /// <see cref="MapCommands.SetBillSuspended"/>, <see cref="MapCommands.SetStockpileFilter"/>,
    /// <see cref="MapCommands.UnmarkZone"/> and <see cref="MapCommands.SetHomeArea"/>. Before these existed a
    /// player had no way to tell a bench what to make, no way to pause a bill without deleting it, no way to
    /// keep a stockpile from taking something, no way to give a zone back, and — the one genuine behaviour
    /// change in this file — <see cref="AreaManager.Home"/> had never been written to by anything, so
    /// <see cref="CleaningBounds"/> had only ever read a permanently empty area.
    ///
    /// <para/>Every "gets worked"/"stops being worked" claim below is proved by the real tick loop
    /// (<see cref="ContentTestBase.RunTicks"/>, which drives <see cref="TickManager.DoSingleTick"/>) rather
    /// than by calling a worker or a job driver directly, the same discipline
    /// <c>MapCommandsTests.A_citizen_builds_what_the_player_designated</c> set for the acts half of this
    /// surface.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapCommandsStandingRulesTests : ContentTestBase
    {
        public MapCommandsStandingRulesTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static void KnowCooking()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Fire"));
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Cooking"));
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        /// <summary>Same two-step every live-map test in this codebase uses — attention <b>and</b> a real,
        /// generated interior — since <see cref="MapCommands"/> resolves its map exactly as
        /// <c>MapViewSnapshot.Capture()</c> does, off <c>Find.God.Attention.FocusedSettlement</c>.</summary>
        private static Settlement OpenedSettlement(string seed)
        {
            Game game = NewSoloGame(seed);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static Pawn AnyCitizenOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.First(p => p.Spawned && p.Map == map);

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>The first cell spiralling out from <paramref name="near"/> (which may itself be occupied,
        /// e.g. by the citizen used to find it) that is empty, standable and unzoned — deterministic, unlike
        /// the initiatives' own random sampling, which is exactly what a test wants.</summary>
        private static IntVec3 FreeCellNear(CoreMap map, IntVec3 near)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; i < pattern.Count; i++)
            {
                IntVec3 candidate = near + pattern[i];
                if (!GenGrid.InBounds(candidate, map)) continue;
                if (!GenGrid.Standable(candidate, map)) continue;
                if (map.zoneManager.ZoneAt(candidate) != null) continue;
                if (map.edificeGrid[candidate] != null) continue;
                if (map.thingGrid.ThingsListAt(candidate).Count > 0) continue;
                return candidate;
            }
            throw new InvalidOperationException("No free cell found near " + near + ".");
        }

        private static CompBillGiver Comp(Thing bench) => ((ThingWithComps)bench).GetComp<CompBillGiver>()!;

        // -------------------------------------------------------------------------------------------
        // QueueBill: the bill actually gets worked, through the real bill path.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_queued_bill_gets_worked_and_produces_the_thing()
        {
            Settlement settlement = OpenedSettlement("queue-a-meal-bill");
            CoreMap map = settlement.InteriorMap!;
            KnowCooking();

            Pawn cook = AnyCitizenOn(settlement, map);
            cook.workSettings!.DisableAll();
            cook.workSettings!.SetPriority(WorkTypeDefOf.Cooking, 1);

            IntVec3 stoveCell = FreeCellNear(map, cook.Position);
            Thing stove = ThingMaker.MakeThing(Def("FueledStove"));
            GenSpawn.Spawn(stove, stoveCell, map);
            SpawnStack(map, FreeCellNear(map, stoveCell), "RawBerries", 40);

            MapCommandResult queued = MapCommands.QueueBill(stoveCell, "CookMealSimple");
            Assert.Equal(MapCommandOutcome.Done, queued.Outcome);
            Assert.True(queued.Changed);

            RunTicks(9000, cook);

            Assert.NotEmpty(map.listerThings.ThingsOfDef(Def("MealSimple")));

            // The standing-rule half of the claim: a Forever bill never goes inert on its own, so it is still
            // asking to be done after it has already produced — see MapCommands.StandingRules.cs's class doc
            // for why that is the deliberate choice behind the whole bill-collision decision.
            var bill = (Bill_Production)Comp(stove).BillStack.Bills[0];
            Assert.Equal(BillRepeatMode.Forever, bill.repeatMode);
            Assert.True(bill.ShouldDoNow(), "a standing bill should still want doing after it has produced");
        }

        [Fact]
        public void A_suspended_bill_stops_being_worked_and_resumes_when_unsuspended()
        {
            Settlement settlement = OpenedSettlement("suspend-a-meal-bill");
            CoreMap map = settlement.InteriorMap!;
            KnowCooking();

            Pawn cook = AnyCitizenOn(settlement, map);
            cook.workSettings!.DisableAll();
            cook.workSettings!.SetPriority(WorkTypeDefOf.Cooking, 1);

            IntVec3 stoveCell = FreeCellNear(map, cook.Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), stoveCell, map);
            SpawnStack(map, FreeCellNear(map, stoveCell), "RawBerries", 40);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.QueueBill(stoveCell, "CookMealSimple").Outcome);

            MapCommandResult suspended = MapCommands.SetBillSuspended(stoveCell, 0, true);
            Assert.Equal(MapCommandOutcome.Done, suspended.Outcome);

            RunTicks(4000, cook);
            Assert.Empty(map.listerThings.ThingsOfDef(Def("MealSimple")));

            MapCommandResult resumed = MapCommands.SetBillSuspended(stoveCell, 0, false);
            Assert.Equal(MapCommandOutcome.Done, resumed.Outcome);

            RunTicks(9000, cook);
            Assert.NotEmpty(map.listerThings.ThingsOfDef(Def("MealSimple")));
        }

        // -------------------------------------------------------------------------------------------
        // The bill-collision decision: the settlement's own initiative shares the bench instead of
        // duplicating what the player already queued.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void QueueBill_shares_the_bench_with_the_settlements_own_initiative_instead_of_duplicating_it()
        {
            Settlement settlement = OpenedSettlement("queue-then-let-the-initiative-run");
            CoreMap map = settlement.InteriorMap!;
            KnowCooking();

            Pawn cook = AnyCitizenOn(settlement, map);
            IntVec3 stoveCell = FreeCellNear(map, cook.Position);
            Thing stove = ThingMaker.MakeThing(Def("FueledStove"));
            GenSpawn.Spawn(stove, stoveCell, map);
            SpawnStack(map, FreeCellNear(map, stoveCell), "RawBerries", 40);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.QueueBill(stoveCell, "CookMealSimple").Outcome);
            Assert.Single(Comp(stove).BillStack.Bills);

            // The settlement genuinely wants meals here (research known, food on the map, mouths to feed) —
            // if CookingInitiative did not already share ground with a player bill, this is exactly the pass
            // that would have queued a second one for the same recipe.
            CookingInitiative.Run(map);

            IReadOnlyList<Bill> bills = Comp(stove).BillStack.Bills;
            Assert.Single(bills);
            Assert.Same(DefDatabase<RecipeDef>.GetNamed("CookMealSimple"), bills[0].recipe);
            // Still the player's own Forever bill, not replaced by the initiative's TargetCount one.
            Assert.Equal(BillRepeatMode.Forever, ((Bill_Production)bills[0]).repeatMode);
        }

        // -------------------------------------------------------------------------------------------
        // SetStockpileFilter: a disallowed item really does stop being hauled in.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_disallowed_stockpile_item_stops_being_accepted_and_haulers_respect_it()
        {
            Settlement settlement = OpenedSettlement("filter-a-stockpile");
            CoreMap map = settlement.InteriorMap!;

            // Every citizen but one is stood down entirely: with nineteen other haulers free to run, one of
            // them could carry our test items into a stockpile of its own (the settlement's own granary, say)
            // before our designated hauler ever reaches them, which would prove nothing about this filter.
            foreach (Pawn p in settlement.Citizens.Where(p => p.Spawned && p.Map == map)) p.workSettings!.DisableAll();
            Pawn hauler = AnyCitizenOn(settlement, map);
            hauler.workSettings!.SetPriority(WorkTypeDefOf.Hauling, 1);

            IntVec3 stockCell = FreeCellNear(map, hauler.Position);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { stockCell }).Outcome);

            // Restricted before anything is ever hauled: WoodLog only, so RawBerries can never have been
            // accepted in the first place.
            MapCommandResult filtered = MapCommands.SetStockpileFilter(stockCell, new[] { "WoodLog" });
            Assert.Equal(MapCommandOutcome.Done, filtered.Outcome);

            // A generated interior ships its own loose chunks and other debris — real haulable Things a
            // one-hauler settlement would happily fetch before (or instead of) the two this test cares about.
            // Cleared for the same reason "An_unmarked_growing_zone_stops_being_sown" clears wild plants: the
            // claim under test is the filter, not "does this hauler eventually get to everything".
            foreach (Thing loose in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).ToList()) loose.Destroy();

            IntVec3 woodCell = FreeCellNear(map, stockCell);
            Thing woodThing = SpawnStack(map, woodCell, "WoodLog", 10);
            IntVec3 berriesCell = FreeCellNear(map, stockCell);
            Thing berriesThing = SpawnStack(map, berriesCell, "RawBerries", 10);

            // Checked as soon as delivery happens rather than after a long, fixed run: once stockpiled, this
            // settlement's own economy is free to draw the wood back out of the world into its abstracted
            // Stores ledger (Economy.SettlementStockInitiative and friends), which is a real, separate claim
            // about the wider simulation, not about whether the filter let the item in — polling and stopping
            // the moment delivery is observed keeps this test about the one claim it makes.
            bool delivered = false;
            for (int i = 0; i < 20 && !delivered; i++)
            {
                RunTicks(100, hauler);
                delivered = woodThing.Spawned && woodThing.Position == stockCell;
            }

            Assert.True(delivered, "the allowed item should have been hauled into the stockpile");
            Assert.NotEqual(stockCell, berriesThing.Position);
        }

        // -------------------------------------------------------------------------------------------
        // UnmarkZone: a cell taken back out of a growing zone is never sown.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void An_unmarked_growing_zone_stops_being_sown()
        {
            Settlement settlement = OpenedSettlement("unmark-a-field");
            CoreMap map = settlement.InteriorMap!;

            Pawn farmer = AnyCitizenOn(settlement, map);
            farmer.workSettings!.DisableAll();
            farmer.workSettings!.SetPriority(WorkTypeDefOf.Growing, 1);

            IntVec3 cell = farmer.Position;
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkGrowingZone("Plant_Potato", new[] { cell }).Outcome);

            MapCommandResult unmarked = MapCommands.UnmarkZone(new[] { cell });
            Assert.Equal(MapCommandOutcome.Done, unmarked.Outcome);
            Assert.Null(map.zoneManager.ZoneAt(cell));

            // Same isolation CookingInitiativeTests/MapCommandsTests use for a Growing-only pawn: strip the
            // wild growth a generated interior ships so there is no other harvest to distract from the claim.
            foreach (Thing wild in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).ToList()) wild.Destroy();

            RunTicks(3000, farmer);

            Assert.DoesNotContain(map.thingGrid.ThingsListAt(cell), t => t.def == Def("Plant_Potato"));
        }

        [Fact]
        public void UnmarkZone_is_a_no_op_when_no_cell_belongs_to_a_zone()
        {
            Settlement settlement = OpenedSettlement("unmark-nothing-there");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);

            MapCommandResult result = MapCommands.UnmarkZone(new[] { cell });

            Assert.Equal(MapCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
        }

        // -------------------------------------------------------------------------------------------
        // SetHomeArea: the load-bearing behaviour change. AreaManager.Home has never had a writer before
        // this, so this is the first time anything in src/ makes it live.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_home_area_actually_bounds_cleaning()
        {
            Settlement settlement = OpenedSettlement("paint-a-home-area");
            CoreMap map = settlement.InteriorMap!;
            map.MapTick(); // let RoomTracker flood once so "outdoors" below is a real reading.

            Pawn someone = AnyCitizenOn(settlement, map);
            IntVec3 farCell = new IntVec3(
                Math.Clamp(someone.Position.x + map.Size.x / 2, 0, map.Size.x - 1),
                0,
                Math.Clamp(someone.Position.z + map.Size.z / 2, 0, map.Size.z - 1));
            IntVec3 cell = FreeCellNear(map, farCell);

            FilthMaker.TryMakeFilth(cell, map, FilthDefOf.Filth_Dirt, "test");
            Assert.False(CleaningBounds.IsCleanable(map, cell),
                "test setup assumption broken: the chosen cell should start out uncleanable open ground");

            MapCommandResult painted = MapCommands.SetHomeArea(new[] { cell }, true);
            Assert.Equal(MapCommandOutcome.Done, painted.Outcome);
            Assert.True(CleaningBounds.IsCleanable(map, cell), "painting the home area should make the cell cleanable");

            Pawn cleaner = NewHuman("Cleaner");
            GenSpawn.Spawn(cleaner, FreeCellNear(map, cell), map);
            cleaner.workSettings!.DisableAll();
            cleaner.workSettings!.SetPriority(WorkTypeDefOf.Cleaning, 1);

            RunTicks(6000, cleaner);

            Assert.DoesNotContain(map.thingGrid.ThingsListAt(cell), t => t is CoreFilth);
        }

        [Fact]
        public void A_home_area_that_excludes_the_whole_settlement_is_accepted_not_refused()
        {
            // "A field on poor soil" and "a stockpile that allows nothing" have their own pins already
            // (MapCommandsTests, SetStockpileFilter's own test below); this is the third unwise-but-legal
            // case the class doc names by name.
            Settlement settlement = OpenedSettlement("unwise-exclude-everything");
            CoreMap map = settlement.InteriorMap!;
            var everyCell = new List<IntVec3>();
            for (int x = 0; x < map.Size.x; x++)
            {
                for (int z = 0; z < map.Size.z; z++) everyCell.Add(new IntVec3(x, 0, z));
            }

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetHomeArea(everyCell, true).Outcome);
            Assert.True(map.areaManager.Home.TrueCount > 0);

            MapCommandResult excluded = MapCommands.SetHomeArea(everyCell, false);

            Assert.Equal(MapCommandOutcome.Done, excluded.Outcome);
            Assert.True(excluded.Changed);
            Assert.Equal(0, map.areaManager.Home.TrueCount);
        }

        // -------------------------------------------------------------------------------------------
        // The design rule, again: a bad decision must be allowed to be bad.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_stockpile_that_allows_nothing_is_accepted_not_refused()
        {
            Settlement settlement = OpenedSettlement("unwise-empty-filter");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { cell }).Outcome);

            MapCommandResult result = MapCommands.SetStockpileFilter(cell, Array.Empty<string>());

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            var stockpile = Assert.IsType<Zone_Stockpile>(map.zoneManager.ZoneAt(cell));
            Assert.Equal(0, stockpile.filter.AllowedDefCount);
        }

        [Fact]
        public void A_bill_for_something_nobody_can_make_yet_is_accepted_not_refused()
        {
            // No research finished, no raw food anywhere on the map: CookMealSimple cannot run right now by
            // any measure. QueueBill still queues it — see MapCommands's own class doc on MapCommands.cs for
            // why refusing this would be the simulation second-guessing the player instead of applying them.
            Settlement settlement = OpenedSettlement("unwise-cant-make-it-yet");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stoveCell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), stoveCell, map);

            MapCommandResult result = MapCommands.QueueBill(stoveCell, "CookMealSimple");

            Assert.Equal(MapCommandOutcome.Done, result.Outcome);
            Assert.True(result.Changed);
        }

        // -------------------------------------------------------------------------------------------
        // The physically impossible, refused with the right named outcome.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void No_game_running_is_refused_as_no_map_for_every_new_command()
        {
            var cell = new IntVec3(1, 0, 1);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.QueueBill(cell, "CookMealSimple").Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.SetBillSuspended(cell, 0, true).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.SetStockpileFilter(cell, new[] { "WoodLog" }).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.UnmarkZone(new[] { cell }).Outcome);
            Assert.Equal(MapCommandOutcome.NoMap, MapCommands.SetHomeArea(new[] { cell }, true).Outcome);
        }

        [Fact]
        public void Every_new_command_refuses_an_off_map_cell()
        {
            Settlement settlement = OpenedSettlement("off-map-for-every-new-command");
            CoreMap map = settlement.InteriorMap!;
            var offMap = new IntVec3(map.Size.x + 5, 0, map.Size.z + 5);

            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.QueueBill(offMap, "CookMealSimple").Outcome);
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.SetBillSuspended(offMap, 0, true).Outcome);
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.SetStockpileFilter(offMap, new[] { "WoodLog" }).Outcome);
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.UnmarkZone(new[] { offMap }).Outcome);
            Assert.Equal(MapCommandOutcome.OffMap, MapCommands.SetHomeArea(new[] { offMap }, true).Outcome);
        }

        [Fact]
        public void QueueBill_and_SetBillSuspended_refuse_a_cell_with_no_bench()
        {
            Settlement settlement = OpenedSettlement("no-bench-here");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);

            Assert.Equal(MapCommandOutcome.Refused, MapCommands.QueueBill(cell, "CookMealSimple").Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.SetBillSuspended(cell, 0, true).Outcome);
        }

        [Fact]
        public void QueueBill_refuses_an_unknown_recipe()
        {
            Settlement settlement = OpenedSettlement("unknown-recipe");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stoveCell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), stoveCell, map);

            MapCommandResult result = MapCommands.QueueBill(stoveCell, "NoSuchRecipeAtAll");

            Assert.Equal(MapCommandOutcome.UnknownDef, result.Outcome);
        }

        [Fact]
        public void QueueBill_refuses_a_recipe_the_bench_cannot_run_at_all()
        {
            Settlement settlement = OpenedSettlement("wrong-bench-for-recipe");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stoveCell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), stoveCell, map);

            // A stonecutting recipe: a real, loaded RecipeDef, just not one FueledStove's own recipeUsers ever
            // names — structurally impossible for this bench, not merely unresearched.
            MapCommandResult result = MapCommands.QueueBill(stoveCell, "Make_Blocks_Sandstone");

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void SetBillSuspended_refuses_an_out_of_range_bill_index()
        {
            Settlement settlement = OpenedSettlement("bad-bill-index");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stoveCell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), stoveCell, map);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.QueueBill(stoveCell, "CookMealSimple").Outcome);

            MapCommandResult result = MapCommands.SetBillSuspended(stoveCell, 5, true);

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void SetStockpileFilter_refuses_a_cell_with_no_stockpile()
        {
            Settlement settlement = OpenedSettlement("no-stockpile-here");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);

            MapCommandResult result = MapCommands.SetStockpileFilter(cell, new[] { "WoodLog" });

            Assert.Equal(MapCommandOutcome.Refused, result.Outcome);
        }

        [Fact]
        public void SetStockpileFilter_refuses_an_unknown_item_def_name_and_leaves_the_filter_untouched()
        {
            Settlement settlement = OpenedSettlement("unknown-item-name");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { cell }).Outcome);
            var stockpile = (Zone_Stockpile)map.zoneManager.ZoneAt(cell)!;
            Assert.True(stockpile.filter.Allows(Def("WoodLog")), "MarkStockpile should start out allowing ordinary goods");

            MapCommandResult result = MapCommands.SetStockpileFilter(cell, new[] { "WoodLog", "NoSuchThingDefAtAll" });

            Assert.Equal(MapCommandOutcome.UnknownDef, result.Outcome);
            // All-or-nothing: a bad name in the batch must not have left the filter half-changed.
            Assert.True(stockpile.filter.Allows(Def("WoodLog")));
        }

        [Fact]
        public void UnmarkZone_and_SetHomeArea_refuse_an_empty_cell_list()
        {
            OpenedSettlement("empty-cell-lists");

            Assert.Equal(MapCommandOutcome.Refused, MapCommands.UnmarkZone(Array.Empty<IntVec3>()).Outcome);
            Assert.Equal(MapCommandOutcome.Refused, MapCommands.SetHomeArea(Array.Empty<IntVec3>(), true).Outcome);
        }
    }
}
