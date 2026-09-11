using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// Ingredient reservation for bills (system: crafting.workbenches). <c>WorkbenchTests</c> pins that one
    /// pawn at one bench crafts a thing; these pin the half that was missing when that landed — a bill takes
    /// out a claim on the exact ingredient stacks it chose, so nothing else (a second bench's bill, a hauler)
    /// can spend the same pile while the first job runs.
    /// <para/>
    /// The bench itself was already reserved before this pass, which is why two pawns at <i>one</i> bench were
    /// never the race: <see cref="global::SimWorld.Crafting.WorkGiver_DoBill.HasJobOnThing"/> refuses a bench
    /// somebody else holds. The race needed two <i>different</i> claims on one pile, which is what these set up.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BillReservationTests : ContentTestBase
    {
        public BillReservationTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static global::SimWorld.Crafting.RecipeDef Recipe(string name) =>
            DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed(name);

        /// <summary>Every work type but one disabled, so the priority scan can only reach the giver under test.</summary>
        private static Pawn Worker(CoreMap map, IntVec3 cell, WorkTypeDef workType, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            p.workSettings.DisableAll();
            p.workSettings.SetPriority(workType, 3);
            return p;
        }

        private static global::SimWorld.Building.Building SpawnBench(CoreMap map, IntVec3 cell, string defName)
        {
            var bench = (global::SimWorld.Building.Building)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(bench, cell, map);
            return bench;
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static global::SimWorld.Things.CompBillGiver Comp(Thing bench) =>
            ((ThingWithComps)bench).GetComp<global::SimWorld.Things.CompBillGiver>()!;

        private static global::SimWorld.Crafting.Bill_Production AddKnifeBill(Thing bench, int repeatCount = 1)
        {
            var bill = new global::SimWorld.Crafting.Bill_Production(Recipe("Make_MeleeWeapon_Knife"))
            {
                repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                repeatCount = repeatCount,
            };
            Comp(bench).BillStack.AddBill(bill);
            return bill;
        }

        private static void KnowStoneTools() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));

        private static bool IsDoingBill(Pawn p) =>
            p.jobs.curJob?.def == global::SimWorld.Crafting.CraftingJobDefOf.DoBill;

        // ---- the race ----

        /// <summary>
        /// Two benches, two crafters, and one pile of steel that covers exactly one knife. Both pawns are
        /// offered work in the same tick; only one may come away with a bill, because the winner's job holds
        /// the pile. Before ingredient reservation landed both got a DoBill job and one of them burned a full
        /// recipe's worth of work for nothing, finding the pile gone at the finish line.
        /// </summary>
        [Fact]
        public void Two_benches_over_one_pile_hand_out_exactly_one_bill_job()
        {
            KnowStoneTools();
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building benchWest = SpawnBench(map, new IntVec3(3, 0, 7), "Smithy");
            global::SimWorld.Building.Building benchEast = SpawnBench(map, new IntVec3(10, 0, 7), "Smithy");

            // Exactly one knife's worth (the recipe asks for 10 Steel), reachable from both benches.
            SpawnStack(map, new IntVec3(7, 0, 7), "Steel", 10);

            AddKnifeBill(benchWest);
            AddKnifeBill(benchEast);

            Pawn west = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing, "West");
            Pawn east = Worker(map, new IntVec3(12, 0, 1), WorkTypeDefOf.Smithing, "East");

            west.jobs.TryFindAndStartJob();
            east.jobs.TryFindAndStartJob();

            int crafting = (IsDoingBill(west) ? 1 : 0) + (IsDoingBill(east) ? 1 : 0);
            Assert.Equal(1, crafting);
        }

        /// <summary>
        /// The same two benches, run to completion: the pile pays for one knife and the loser never spends a
        /// tick of recipe work on a bill it cannot finish. Asserting on the knives as well as the jobs is what
        /// separates "the claim was taken" from "the claim was taken and honoured to the end".
        /// </summary>
        [Fact]
        public void One_pile_pays_for_one_knife_and_the_loser_wastes_no_work()
        {
            KnowStoneTools();
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building benchWest = SpawnBench(map, new IntVec3(3, 0, 7), "Smithy");
            global::SimWorld.Building.Building benchEast = SpawnBench(map, new IntVec3(10, 0, 7), "Smithy");
            SpawnStack(map, new IntVec3(7, 0, 7), "Steel", 10);

            global::SimWorld.Crafting.Bill_Production billWest = AddKnifeBill(benchWest);
            global::SimWorld.Crafting.Bill_Production billEast = AddKnifeBill(benchEast);

            Pawn west = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing, "West");
            Pawn east = Worker(map, new IntVec3(12, 0, 1), WorkTypeDefOf.Smithing, "East");

            // Counted a tick at a time rather than through RunTicks, because "one of them worked and the
            // other did not" is not visible in the end state: before the fix both benches got a bill, both
            // pawns worked a full recipe, and the loser only discovered at its finish line that the steel was
            // gone. The product count alone cannot tell those two runs apart — the tick counts can.
            int westBillTicks = 0;
            int eastBillTicks = 0;
            for (int i = 0; i < 4000; i++)
            {
                RunTicks(1, west, east);
                if (IsDoingBill(west)) westBillTicks++;
                if (IsDoingBill(east)) eastBillTicks++;
            }

            Assert.Single(map.listerThings.AllThings, t => t.def.defName == "MeleeWeapon_Knife");
            Assert.Empty(map.listerThings.ThingsOfDef(Def("Steel")));

            // One bill ran its single iteration; the other never started one, so its count is untouched.
            Assert.Equal(1, billWest.repeatCount + billEast.repeatCount);

            // And the one that never ran spent no time on it at all.
            Assert.True(westBillTicks == 0 || eastBillTicks == 0,
                "both crafters worked the bill: west " + westBillTicks + " ticks, east " + eastBillTicks);
            Assert.True(westBillTicks + eastBillTicks > 0, "neither crafter ever got the bill");
        }

        /// <summary>
        /// Enough steel for both knives and both crafters get one: the claim is on the pile a bill actually
        /// needs, not a blanket "one bill at a time on this map". Without this the previous two tests would
        /// also pass against a fix that simply refused concurrent bills.
        /// </summary>
        [Fact]
        public void Two_separate_piles_let_both_benches_run_at_once()
        {
            KnowStoneTools();
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building benchWest = SpawnBench(map, new IntVec3(3, 0, 7), "Smithy");
            global::SimWorld.Building.Building benchEast = SpawnBench(map, new IntVec3(10, 0, 7), "Smithy");
            SpawnStack(map, new IntVec3(3, 0, 8), "Steel", 10);
            SpawnStack(map, new IntVec3(10, 0, 8), "Steel", 10);

            AddKnifeBill(benchWest);
            AddKnifeBill(benchEast);

            Pawn west = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing, "West");
            Pawn east = Worker(map, new IntVec3(12, 0, 1), WorkTypeDefOf.Smithing, "East");

            west.jobs.TryFindAndStartJob();
            east.jobs.TryFindAndStartJob();

            Assert.True(IsDoingBill(west), "the west crafter was refused a bill it had its own steel for");
            Assert.True(IsDoingBill(east), "the east crafter was refused a bill it had its own steel for");

            RunTicks(4000, west, east);
            Assert.Equal(2, map.listerThings.AllThings.Count(t => t.def.defName == "MeleeWeapon_Knife"));
        }

        /// <summary>
        /// The same race over the cooking half of the family, because <c>CookMeals</c> and <c>DoBillsSmith</c>
        /// are one <see cref="global::SimWorld.Crafting.WorkGiver_DoBill"/> class over different bench content.
        /// Worth its own test rather than trusting that: a meal recipe counts its ingredients in nutrition
        /// rather than pieces (<see cref="global::SimWorld.Crafting.IngredientValueGetter_Nutrition"/>), so it
        /// reaches the chosen stacks down a different arm of <see cref="global::SimWorld.Crafting.BillIngredientsFinder"/>
        /// than a knife does — and it is the counts that end up in the claim.
        /// </summary>
        [Fact]
        public void Two_stoves_over_one_pile_of_potatoes_hand_out_exactly_one_bill_job()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Cooking"));
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building stoveWest = SpawnBench(map, new IntVec3(3, 0, 7), "FueledStove");
            global::SimWorld.Building.Building stoveEast = SpawnBench(map, new IntVec3(10, 0, 7), "FueledStove");

            // A simple meal wants 0.5 nutrition and a potato carries 0.05, so ten is one meal's worth.
            SpawnStack(map, new IntVec3(7, 0, 7), "RawPotatoes", 10);

            foreach (Thing stove in new[] { (Thing)stoveWest, stoveEast })
            {
                Comp(stove).BillStack.AddBill(new global::SimWorld.Crafting.Bill_Production(Recipe("CookMealSimple"))
                {
                    repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                    repeatCount = 1,
                });
            }

            Pawn west = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Cooking, "West");
            Pawn east = Worker(map, new IntVec3(12, 0, 1), WorkTypeDefOf.Cooking, "East");

            west.jobs.TryFindAndStartJob();
            east.jobs.TryFindAndStartJob();

            Assert.Equal(1, (IsDoingBill(west) ? 1 : 0) + (IsDoingBill(east) ? 1 : 0));

            RunTicks(4000, west, east);
            Assert.Single(map.listerThings.AllThings, t => t.def.defName == "MealSimple");
        }

        /// <summary>
        /// A hauler and a crafter want the same pile. Hauling already reserved what it carries
        /// (<see cref="JobDriver_HaulToCell"/> claims both the item and its destination cell), so once a bill
        /// claims its ingredients the two respect each other through the one
        /// <see cref="ReservationManager"/> — the crafter's steel cannot be carried off mid-recipe.
        /// </summary>
        [Fact]
        public void A_hauler_leaves_alone_the_steel_a_bill_has_claimed()
        {
            KnowStoneTools();
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(3, 0, 7), "Smithy");
            Thing steel = SpawnStack(map, new IntVec3(5, 0, 7), "Steel", 10);
            AddKnifeBill(bench);

            // Somewhere for a hauler to want to put it.
            var stockpile = new global::SimWorld.Building.Zone_Stockpile();
            map.zoneManager.RegisterZone(stockpile);
            map.zoneManager.AddCell(stockpile, new IntVec3(12, 0, 12));
            stockpile.filter.SetAllow(Def("Steel"), true);

            Pawn crafter = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing, "Crafter");
            Pawn hauler = Worker(map, new IntVec3(12, 0, 1), WorkTypeDefOf.Hauling, "Hauler");

            crafter.jobs.TryFindAndStartJob();
            Assert.True(IsDoingBill(crafter), "the crafter never got the bill this test is about");

            hauler.jobs.TryFindAndStartJob();
            Assert.NotEqual(JobDefOf.HaulToCell, hauler.jobs.curJob?.def);
            Assert.False(map.reservationManager.IsReservedBy(hauler, steel));
        }

        // ---- the pieces the fix is built from ----

        /// <summary>
        /// <see cref="ReservationManager"/> claims a count out of a stack, not only the whole Thing
        /// (RimWorld: <c>Reserve(..., int stackCount)</c>, <c>StackCount_All</c>). Two claimants may share one
        /// pile so long as their counts fit inside it; the one that would overdraw it is refused.
        /// </summary>
        [Fact]
        public void A_stack_can_be_reserved_by_the_piece_until_it_runs_out()
        {
            CoreMap map = NewMap(8, 8);
            Thing pile = SpawnStack(map, new IntVec3(4, 0, 4), "Steel", 10);
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            GenSpawn.Spawn(a, new IntVec3(1, 0, 1), map);
            GenSpawn.Spawn(b, new IntVec3(2, 0, 1), map);

            Assert.True(map.reservationManager.Reserve(a, pile, maxClaimants: 2, stackCount: 4));

            // 6 of the remaining 6 is fine; one more piece than the pile holds is not.
            Assert.False(map.reservationManager.CanReserve(b, pile, maxClaimants: 2, stackCount: 7));
            Assert.True(map.reservationManager.CanReserve(b, pile, maxClaimants: 2, stackCount: 6));
            Assert.True(map.reservationManager.Reserve(b, pile, maxClaimants: 2, stackCount: 6));

            // Now the pile is fully spoken for.
            Pawn c = NewHuman("C");
            GenSpawn.Spawn(c, new IntVec3(3, 0, 1), map);
            Assert.False(map.reservationManager.CanReserve(c, pile, maxClaimants: 2, stackCount: 1));

            // A whole-Thing claim (StackCount_All) still excludes everyone, which is what every job that
            // does not ask for a count gets by default.
            map.reservationManager.ReleaseAllClaimedBy(a);
            map.reservationManager.ReleaseAllClaimedBy(b);
            Assert.True(map.reservationManager.Reserve(a, pile));
            Assert.False(map.reservationManager.CanReserve(b, pile, maxClaimants: 2, stackCount: 1));
        }

        /// <summary>
        /// A <see cref="Job"/> carries queues of targets beside its three fixed ones (RimWorld:
        /// <c>targetQueueA</c>/<c>targetQueueB</c>/<c>countQueue</c>), and they survive a save.
        /// </summary>
        [Fact]
        public void A_jobs_target_queues_round_trip_through_a_save()
        {
            CoreMap map = NewMap(8, 8);
            Thing steel = SpawnStack(map, new IntVec3(4, 0, 4), "Steel", 30);
            Thing wood = SpawnStack(map, new IntVec3(5, 0, 4), "WoodLog", 12);
            Pawn pawn = NewHuman("Crafter");
            GenSpawn.Spawn(pawn, new IntVec3(1, 0, 1), map);

            var job = new Job(global::SimWorld.Crafting.CraftingJobDefOf.DoBill, steel);
            job.AddQueuedTarget(TargetIndex.B, steel);
            job.AddQueuedTarget(TargetIndex.B, wood);
            job.countQueue = new List<int> { 10, 4 };
            job.AddQueuedTarget(TargetIndex.A, new IntVec3(6, 0, 6));
            pawn.jobs.StartJob(job);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedPawn = loaded.mapPawns.AllPawns.Single(p => p.Label == "Crafter");
            Job? loadedJob = loadedPawn.jobs.curJob;
            Assert.NotNull(loadedJob);

            List<LocalTargetInfo> queueB = loadedJob!.GetTargetQueue(TargetIndex.B);
            Assert.Equal(2, queueB.Count);
            Assert.Equal(new[] { "Steel", "WoodLog" }, queueB.Select(t => t.Thing!.def.defName));
            Assert.Equal(new[] { 10, 4 }, loadedJob.countQueue!);

            // The cell-only half of a queued target has no cross-ref bank to lean on; pin it too.
            List<LocalTargetInfo> queueA = loadedJob.GetTargetQueue(TargetIndex.A);
            Assert.Equal(new IntVec3(6, 0, 6), Assert.Single(queueA).Cell);
        }

        /// <summary>A live bill's ingredient claims survive a save and reload, so a reloaded colony does not
        /// re-offer the same pile to a second bench.</summary>
        [Fact]
        public void Ingredient_claims_survive_a_save_and_reload()
        {
            KnowStoneTools();
            CoreMap map = NewMap(14, 14);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(3, 0, 7), "Smithy");
            SpawnStack(map, new IntVec3(5, 0, 7), "Steel", 10);
            AddKnifeBill(bench);
            Pawn crafter = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing, "Crafter");

            crafter.jobs.TryFindAndStartJob();
            Assert.True(IsDoingBill(crafter));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Pawn loadedCrafter = loaded.mapPawns.AllPawns.Single(p => p.Label == "Crafter");
            Thing loadedSteel = loaded.listerThings.ThingsOfDef(Def("Steel")).Single();
            Assert.True(loaded.reservationManager.IsReservedBy(loadedCrafter, loadedSteel),
                "the reloaded crafter no longer holds the steel its bill chose");
        }
    }
}
