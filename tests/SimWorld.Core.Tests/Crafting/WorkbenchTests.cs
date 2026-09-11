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
    /// Bills at a workbench (system: crafting.workbenches): a pawn who walks to a bench, works a
    /// <see cref="global::SimWorld.Crafting.Bill_Production"/> through the real
    /// <see cref="global::SimWorld.Crafting.WorkGiver_DoBill"/>/<see cref="global::SimWorld.Crafting.JobDriver_DoBill"/>
    /// path, consumes ingredients and produces the recipe's output. <see cref="global::SimWorld.Crafting.Bill"/>,
    /// <see cref="global::SimWorld.Crafting.BillStack"/>, <see cref="global::SimWorld.Crafting.RecipeDef"/>,
    /// <see cref="global::SimWorld.Crafting.BillIngredientsFinder"/> and <see cref="global::SimWorld.Crafting.GenRecipe"/>
    /// already have their own tests in <c>CraftingTests.cs</c>; these pin the seam this pass added — a real
    /// bench Thing on a map, and the giver/driver that actually run it.
    /// </summary>
    [Collection("GlobalDefs")]
    public class WorkbenchTests : ContentTestBase
    {
        public WorkbenchTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static global::SimWorld.Crafting.RecipeDef Recipe(string name) =>
            DefDatabase<global::SimWorld.Crafting.RecipeDef>.GetNamed(name);

        private static WorkGiverDef Giver(string name) => DefDatabase<WorkGiverDef>.GetNamed(name);

        /// <summary>Every other work type disabled, so the priority scan can only ever reach the one
        /// WorkGiverDef under test — isolates these tests from whatever the concurrently developed
        /// hauling/research/doctoring givers do on the same bare map.</summary>
        private static Pawn Worker(CoreMap map, IntVec3 cell, WorkTypeDef workType, string name = "Crafter")
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

        private static void KnowStoneTools() =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));

        [Fact]
        public void A_pawn_smiths_a_knife_through_the_real_work_giver_consumes_steel_and_earns_xp()
        {
            KnowStoneTools();
            CoreMap map = NewMap(10, 10);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(5, 0, 5), "Smithy");
            Thing steel = SpawnStack(map, new IntVec3(5, 0, 6), "Steel", 10);
            Pawn pawn = Worker(map, new IntVec3(2, 0, 2), WorkTypeDefOf.Smithing);

            var bill = new global::SimWorld.Crafting.Bill_Production(Recipe("Make_MeleeWeapon_Knife"))
            {
                repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                repeatCount = 1,
            };
            Comp(bench).BillStack.AddBill(bill);

            float xpBefore = pawn.skills.GetSkill(SkillDefOf.Crafting)!.XpTotalEarned;

            RunTicks(2500, pawn);

            Assert.True(steel.Destroyed || steel.stackCount < 10, "the steel was never consumed");
            Assert.Contains(map.listerThings.AllThings, t => t.def.defName == "MeleeWeapon_Knife");
            Assert.Equal(0, bill.repeatCount);

            float xpAfter = pawn.skills.GetSkill(SkillDefOf.Crafting)!.XpTotalEarned;
            Assert.True(xpAfter > xpBefore, "no Crafting XP was granted for smithing the knife");
        }

        [Fact]
        public void No_job_is_offered_when_the_bench_has_no_ingredients()
        {
            KnowStoneTools();
            CoreMap map = NewMap(8, 8);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(4, 0, 4), "Smithy");
            Pawn pawn = Worker(map, new IntVec3(1, 0, 1), WorkTypeDefOf.Smithing);

            var bill = new global::SimWorld.Crafting.Bill_Production(Recipe("Make_MeleeWeapon_Knife"))
            {
                repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                repeatCount = 1,
            };
            Comp(bench).BillStack.AddBill(bill);

            // Not a single unit of Steel exists anywhere on the map. With every other work type disabled,
            // the only job this pawn could be given is DoBill; idle think nodes behind JobGiver_Work still
            // hand it something to do (wander), so the assertion is "not this job", not "no job at all".
            pawn.jobs.TryFindAndStartJob();

            Assert.NotEqual(global::SimWorld.Crafting.CraftingJobDefOf.DoBill, pawn.jobs.curJob?.def);
            Assert.Equal(1, bill.repeatCount);
        }

        [Fact]
        public void A_repeat_count_bill_stops_once_its_count_is_satisfied()
        {
            KnowStoneTools();
            CoreMap map = NewMap(10, 10);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(5, 0, 5), "Smithy");
            SpawnStack(map, new IntVec3(5, 0, 6), "Steel", 30);
            Pawn pawn = Worker(map, new IntVec3(2, 0, 2), WorkTypeDefOf.Smithing);

            var bill = new global::SimWorld.Crafting.Bill_Production(Recipe("Make_MeleeWeapon_Knife"))
            {
                repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                repeatCount = 2,
            };
            Comp(bench).BillStack.AddBill(bill);

            // Comfortably more ticks than two full iterations cost even at skill 0 (see
            // JobDriver_DoBill.WorkSpeedFactorFromSkillLevel), plus room to walk back and forth.
            RunTicks(6000, pawn);

            Assert.Equal(0, bill.repeatCount);
            Assert.False(bill.ShouldDoNow());
            int knives = map.listerThings.AllThings.Count(t => t.def.defName == "MeleeWeapon_Knife");
            Assert.Equal(2, knives);

            // Enough steel remained for a third knife (30 - 2*10 = 10 left); nothing should touch it once
            // the bill itself has stopped wanting the work.
            int knivesBefore = knives;
            RunTicks(3000, pawn);
            Assert.Equal(knivesBefore, map.listerThings.AllThings.Count(t => t.def.defName == "MeleeWeapon_Knife"));
        }

        [Fact]
        public void A_bill_mid_progress_survives_a_save_and_reload()
        {
            KnowStoneTools();
            CoreMap map = NewMap(10, 10);
            global::SimWorld.Building.Building bench = SpawnBench(map, new IntVec3(5, 0, 5), "Smithy");
            SpawnStack(map, new IntVec3(5, 0, 6), "Steel", 30);
            Pawn pawn = Worker(map, new IntVec3(2, 0, 2), WorkTypeDefOf.Smithing);

            var bill = new global::SimWorld.Crafting.Bill_Production(Recipe("Make_MeleeWeapon_Knife"))
            {
                repeatMode = global::SimWorld.Crafting.BillRepeatMode.RepeatCount,
                repeatCount = 3,
            };
            Comp(bench).BillStack.AddBill(bill);

            // Long enough for one knife, short of what a second would need — genuinely "mid-bill", not
            // freshly queued and not finished.
            RunTicks(1600, pawn);
            int repeatCountBeforeSave = bill.repeatCount;
            Assert.InRange(repeatCountBeforeSave, 1, 2);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing? loadedBench = loaded.listerThings.AllThings.FirstOrDefault(t => t.def.defName == "Smithy");
            Assert.NotNull(loadedBench);
            var loadedBill = Assert.IsType<global::SimWorld.Crafting.Bill_Production>(
                Assert.Single(Comp(loadedBench!).BillStack.Bills));
            Assert.Same(Recipe("Make_MeleeWeapon_Knife"), loadedBill.recipe);
            Assert.Equal(repeatCountBeforeSave, loadedBill.repeatCount);
        }

        [Fact]
        public void CookMeals_and_DoBillsSmith_share_one_giver_class_over_different_bench_content()
        {
            Assert.IsType<global::SimWorld.Crafting.WorkGiver_DoBill>(Giver("CookMeals").Worker);
            Assert.IsType<global::SimWorld.Crafting.WorkGiver_DoBill>(Giver("DoBillsSmith").Worker);
            Assert.IsType<global::SimWorld.Crafting.WorkGiver_DoBill>(Giver("DoBillsTailor").Worker);
            Assert.IsType<global::SimWorld.Crafting.WorkGiver_DoBill>(Giver("DoBillsArt").Worker);
            Assert.IsType<global::SimWorld.Crafting.WorkGiver_DoBill>(Giver("DoBillsCraft").Worker);

            // FueledStove (Cooking) and Smithy (Smithing) both carry the comp, each naming a different
            // WorkTypeDef, which is the entire content-side difference between the five WorkGiverDefs above.
            Assert.Equal(WorkTypeDefOf.Cooking,
                Def("FueledStove").comps!.OfType<global::SimWorld.Things.CompProperties_BillGiver>().Single().workType);
            Assert.Equal(WorkTypeDefOf.Smithing,
                Def("Smithy").comps!.OfType<global::SimWorld.Things.CompProperties_BillGiver>().Single().workType);
        }
    }
}
