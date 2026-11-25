using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// REQ 005: every lever <see cref="MapCommands"/> sets is readable back through
    /// <see cref="MapViewSnapshot.CaptureLevers()"/>. Each test sets a value the way a host would — through the
    /// command and nothing else — and reads the same value off the capture, so a read that drifts from what the
    /// command writes fails here rather than on the player's screen.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewLeversTests : ContentTestBase
    {
        public MapViewLeversTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static Pawn AnyCitizenOn(Settlement settlement, CoreMap map) =>
            settlement.Citizens.First(p => p.Spawned && p.Map == map);

        private static IntVec3 FreeCellNear(CoreMap map, IntVec3 near, ICollection<IntVec3>? taken = null)
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
                if (taken != null && taken.Contains(candidate)) continue;
                return candidate;
            }
            throw new InvalidOperationException("No free cell found near " + near + ".");
        }

        private static IntVec3 PlaceStove(CoreMap map, Pawn near)
        {
            IntVec3 cell = FreeCellNear(map, near.Position);
            GenSpawn.Spawn(ThingMaker.MakeThing(Def("FueledStove")), cell, map);
            return cell;
        }

        private static BenchView BenchAt(SettlementLevers levers, IntVec3 cell) =>
            levers.Benches.Single(b => b.Position == cell);

        // ---- absence ----

        [Fact]
        public void With_no_game_the_levers_report_absence_rather_than_throwing()
        {
            SettlementLevers levers = MapViewSnapshot.CaptureLevers();

            Assert.False(levers.HasMap);
            Assert.False(string.IsNullOrEmpty(levers.AbsenceReason));
            Assert.Empty(levers.Benches);
            Assert.Empty(levers.Zones);
            Assert.Empty(levers.Citizens);
            Assert.Equal(0, levers.Home.TrueCount);
        }

        // ---- 1. bills ----

        [Fact]
        public void A_queued_bill_reads_back_and_suspending_it_reads_back()
        {
            Settlement settlement = OpenedSettlement("levers-bills");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stove = PlaceStove(map, AnyCitizenOn(settlement, map));

            Assert.Empty(BenchAt(MapViewSnapshot.CaptureLevers(), stove).Bills);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.QueueBill(stove, "CookMealSimple").Outcome);
            BenchView bench = BenchAt(MapViewSnapshot.CaptureLevers(), stove);
            Assert.Equal("FueledStove", bench.DefName);
            Assert.Equal(Def("FueledStove").LabelCap, bench.Label);
            BillView bill = Assert.Single(bench.Bills);
            Assert.Equal(0, bill.Index);
            Assert.Equal("CookMealSimple", bill.RecipeDefName);
            Assert.Equal(DefDatabase<RecipeDef>.GetNamed("CookMealSimple").LabelCap, bill.RecipeLabel);
            Assert.NotEqual(bill.RecipeDefName, bill.RecipeLabel);
            Assert.False(bill.Suspended);
            Assert.Equal(BillRepeatMode.Forever, bill.RepeatMode);
            Assert.Null(bill.RepeatCountLeft);
            Assert.Null(bill.TargetCount);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetBillSuspended(stove, 0, true).Outcome);
            Assert.True(BenchAt(MapViewSnapshot.CaptureLevers(), stove).Bills[0].Suspended);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetBillSuspended(stove, 0, false).Outcome);
            Assert.False(BenchAt(MapViewSnapshot.CaptureLevers(), stove).Bills[0].Suspended);
        }

        [Fact]
        public void Bills_read_in_stack_order_with_each_modes_count()
        {
            Settlement settlement = OpenedSettlement("levers-bill-modes");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stove = PlaceStove(map, AnyCitizenOn(settlement, map));
            var comp = ((ThingWithComps)map.thingGrid.ThingsListAt(stove).Single(t => t.def.defName == "FueledStove"))
                .GetComp<CompBillGiver>()!;

            // An initiative-style TargetCount bill first, then the player's Forever one, then a finite batch:
            // the read keeps BillStack order, which is the index SetBillSuspended takes.
            RecipeDef meal = DefDatabase<RecipeDef>.GetNamed("CookMealSimple");
            comp.BillStack.AddBill(new Bill_Production(meal) { repeatMode = BillRepeatMode.TargetCount, targetCount = 7 });
            Assert.Equal(MapCommandOutcome.Done, MapCommands.QueueBill(stove, "CookMealSimple").Outcome);
            comp.BillStack.AddBill(new Bill_Production(meal) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 3 });

            IReadOnlyList<BillView> bills = BenchAt(MapViewSnapshot.CaptureLevers(), stove).Bills;
            Assert.Equal(new[] { 0, 1, 2 }, bills.Select(b => b.Index));
            Assert.Equal(
                new BillRepeatMode?[] { BillRepeatMode.TargetCount, BillRepeatMode.Forever, BillRepeatMode.RepeatCount },
                bills.Select(b => b.RepeatMode));
            Assert.Equal(7, bills[0].TargetCount);
            Assert.Null(bills[0].RepeatCountLeft);
            Assert.Equal(3, bills[2].RepeatCountLeft);
            Assert.Null(bills[2].TargetCount);
        }

        [Fact]
        public void Reading_a_target_count_bill_does_not_change_its_paused_state()
        {
            // Bill_Production.ShouldDoNow rewrites `paused` on a TargetCount bill. The read must not call it:
            // reading the levers may not change what a crafter does next.
            Settlement settlement = OpenedSettlement("levers-no-side-effects");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stove = PlaceStove(map, AnyCitizenOn(settlement, map));
            var comp = ((ThingWithComps)map.thingGrid.ThingsListAt(stove).Single(t => t.def.defName == "FueledStove"))
                .GetComp<CompBillGiver>()!;
            // Target 0: ShouldDoNow would pause it at once, since the map holds >= 0 meals.
            var bill = new Bill_Production(DefDatabase<RecipeDef>.GetNamed("CookMealSimple"))
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = 0,
            };
            comp.BillStack.AddBill(bill);

            BillView read = BenchAt(MapViewSnapshot.CaptureLevers(), stove).Bills.Single();

            Assert.False(read.Paused);
            Assert.False(bill.paused, "capturing the levers changed a bill's state");
        }

        // ---- 2. a bench's recipes ----

        [Fact]
        public void A_bench_lists_its_recipes_and_whether_each_is_researched()
        {
            Settlement settlement = OpenedSettlement("levers-recipes");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 stove = PlaceStove(map, AnyCitizenOn(settlement, map));
            RecipeDef meal = DefDatabase<RecipeDef>.GetNamed("CookMealSimple");

            BenchView before = BenchAt(MapViewSnapshot.CaptureLevers(), stove);
            Assert.Equal(Def("FueledStove").AllRecipes.Select(r => r.defName), before.Recipes.Select(r => r.DefName));
            RecipeOption cook = before.Recipes.Single(r => r.DefName == "CookMealSimple");
            Assert.Equal(meal.LabelCap, cook.Label);
            Assert.Equal(meal.AvailableNow, cook.Researched);

            foreach (ResearchProjectDef p in meal.researchPrerequisites!) Find.ResearchManager.FinishProject(p);
            Assert.True(BenchAt(MapViewSnapshot.CaptureLevers(), stove).Recipes.Single(r => r.DefName == "CookMealSimple").Researched);
        }

        [Fact]
        public void The_catalogue_lists_every_bill_giving_bench_def_with_its_recipes()
        {
            IReadOnlyList<BenchRecipes> benches = LeverCatalogue.BillGivers();

            BenchRecipes stove = benches.Single(b => b.BenchDefName == "FueledStove");
            Assert.Equal(Def("FueledStove").LabelCap, stove.BenchLabel);
            Assert.Equal(Def("FueledStove").AllRecipes.Select(r => r.defName), stove.Recipes.Select(r => r.DefName));
            Assert.Contains(stove.Recipes, r => r.DefName == "CookMealSimple");

            Assert.Equal(benches.Select(b => b.BenchDefName).OrderBy(n => n, StringComparer.Ordinal), benches.Select(b => b.BenchDefName));
            Assert.DoesNotContain(benches, b => b.BenchDefName == "Steel");
            Assert.Equal(stove.Recipes.Select(r => r.DefName), LeverCatalogue.RecipesOf("FueledStove").Select(r => r.DefName));
            Assert.Empty(LeverCatalogue.RecipesOf("Steel"));
            Assert.Empty(LeverCatalogue.RecipesOf("NoSuchBench"));
        }

        // ---- 3 and 4. zones and stockpile filters ----

        [Fact]
        public void A_marked_stockpile_reads_back_with_its_cells_and_its_filter()
        {
            Settlement settlement = OpenedSettlement("levers-stockpile");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            var cells = new List<IntVec3>();
            cells.Add(FreeCellNear(map, citizen.Position + new IntVec3(3, 0, 3)));
            cells.Add(FreeCellNear(map, cells[0], cells));
            int zonesBefore = MapViewSnapshot.CaptureLevers().Zones.Count;

            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(cells).Outcome);
            SettlementLevers marked = MapViewSnapshot.CaptureLevers();
            Assert.Equal(zonesBefore + 1, marked.Zones.Count);
            ZoneView zone = marked.Zones.Single(z => z.Cells.Contains(cells[0]));
            Assert.Equal(ZoneKind.Stockpile, zone.Kind);
            Assert.False(string.IsNullOrEmpty(zone.Label));
            Assert.Equal(cells.OrderBy(c => c.x).ThenBy(c => c.z), zone.Cells.OrderBy(c => c.x).ThenBy(c => c.z));
            Assert.Contains(zone.AnyCell, cells);
            Assert.Null(zone.PlantDefToGrow);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetStockpileFilter(zone.AnyCell, new[] { "WoodLog", "Steel" }).Outcome);
            ZoneView filtered = MapViewSnapshot.CaptureLevers().Zones.Single(z => z.Cells.Contains(cells[0]));
            Assert.Equal(new[] { "Steel", "WoodLog" }, filtered.Allowed.Select(a => a.DefName));
            Assert.Equal(new[] { Def("Steel").LabelCap, Def("WoodLog").LabelCap }, filtered.Allowed.Select(a => a.Label));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetStockpileFilter(zone.AnyCell, Array.Empty<string>()).Outcome);
            Assert.Empty(MapViewSnapshot.CaptureLevers().Zones.Single(z => z.Cells.Contains(cells[0])).Allowed);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.UnmarkZone(cells).Outcome);
            SettlementLevers unmarked = MapViewSnapshot.CaptureLevers();
            Assert.Equal(zonesBefore, unmarked.Zones.Count);
            Assert.DoesNotContain(unmarked.Zones, z => z.Cells.Contains(cells[0]));
        }

        [Fact]
        public void A_marked_growing_zone_reads_back_with_its_crop()
        {
            Settlement settlement = OpenedSettlement("levers-growing");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 cell = FreeCellNear(map, citizen.Position + new IntVec3(-3, 0, 2));

            MapCommandResult result = MapCommands.MarkGrowingZone("Plant_Potato", new[] { cell });
            Assert.Equal(MapCommandOutcome.Done, result.Outcome);

            ZoneView zone = MapViewSnapshot.CaptureLevers().Zones.Single(z => z.Cells.Contains(cell));
            Assert.Equal(ZoneKind.Growing, zone.Kind);
            Assert.Equal("Plant_Potato", zone.PlantDefToGrow);
            Assert.True(zone.AllowSow);
            Assert.Empty(zone.Allowed);
        }

        [Fact]
        public void The_default_stockpile_filter_reads_as_storable_things_without_corpses()
        {
            Settlement settlement = OpenedSettlement("levers-default-filter");
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCellNear(map, AnyCitizenOn(settlement, map).Position + new IntVec3(2, 0, -3));
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(new[] { cell }).Outcome);

            ZoneView zone = MapViewSnapshot.CaptureLevers().Zones.Single(z => z.Cells.Contains(cell));
            HashSet<string> storable = LeverCatalogue.StorableThings().Select(s => s.DefName).ToHashSet();

            Assert.NotEmpty(zone.Allowed);
            Assert.All(zone.Allowed, a => Assert.Contains(a.DefName, storable));
            Assert.Contains(zone.Allowed, a => a.DefName == "Steel");
            Assert.DoesNotContain(zone.Allowed, a => a.DefName.StartsWith("Corpse_", StringComparison.Ordinal));
        }

        [Fact]
        public void The_storable_list_is_items_only_in_defName_order()
        {
            IReadOnlyList<DefOption> storable = LeverCatalogue.StorableThings();

            Assert.Contains(storable, s => s.DefName == "Steel");
            Assert.Contains(storable, s => s.DefName == "WoodLog");
            Assert.DoesNotContain(storable, s => s.DefName == "FueledStove");
            Assert.All(storable, s => Assert.True(Def(s.DefName).EverHaulable, s.DefName));
            Assert.Equal(storable.Select(s => s.DefName).OrderBy(n => n, StringComparer.Ordinal), storable.Select(s => s.DefName));
        }

        // ---- 5. areas ----

        [Fact]
        public void Painted_home_and_snow_clear_cells_read_back_in_their_masks()
        {
            Settlement settlement = OpenedSettlement("levers-areas");
            CoreMap map = settlement.InteriorMap!;
            var cells = new[] { new IntVec3(1, 0, 2), new IntVec3(2, 0, 2) };

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetHomeArea(cells, true).Outcome);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetSnowClearArea(cells, true).Outcome);
            SettlementLevers painted = MapViewSnapshot.CaptureLevers();
            Assert.Equal(map.Size.x, painted.Home.SizeX);
            Assert.Equal(map.cellIndices.NumGridCells, painted.Home.Cells.Count);
            Assert.All(cells, c => Assert.True(painted.Home.Contains(c.x, c.z)));
            Assert.All(cells, c => Assert.True(painted.SnowClear.Contains(c.x, c.z)));
            Assert.Equal(map.areaManager.Home.TrueCount, painted.Home.TrueCount);
            Assert.Equal(map.areaManager.SnowClear.TrueCount, painted.SnowClear.TrueCount);
            Assert.Equal(painted.Home.TrueCount, painted.Home.Cells.Count(b => b));

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetHomeArea(cells, false).Outcome);
            SettlementLevers cleared = MapViewSnapshot.CaptureLevers();
            Assert.All(cells, c => Assert.False(cleared.Home.Contains(c.x, c.z)));
            Assert.All(cells, c => Assert.True(cleared.SnowClear.Contains(c.x, c.z)));
        }

        // ---- 6. medical care ----

        [Fact]
        public void Medical_care_reads_back_what_was_set()
        {
            OpenedSettlement("levers-medical");
            Assert.Equal(MedicalCareCategory.Best, MapViewSnapshot.CaptureLevers().MedicalCare);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.SetMedicalCare(MedicalCareCategory.NoMeds).Outcome);
            Assert.Equal(MedicalCareCategory.NoMeds, MapViewSnapshot.CaptureLevers().MedicalCare);
        }

        // ---- 7. under orders ----

        [Fact]
        public void An_ordered_citizen_reads_as_under_orders_until_cancelled()
        {
            Settlement settlement = OpenedSettlement("levers-orders");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            CitizenOrdersView Line() => MapViewSnapshot.CaptureLevers().Citizens.Single(c => c.ThingId == citizen.thingIDNumber);

            Assert.False(Line().HasOrders);
            Assert.Equal(citizen.Label, Line().Name);

            IntVec3 target = FreeCellNear(map, citizen.Position + new IntVec3(4, 0, 0));
            Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(citizen.thingIDNumber, "Goto", target).Outcome);
            CitizenOrdersView ordered = Line();
            Assert.True(ordered.OnPlayerOrder);
            Assert.Equal("Goto", ordered.CurrentJobDefName);
            Assert.True(ordered.HasOrders);

            citizen.jobs!.QueueJob(new Job(DefDatabase<JobDef>.GetNamed("Goto"), new LocalTargetInfo(target)) { playerForced = true });
            citizen.jobs!.QueueJob(new Job(DefDatabase<JobDef>.GetNamed("Goto"), new LocalTargetInfo(target)));
            Assert.Equal(1, Line().QueuedOrders);

            Assert.Equal(MapCommandOutcome.Done, MapCommands.CancelOrder(citizen.thingIDNumber).Outcome);
            CitizenOrdersView cancelled = Line();
            Assert.False(cancelled.OnPlayerOrder);
            Assert.Equal(0, cancelled.QueuedOrders);
            Assert.False(cancelled.HasOrders);
        }

        // ---- 8. orderable jobs ----

        [Fact]
        public void Orderable_jobs_resolve_in_content_and_name_their_target_kind()
        {
            IReadOnlyList<OrderableJob> jobs = LeverCatalogue.OrderableJobs();

            Assert.Equal(10, jobs.Count);
            Assert.Equal(jobs.Count, jobs.Select(j => j.DefName).Distinct().Count());
            Assert.All(jobs, j =>
            {
                JobDef def = DefDatabase<JobDef>.GetNamed(j.DefName);
                Assert.Equal(def.LabelCap, j.Label);
                Assert.False(string.IsNullOrEmpty(j.TargetHint));
            });
            Assert.Equal(OrderTargetKind.Cell, jobs.Single(j => j.DefName == "Goto").Target);
            Assert.Equal(OrderTargetKind.Thing, jobs.Single(j => j.DefName == "Mine").Target);
            Assert.Equal(OrderTargetKind.Thing, jobs.Single(j => j.DefName == "TendPatient").Target);
        }

        [Fact]
        public void Every_cell_orderable_job_is_accepted_and_taken_as_the_citizens_job()
        {
            Settlement settlement = OpenedSettlement("levers-orderable-cells");
            CoreMap map = settlement.InteriorMap!;
            Pawn citizen = AnyCitizenOn(settlement, map);
            IntVec3 target = FreeCellNear(map, citizen.Position + new IntVec3(3, 0, 0));

            foreach (OrderableJob job in LeverCatalogue.OrderableJobs().Where(j => j.Target == OrderTargetKind.Cell))
            {
                Assert.Equal(MapCommandOutcome.Done, MapCommands.OrderJob(citizen.thingIDNumber, job.DefName, target).Outcome);
                Assert.Equal(job.DefName, citizen.jobs!.curJob?.def.defName);
                Assert.True(citizen.jobs.curJob!.playerForced);
                MapCommands.CancelOrder(citizen.thingIDNumber);
            }
        }

        // ---- labels ----

        [Fact]
        public void Labels_resolve_from_content_for_every_kind_of_handle_the_seam_hands_out()
        {
            Assert.Equal(DefDatabase<RecipeDef>.GetNamed("CookMealSimple").LabelCap, DefLabels.LabelOf("CookMealSimple"));
            Assert.Equal(Def("FueledStove").LabelCap, DefLabels.LabelOf("FueledStove"));
            Assert.Equal(Def("Steel").LabelCap, DefLabels.LabelOf("Steel"));
            Assert.Equal(TerrainDefOf.Soil.LabelCap, DefLabels.LabelOf(TerrainDefOf.Soil.defName));
            Assert.Equal(JobDefOf.Mine.LabelCap, DefLabels.LabelOf("Mine"));
            Assert.Equal("Cook a plain meal", DefLabels.LabelOf("CookMealSimple"));

            Assert.Equal(Def("Steel").description, DefLabels.DescriptionOf("Steel"));
            Assert.True(DefLabels.IsKnown("Steel"));
        }

        [Fact]
        public void An_unknown_defName_labels_as_itself_and_has_no_description()
        {
            Assert.Equal("NoSuchDefAnywhere", DefLabels.LabelOf("NoSuchDefAnywhere"));
            Assert.Null(DefLabels.DescriptionOf("NoSuchDefAnywhere"));
            Assert.False(DefLabels.IsKnown("NoSuchDefAnywhere"));
            Assert.Equal(string.Empty, DefLabels.LabelOf(null));
            Assert.Null(DefLabels.DescriptionOf(""));
        }

        [Fact]
        public void A_name_shared_across_def_types_resolves_to_the_thing()
        {
            // A defName is unique per Def type only. Find a real collision in content if there is one; the
            // ThingDef must win, since things are what the seam names most.
            var thingNames = DefDatabase<ThingDef>.AllDefsListForReading.Select(d => d.defName).ToHashSet();
            Def? other = DefDatabase.Global.AllDefs.FirstOrDefault(d => !(d is ThingDef) && thingNames.Contains(d.defName));
            if (other == null) return; // No collision in shipped content: nothing to disambiguate.
            Assert.Equal(Def(other.defName).LabelCap, DefLabels.LabelOf(other.defName));
        }
    }
}
