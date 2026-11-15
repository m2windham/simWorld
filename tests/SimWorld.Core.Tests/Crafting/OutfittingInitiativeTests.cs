using System.Linq;

using SimWorld.AI;
using SimWorld.Crafting;
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
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// crafting.billcreators, the bench half: <see cref="OutfittingInitiative"/> queues a tailoring or
    /// smithing bill sized by the citizens who lack the product, never with a zero target, at whatever bench
    /// already stands, and the chain closes end to end: cloth on the floor becomes a shirt on a citizen.
    /// </summary>
    public class OutfittingInitiativeTests : ContentTestBase
    {
        public OutfittingInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(16, 16, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreRecipeDef Recipe(string name) => DefDatabase<CoreRecipeDef>.GetNamed(name);

        private static Pawn SpawnHuman(CoreMap map, IntVec3 cell, string name)
        {
            Pawn p = NewHuman(name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static CompBillGiver SpawnBench(CoreMap map, string defName, IntVec3 cell)
        {
            var bench = (ThingWithComps)ThingMaker.MakeThing(Def(defName));
            GenSpawn.Spawn(bench, cell, map);
            return bench.GetComp<CompBillGiver>()!;
        }

        private static Bill_Production? BillOn(CompBillGiver comp, string recipe) =>
            comp.BillStack.Bills.OfType<Bill_Production>().SingleOrDefault(b => b.recipe.defName == recipe);

        [Fact]
        public void The_shipped_shirt_and_knife_recipes_are_outfitting_and_meals_are_not()
        {
            Assert.True(OutfittingInitiative.IsOutfitting(Recipe("Make_Apparel_Shirt")));
            Assert.True(OutfittingInitiative.IsOutfitting(Recipe("Make_MeleeWeapon_Knife")));
            Assert.False(OutfittingInitiative.IsOutfitting(Recipe("CookMealSimple")));
        }

        [Fact]
        public void The_shirt_bill_targets_one_per_citizen_without_a_shirt()
        {
            CoreMap map = NewMap();
            CompBillGiver tailor = SpawnBench(map, "TableTailor", new IntVec3(8, 0, 8));
            SpawnHuman(map, new IntVec3(1, 0, 1), "A");
            SpawnHuman(map, new IntVec3(2, 0, 1), "B");
            Pawn dressed = SpawnHuman(map, new IntVec3(3, 0, 1), "C");
            dressed.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"), Def("Cloth")));

            OutfittingInitiative.Run(map);

            Bill_Production bill = BillOn(tailor, "Make_Apparel_Shirt")!;
            Assert.NotNull(bill);
            Assert.Equal(BillRepeatMode.TargetCount, bill.repeatMode);
            Assert.Equal(2, bill.targetCount);
            Assert.True(bill.unpauseWhenYouHave < bill.targetCount);
        }

        [Fact]
        public void No_bill_is_queued_while_nobody_lacks_the_product_and_a_standing_one_goes_when_the_gap_closes()
        {
            CoreMap map = NewMap();
            CompBillGiver tailor = SpawnBench(map, "TableTailor", new IntVec3(8, 0, 8));
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1), "A");
            OutfittingInitiative.Run(map);
            Assert.NotNull(BillOn(tailor, "Make_Apparel_Shirt"));

            citizen.apparel.Wear((ThingWithComps)ThingMaker.MakeThing(Def("Apparel_Shirt"), Def("Cloth")));
            OutfittingInitiative.Run(map);

            Assert.Null(BillOn(tailor, "Make_Apparel_Shirt"));
            Assert.DoesNotContain(tailor.BillStack.Bills, b => b is Bill_Production p && p.targetCount <= 0);
        }

        [Fact]
        public void The_knife_bill_waits_for_its_research_and_counts_only_unarmed_citizens_who_can_fight()
        {
            CoreMap map = NewMap();
            CompBillGiver smithy = SpawnBench(map, "Smithy", new IntVec3(8, 0, 8));
            SpawnHuman(map, new IntVec3(1, 0, 1), "Unarmed");
            Pawn armed = SpawnHuman(map, new IntVec3(2, 0, 1), "Armed");
            armed.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("MeleeWeapon_Knife")));
            Pawn pacifist = SpawnHuman(map, new IntVec3(3, 0, 1), "Pacifist");
            pacifist.story.adulthood = DefDatabase<BackstoryDef>.GetNamed("TribalElder");

            OutfittingInitiative.Run(map);
            Assert.Null(BillOn(smithy, "Make_MeleeWeapon_Knife"));

            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("StoneTools"));
            OutfittingInitiative.Run(map);

            Assert.Equal(1, BillOn(smithy, "Make_MeleeWeapon_Knife")!.targetCount);
        }

        [Fact]
        public void Cloth_on_the_floor_becomes_a_shirt_on_the_citizen()
        {
            CoreMap map = NewMap();
            SpawnBench(map, "TableTailor", new IntVec3(8, 0, 8));
            Thing cloth = ThingMaker.MakeThing(Def("Cloth"));
            cloth.stackCount = 40;
            GenSpawn.Spawn(cloth, new IntVec3(6, 0, 8), map);
            Pawn citizen = SpawnHuman(map, new IntVec3(1, 0, 1), "Tailor");
            citizen.workSettings.DisableAll();
            citizen.workSettings.SetPriority(WorkTypeDefOf.Tailoring, 3);

            OutfittingInitiative.Run(map);
            // Long enough to tailor it and for the citizen's apparel cooldown (set on the first, empty look,
            // RimWorld's 6000-9000 ticks) to lapse.
            RunTicks(JobGiver_OptimizeApparel.ApparelOptimizeCheckIntervalMax + 1000, citizen);

            Assert.Contains(citizen.apparel.WornApparel, a => a.def.defName == "Apparel_Shirt");
        }
    }
}
