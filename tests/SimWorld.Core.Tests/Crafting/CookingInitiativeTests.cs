using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreRecipeDef = SimWorld.Crafting.RecipeDef;

namespace SimWorld.Tests.Crafting
{
    /// <summary>
    /// A settlement raising its own kitchen and keeping a standing meal bill on it
    /// (<see cref="CookingInitiative"/>).
    ///
    /// <para/><b>Two holes, and both had to close together.</b> Nothing in <c>src/</c> queued a cooking bill
    /// — <c>StonecutterInitiative</c> had closed the "nobody ever creates a bill" gap for stone only, leaving
    /// cooking as the one remaining bench whose demand figure is real (every citizen eats). And
    /// <c>FueledStove</c> had no Blueprint/Frame pair authored at all, so
    /// <see cref="GenConstruct.BlueprintDefFor"/> returned null for it and no stove could be placed on a map
    /// by anything: the only stove that had ever existed in this repository was spawned by a test.
    ///
    /// <para/>Why it is worth a system rather than flavour: <c>CookMealSimple</c> spends 0.5 nutrition of raw
    /// food and makes a 0.9-nutrition meal, so cooking is the only step in this port's food chain that
    /// returns more than it takes. The first test below asserts that off content, because if a future recipe
    /// edit made cooking a net loss, this whole initiative would be working against the settlement.
    /// </summary>
    public class CookingInitiativeTests : ContentTestBase
    {
        public CookingInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 24) => new CoreMap(size, size, TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static void KnowCooking()
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Fire"));
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Cooking"));
        }

        private static Thing SpawnStack(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing t = ThingMaker.MakeThing(Def(defName));
            t.stackCount = count;
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        private static Settlement SettlementWith(CoreMap map, int citizens)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Cooktown", 0);
            for (int i = 0; i < citizens; i++)
            {
                settlement.AddCitizen(new SimWorld.Pawns.Pawn(Human, "Cook" + i));
            }
            return settlement;
        }

        private static IEnumerable<ThingDef> BlueprintEntities(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint>()
                .Select(bp => bp.EntityToBuild!);

        [Fact]
        public void Cooking_returns_more_nutrition_than_it_consumes()
        {
            // Read off content, for every cooking recipe content ships. If this ever flips, a settlement that
            // cooks is worse off than one that does not and this whole initiative is a liability.
            List<CoreRecipeDef> cooking = DefDatabase<CoreRecipeDef>.AllDefsListForReading
                .Where(CookingInitiative.IsCooking).ToList();
            Assert.NotEmpty(cooking);

            foreach (CoreRecipeDef recipe in cooking)
            {
                float consumed = recipe.ingredients!.Sum(i => i.count);
                float produced = recipe.products!.Sum(p => (p.thingDef?.ingestible?.nutrition ?? 0f) * p.count);
                Assert.True(produced > consumed,
                    recipe.defName + " turns " + consumed + " nutrition of ingredients into " + produced);
            }
        }

        [Fact]
        public void Every_cooking_recipe_names_a_bench_that_can_actually_be_built()
        {
            // The gap that made cooking unreachable from the bench side: a recipeUsers entry with no
            // Blueprint content behind it is a kitchen nothing can raise.
            foreach (CoreRecipeDef recipe in DefDatabase<CoreRecipeDef>.AllDefsListForReading.Where(CookingInitiative.IsCooking))
            {
                foreach (ThingDef bench in recipe.recipeUsers ?? new List<ThingDef>())
                {
                    Assert.NotNull(GenConstruct.BlueprintDefFor(bench));
                    Assert.NotNull(GenConstruct.FrameDefFor(bench));
                }
            }
        }

        [Fact]
        public void A_settlement_that_knows_cooking_and_has_raw_food_raises_a_stove()
        {
            CoreMap map = NewMap();
            KnowCooking();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);

            CookingInitiative.Run(map);

            Assert.Contains(Def("FueledStove"), BlueprintEntities(map));
        }

        [Fact]
        public void It_puts_the_stove_within_reach_of_the_food_it_cooks()
        {
            // WorkGiver_DoBill only offers a bill whose ingredients already lie within its search radius of
            // the bench, so a stove anywhere else is a bench nobody can ever work.
            CoreMap map = NewMap(40);
            KnowCooking();
            var foodCell = new IntVec3(30, 0, 30);
            SpawnStack(map, foodCell, "RawBerries", 60);

            CookingInitiative.Run(map);

            Thing blueprint = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .First(b => b is Blueprint bp && ReferenceEquals(bp.EntityToBuild, Def("FueledStove")));
            int distance = (blueprint.Position - foodCell).LengthHorizontalSquared;
            Assert.True(distance <= WorkGiver_DoBill.IngredientSearchRadius * WorkGiver_DoBill.IngredientSearchRadius * 2,
                "the stove was planted " + distance + " (squared cells) from the nearest ingredient");
        }

        [Fact]
        public void A_settlement_that_has_not_learned_to_cook_raises_nothing()
        {
            CoreMap map = NewMap();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);

            CookingInitiative.Run(map);

            Assert.DoesNotContain(Def("FueledStove"), BlueprintEntities(map));
        }

        [Fact]
        public void A_settlement_with_nothing_raw_on_the_map_raises_nothing()
        {
            CoreMap map = NewMap();
            KnowCooking();

            CookingInitiative.Run(map);

            Assert.DoesNotContain(Def("FueledStove"), BlueprintEntities(map));
        }

        [Fact]
        public void A_built_stove_gets_a_standing_meal_bill()
        {
            CoreMap map = NewMap();
            KnowCooking();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);
            Thing stove = ThingMaker.MakeThing(Def("FueledStove"));
            GenSpawn.Spawn(stove, new IntVec3(11, 0, 12), map);
            // Mouths on the map: the bill's target is the settlement's own nutrition demand, and a kitchen
            // with nobody to cook for correctly asks for nothing (the zero-target rule StonecutterInitiative
            // wrote and this class obeys).
            GenSpawn.Spawn(NewHuman("Eater"), new IntVec3(10, 0, 12), map);

            CookingInitiative.Run(map);

            CompBillGiver comp = ((ThingWithComps)stove).GetComp<CompBillGiver>()!;
            Assert.NotEmpty(comp.BillStack.Bills);
            Assert.All(comp.BillStack.Bills, b => Assert.True(CookingInitiative.IsCooking(b.recipe)));
            Assert.All(comp.BillStack.Bills, b => Assert.True(((Bill_Production)b).targetCount > 0));
        }

        [Fact]
        public void A_second_pass_does_not_queue_the_same_bill_twice()
        {
            CoreMap map = NewMap();
            KnowCooking();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);
            Thing stove = ThingMaker.MakeThing(Def("FueledStove"));
            GenSpawn.Spawn(stove, new IntVec3(11, 0, 12), map);
            // Mouths on the map: the bill's target is the settlement's own nutrition demand, and a kitchen
            // with nobody to cook for correctly asks for nothing (the zero-target rule StonecutterInitiative
            // wrote and this class obeys).
            GenSpawn.Spawn(NewHuman("Eater"), new IntVec3(10, 0, 12), map);

            CookingInitiative.Run(map);
            int after = ((ThingWithComps)stove).GetComp<CompBillGiver>()!.BillStack.Count;
            CookingInitiative.Run(map);

            Assert.Equal(after, ((ThingWithComps)stove).GetComp<CompBillGiver>()!.BillStack.Count);
        }

        [Fact]
        public void A_kitchen_with_nobody_to_cook_for_queues_nothing()
        {
            // StonecutterInitiative's own rule, obeyed here: a bill whose target is zero must not be queued,
            // because a bill that can never run looks exactly like one that should.
            CoreMap map = NewMap();
            KnowCooking();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);
            Thing stove = ThingMaker.MakeThing(Def("FueledStove"));
            GenSpawn.Spawn(stove, new IntVec3(11, 0, 12), map);

            CookingInitiative.Run(map);

            Assert.Empty(((ThingWithComps)stove).GetComp<CompBillGiver>()!.BillStack.Bills);
        }

        [Fact]
        public void A_bigger_settlement_asks_for_more_meals()
        {
            // The demand figure is shared with the hunting gate and the field size on purpose, so this is a
            // statement about that one figure rather than about a constant of this class's own.
            CoreMap map = NewMap();
            KnowCooking();
            SpawnStack(map, new IntVec3(12, 0, 12), "RawBerries", 60);
            CoreRecipeDef recipe = CookingInitiative.CookableRecipesOn(map).First();

            Bill_Production few = CookingInitiative.StandingBillFor(recipe, SettlementWith(map, 3), map);
            Bill_Production many = CookingInitiative.StandingBillFor(recipe, SettlementWith(map, 30), map);

            Assert.True(many.targetCount > few.targetCount);
        }
    }
}
