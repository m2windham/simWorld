using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Crafting
{
    public class CraftingTests : ContentTestBase
    {
        public CraftingTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        private static ThingDef RawPotatoes => Def("RawPotatoes");
        private static ThingDef MeatGeneric => Def("Meat_Generic");
        private static ThingDef MealSimple => Def("MealSimple");
        private static ThingDef Steel => Def("Steel");
        private static ThingDef ChunkSandstone => Def("ChunkSandstone");
        private static ThingDef BlocksSandstone => Def("BlocksSandstone");
        private static ThingDef FueledStove => Def("FueledStove");
        private static ThingDef TableStonecutter => Def("TableStonecutter");

        // ---- content ----

        [Fact]
        public void Core_crafting_content_loads_with_expected_counts()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(12, DefDatabase<ThingCategoryDef>.DefCount);
            Assert.Equal(5, DefDatabase<StuffCategoryDef>.DefCount);
            Assert.True(DefDatabase<ThingDef>.AllDefsListForReading.Count(d => d.category == ThingCategory.Item) >= 10);
            Assert.True(DefDatabase<RecipeDef>.DefCount >= 5);
            // Assert the production benches this module ships, not a count of every building def:
            // other modules (natural rock from Map core, and later Building) add their own.
            Assert.All(
                new[] { "FueledStove", "TableButcher", "TableStonecutter" },
                name => Assert.Equal(ThingCategory.Building, Def(name).category));
        }

        [Fact]
        public void DefOfs_are_bound()
        {
            Assert.NotNull(StatDefOf.MarketValue);
            Assert.Equal("MarketValue", StatDefOf.MarketValue.defName);
            Assert.NotNull(ThingCategoryDefOf.Root);
            Assert.NotNull(ThingCategoryDefOf.Foods);
            Assert.NotNull(RecipeDefOf.CookMealSimple);
            Assert.NotNull(RecipeDefOf.Make_Blocks_Sandstone);
            Assert.NotNull(CraftingHediffDefOf.FoodPoisoning);
        }

        [Fact]
        public void RawPotatoes_carries_the_MarketValue_stat_base()
        {
            Assert.Equal(0.25f, RawPotatoes.BaseMarketValue);
        }

        // ---- category tree ----

        [Fact]
        public void ThingCategoryDef_descendants_include_leaves_and_direct_members()
        {
            List<ThingDef> descendants = ThingCategoryDefOf.Foods.DescendantThingDefs.ToList();
            Assert.Contains(RawPotatoes, descendants);
            Assert.Contains(MealSimple, descendants);
            Assert.Contains(MeatGeneric, descendants);

            Assert.Contains(ThingCategoryDefOf.Meat, ThingCategoryDefOf.Foods.ChildCategories);
            Assert.Contains(ThingCategoryDefOf.Foods, ThingCategoryDefOf.Meat.Parents);
            Assert.Contains(ThingCategoryDefOf.Meat, ThingCategoryDefOf.Foods.ThisAndChildCategoryDefs);
        }

        [Fact]
        public void ThingDef_AllRecipes_lists_recipes_naming_it_as_a_recipeUser()
        {
            List<RecipeDef> recipes = FueledStove.AllRecipes.ToList();
            Assert.Contains(RecipeDefOf.CookMealSimple, recipes);
            Assert.DoesNotContain(RecipeDefOf.Make_Blocks_Sandstone, recipes);
            Assert.Contains(RecipeDefOf.Make_Blocks_Sandstone, TableStonecutter.AllRecipes);
            Assert.True(RecipeDefOf.CookMealSimple.IsIngredient(RawPotatoes));
            Assert.True(RecipeDefOf.CookMealSimple.IsIngredient(MeatGeneric));
            Assert.False(RecipeDefOf.CookMealSimple.IsIngredient(Steel));
            Assert.True(RecipeDefOf.Make_Blocks_Sandstone.IsIngredient(ChunkSandstone));
            Assert.Equal(BlocksSandstone, RecipeDefOf.Make_Blocks_Sandstone.ProducedThingDef);
        }

        // ---- ThingFilter ----

        [Fact]
        public void ThingFilter_allowing_a_category_allows_every_descendant()
        {
            var filter = new ThingFilter();
            filter.SetAllow(ThingCategoryDefOf.Foods, true);

            Assert.True(filter.Allows(RawPotatoes));
            Assert.True(filter.Allows(MealSimple));
            Assert.False(filter.Allows(Steel));
            Assert.Equal(ThingCategoryDefOf.Foods.DescendantThingDefs.Count(), filter.AllowedDefCount);
        }

        [Fact]
        public void ThingFilter_can_disallow_one_def_without_touching_the_rest()
        {
            var filter = new ThingFilter();
            filter.SetAllow(ThingCategoryDefOf.Foods, true);
            filter.SetAllow(RawPotatoes, false);

            Assert.False(filter.Allows(RawPotatoes));
            Assert.True(filter.Allows(MealSimple));
        }

        [Fact]
        public void ThingFilter_quality_and_hitpoints_ranges_gate_ItemStacks()
        {
            var filter = new ThingFilter();
            filter.SetAllow(Steel, true);
            filter.allowedQualities = new QualityRange(QualityCategory.Good, QualityCategory.Legendary);
            filter.allowedHitPointsPercents = new FloatRange(0.5f, 1f);

            Assert.False(filter.Allows(new ItemStack(Steel, 1, Quality: QualityCategory.Normal)));
            Assert.True(filter.Allows(new ItemStack(Steel, 1, Quality: QualityCategory.Good)));
            Assert.False(filter.Allows(new ItemStack(Steel, 1, Quality: QualityCategory.Good, HitPointsPercent: 0.2f)));
            Assert.True(filter.Allows(new ItemStack(Steel, 1, Quality: QualityCategory.Good, HitPointsPercent: 0.8f)));
        }

        [Fact]
        public void ThingFilter_CopyAllowancesFrom_copies_defs_and_ranges()
        {
            var source = new ThingFilter();
            source.SetAllow(RawPotatoes, true);
            source.SetAllow(Steel, true);
            source.allowedQualities = new QualityRange(QualityCategory.Poor, QualityCategory.Good);

            var copy = new ThingFilter();
            copy.CopyAllowancesFrom(source);

            Assert.Equal(2, copy.AllowedDefCount);
            Assert.True(copy.Allows(RawPotatoes));
            Assert.True(copy.Allows(Steel));
            Assert.Equal(new QualityRange(QualityCategory.Poor, QualityCategory.Good), copy.allowedQualities);
        }

        private class FilterHolder : IExposable
        {
            public ThingFilter? filter;
            public void ExposeData() => Scribe_Deep.Look(ref filter, "filter");
        }

        [Fact]
        public void ThingFilter_round_trips_through_Scribe()
        {
            var holder = new FilterHolder { filter = new ThingFilter() };
            holder.filter!.SetAllow(RawPotatoes, true);
            holder.filter!.SetAllow(Steel, true);
            holder.filter!.allowedHitPointsPercents = new FloatRange(0.25f, 0.9f);
            holder.filter!.allowedQualities = new QualityRange(QualityCategory.Poor, QualityCategory.Excellent);

            string xml = Scribe.SaveToString(holder, "game");
            FilterHolder loaded = Scribe.Load<FilterHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(2, loaded.filter!.AllowedDefCount);
            Assert.True(loaded.filter.Allows(RawPotatoes));
            Assert.True(loaded.filter.Allows(Steel));
            Assert.Equal(new FloatRange(0.25f, 0.9f), loaded.filter.allowedHitPointsPercents);
            Assert.Equal(new QualityRange(QualityCategory.Poor, QualityCategory.Excellent), loaded.filter.allowedQualities);
        }

        // ---- ingredient selection ----

        private static Bill_Production MakeBill(RecipeDef recipe) => new Bill_Production(recipe);

        /// <summary>One combined ingredient slot accepting any of <paramref name="anyOf"/> (an "or" slot,
        /// like CookMealSimple's meat-or-veg). ResolveReferences runs once, after everything is set, so the
        /// auto-generated fixedIngredientFilter/defaultIngredientFilter (which seed Bill.ingredientFilter)
        /// reflect the final ingredient list.</summary>
        private static RecipeDef MakeAdHocRecipe(bool allowMixing, float count, params ThingDef[] anyOf)
        {
            var filter = new ThingFilter();
            foreach (ThingDef def in anyOf) filter.SetAllow(def, true);
            var recipe = new RecipeDef
            {
                defName = "TestRecipe_" + Guid.NewGuid().ToString("N"),
                workAmount = 100,
                allowMixingIngredients = allowMixing,
                ingredientValueGetterClass = typeof(IngredientValueGetter_Nutrition),
                ingredients = new List<IngredientCount> { new IngredientCount { filter = filter, count = count } },
                products = new List<ThingDefCountClass> { new ThingDefCountClass(MealSimple, 1) },
            };
            recipe.ResolveReferences();
            return recipe;
        }

        [Fact]
        public void TryFindBestIngredients_picks_from_ordered_candidates_using_nutrition_units()
        {
            RecipeDef recipe = MakeAdHocRecipe(allowMixing: true, count: 0.5f, RawPotatoes);
            Bill_Production bill = MakeBill(recipe);

            var candidates = new List<ItemStack> { new ItemStack(RawPotatoes, 20) };
            var chosen = new List<ItemStack>();

            Assert.True(BillIngredientsFinder.TryFindBestIngredients(bill, candidates, chosen));
            Assert.Single(chosen);
            Assert.Equal(10, chosen[0].Count);
        }

        [Fact]
        public void TryFindBestIngredients_splits_the_need_across_multiple_stacks()
        {
            RecipeDef recipe = MakeAdHocRecipe(allowMixing: true, count: 0.5f, RawPotatoes);
            Bill_Production bill = MakeBill(recipe);

            var candidates = new List<ItemStack> { new ItemStack(RawPotatoes, 5), new ItemStack(RawPotatoes, 20) };
            var chosen = new List<ItemStack>();

            Assert.True(BillIngredientsFinder.TryFindBestIngredients(bill, candidates, chosen));
            Assert.Equal(2, chosen.Count);
            Assert.Equal(5, chosen[0].Count);
            Assert.Equal(5, chosen[1].Count);
        }

        [Fact]
        public void TryFindBestIngredients_disallows_mixing_defs_within_one_slot_when_not_allowed()
        {
            RecipeDef recipe = MakeAdHocRecipe(allowMixing: false, count: 0.5f, RawPotatoes, MeatGeneric);
            Bill_Production bill = MakeBill(recipe);
            var candidates = new List<ItemStack> { new ItemStack(RawPotatoes, 3), new ItemStack(MeatGeneric, 20) };
            var chosen = new List<ItemStack>();

            // Only 3 potato units (0.15 nutrition) are ever considered once potatoes lock the slot; meat is
            // skipped despite being plentiful, so 0.5 nutrition is never reached.
            Assert.False(BillIngredientsFinder.TryFindBestIngredients(bill, candidates, chosen));
            Assert.Empty(chosen);
        }

        [Fact]
        public void TryFindBestIngredients_allows_mixing_defs_within_one_slot_when_allowed()
        {
            RecipeDef recipe = MakeAdHocRecipe(allowMixing: true, count: 0.5f, RawPotatoes, MeatGeneric);
            Bill_Production bill = MakeBill(recipe);
            var candidates = new List<ItemStack> { new ItemStack(RawPotatoes, 3), new ItemStack(MeatGeneric, 20) };
            var chosen = new List<ItemStack>();

            Assert.True(BillIngredientsFinder.TryFindBestIngredients(bill, candidates, chosen));
            Assert.Equal(2, chosen.Count);
            Assert.Equal(RawPotatoes, chosen[0].Def);
            Assert.Equal(3, chosen[0].Count);
            Assert.Equal(MeatGeneric, chosen[1].Def);
            Assert.Equal(7, chosen[1].Count);
        }

        [Fact]
        public void TryFindBestIngredients_fails_and_clears_chosen_when_candidates_are_short()
        {
            RecipeDef recipe = MakeAdHocRecipe(allowMixing: true, count: 0.5f, RawPotatoes);
            Bill_Production bill = MakeBill(recipe);

            var candidates = new List<ItemStack> { new ItemStack(RawPotatoes, 3) };
            var chosen = new List<ItemStack> { new ItemStack(Steel, 99) };

            Assert.False(BillIngredientsFinder.TryFindBestIngredients(bill, candidates, chosen));
            Assert.Empty(chosen);
        }

        // ---- GenRecipe ----

        [Fact]
        public void MakeRecipeProducts_cooks_one_simple_meal_and_awards_cooking_xp()
        {
            Pawn worker = NewHuman();
            SkillRecord cooking = worker.skills.GetSkill(SkillDefOf.Cooking)!;
            float xpBefore = cooking.xpSinceLastLevel;

            var ingredients = new List<ItemStack> { new ItemStack(MeatGeneric, 3), new ItemStack(RawPotatoes, 10) };
            List<ItemStack> products = GenRecipe.MakeRecipeProducts(RecipeDefOf.CookMealSimple, worker, ingredients);

            Assert.Single(products);
            Assert.Equal(MealSimple, products[0].Def);
            Assert.Equal(1, products[0].Count);
            Assert.True(cooking.xpSinceLastLevel > xpBefore);
        }

        [Fact]
        public void DominantIngredient_picks_the_largest_stack()
        {
            var ingredients = new List<ItemStack> { new ItemStack(MeatGeneric, 3), new ItemStack(RawPotatoes, 10) };
            Assert.Equal(RawPotatoes, GenRecipe.DominantIngredient(ingredients));
        }

        // ---- quality ----

        [Fact]
        public void Quality_mean_increases_with_skill()
        {
            Rand.Current = new RandomStream(42);
            const int n = 1000;
            double sumLow = 0, sumHigh = 0;
            for (int i = 0; i < n; i++) sumLow += (int)QualityUtility.GenerateQualityCreatedByPawn(0, inspired: false);
            for (int i = 0; i < n; i++) sumHigh += (int)QualityUtility.GenerateQualityCreatedByPawn(20, inspired: false);

            Assert.True(sumHigh / n > sumLow / n);
        }

        [Fact]
        public void Quality_inspired_shifts_up_two_tiers_and_Legendary_needs_inspiration()
        {
            Rand.Current = new RandomStream(7);
            bool sawLegendaryUninspired = false;
            bool sawLegendaryInspired = false;
            for (int i = 0; i < 2000; i++)
            {
                if (QualityUtility.GenerateQualityCreatedByPawn(20, inspired: false) == QualityCategory.Legendary) sawLegendaryUninspired = true;
                if (QualityUtility.GenerateQualityCreatedByPawn(20, inspired: true) == QualityCategory.Legendary) sawLegendaryInspired = true;
            }

            Assert.False(sawLegendaryUninspired);
            Assert.True(sawLegendaryInspired);
        }

        // ---- bills ----

        [Fact]
        public void Bill_Production_RepeatCount_counts_down_to_zero()
        {
            Pawn worker = NewHuman();
            var bill = new Bill_Production(RecipeDefOf.CookMealSimple) { repeatMode = BillRepeatMode.RepeatCount, repeatCount = 2 };

            Assert.True(bill.ShouldDoNow());
            bill.Notify_IterationCompleted(worker, new List<ItemStack>());
            Assert.Equal(1, bill.repeatCount);
            Assert.True(bill.ShouldDoNow());

            bill.Notify_IterationCompleted(worker, new List<ItemStack>());
            Assert.Equal(0, bill.repeatCount);
            Assert.False(bill.ShouldDoNow());
        }

        private class FakeProductCounter : IProductCounter
        {
            public int Count;
            public int CountProducts(Bill_Production bill) => Count;
        }

        private class FakeBillGiver : IBillGiver
        {
            public IProductCounter? ProductCounter { get; set; }
            public string LabelCap => "Fake";
        }

        [Fact]
        public void Bill_Production_TargetCount_pauses_and_unpauses_with_hysteresis()
        {
            var counter = new FakeProductCounter { Count = 0 };
            var giver = new FakeBillGiver { ProductCounter = counter };
            var stack = new BillStack(giver);
            var bill = new Bill_Production(RecipeDefOf.CookMealSimple)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = 5,
                unpauseWhenYouHave = 0,
            };
            stack.AddBill(bill);

            counter.Count = 0;
            Assert.True(bill.ShouldDoNow());

            counter.Count = 5;
            Assert.False(bill.ShouldDoNow());
            Assert.True(bill.paused);

            counter.Count = 3;
            Assert.False(bill.ShouldDoNow());

            counter.Count = 0;
            Assert.True(bill.ShouldDoNow());
            Assert.False(bill.paused);
        }

        [Fact]
        public void Bill_Production_Forever_always_wants_doing()
        {
            var bill = new Bill_Production(RecipeDefOf.CookMealSimple) { repeatMode = BillRepeatMode.Forever };
            Assert.True(bill.ShouldDoNow());
            bill.Notify_IterationCompleted(NewHuman(), new List<ItemStack>());
            Assert.True(bill.ShouldDoNow());
        }

        [Fact]
        public void Bill_PawnAllowedToStartAnew_is_gated_by_suspended_and_skill_range()
        {
            Pawn pawn = NewHuman();
            var bill = new Bill_Production(RecipeDefOf.CookMealSimple) { allowedSkillRange = new IntRange(5, 20) };

            Assert.False(bill.PawnAllowedToStartAnew(pawn));

            pawn.skills.GetSkill(SkillDefOf.Cooking)!.Level = 10;
            Assert.True(bill.PawnAllowedToStartAnew(pawn));

            bill.suspended = true;
            Assert.False(bill.PawnAllowedToStartAnew(pawn));
        }

        [Fact]
        public void BillStack_reorders_deletes_and_finds_the_first_that_should_be_done()
        {
            var giver = new FakeBillGiver();
            var stack = new BillStack(giver);
            var a = new Bill_Production(RecipeDefOf.CookMealSimple) { suspended = true };
            var b = new Bill_Production(RecipeDefOf.CookMealSimple) { repeatMode = BillRepeatMode.Forever };
            var c = new Bill_Production(RecipeDefOf.CookMealSimple) { repeatMode = BillRepeatMode.Forever };
            stack.AddBill(a);
            stack.AddBill(b);
            stack.AddBill(c);

            Assert.Equal(3, stack.Count);
            Assert.Same(b, stack.FirstShouldDoNow);
            Assert.True(stack.AnyShouldDoNow);

            stack.Reorder(c, -2);
            Assert.Same(c, stack[0]);

            stack.Delete(a);
            Assert.Equal(2, stack.Count);
            Assert.DoesNotContain(a, stack.Bills);
        }

        private class BillStackHolder : IExposable, IBillGiver
        {
            public BillStack? stack;
            public IProductCounter? ProductCounter => null;
            public string LabelCap => "Holder";

            public void ExposeData() => Scribe_Deep.Look(ref stack, "stack", this);
        }

        [Fact]
        public void BillStack_round_trips_through_Scribe_with_bills_and_filters()
        {
            var holder = new BillStackHolder();
            holder.stack = new BillStack(holder);
            var bill = new Bill_Production(RecipeDefOf.CookMealSimple)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = 7,
                suspended = true,
            };
            bill.ingredientFilter.SetAllow(MeatGeneric, false);
            holder.stack.AddBill(bill);

            string xml = Scribe.SaveToString(holder, "game");
            BillStackHolder loaded = Scribe.Load<BillStackHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(1, loaded.stack!.Count);
            var loadedBill = (Bill_Production)loaded.stack[0];
            Assert.Same(RecipeDefOf.CookMealSimple, loadedBill.recipe);
            Assert.Equal(BillRepeatMode.TargetCount, loadedBill.repeatMode);
            Assert.Equal(7, loadedBill.targetCount);
            Assert.True(loadedBill.suspended);
            Assert.False(loadedBill.ingredientFilter.Allows(MeatGeneric));
            Assert.Same(loaded.stack, loadedBill.billStack);
        }

        // ---- food ----

        [Fact]
        public void Food_Eat_raises_the_need_and_clamps_to_max()
        {
            Pawn pawn = NewHuman();
            pawn.needs.food!.CurLevel = pawn.needs.food.MaxLevel - 0.05f;

            int eaten = FoodUtility.Eat(pawn, new ItemStack(MealSimple, 1));

            Assert.Equal(1, eaten);
            Assert.Equal(pawn.needs.food.MaxLevel, pawn.needs.food.CurLevel);
        }

        [Fact]
        public void Food_a_poisoned_meal_gives_the_pawn_FoodPoisoning()
        {
            Pawn pawn = NewHuman();
            ItemStack poisoned = new ItemStack(MealSimple, 1) { Poisoned = true };

            FoodUtility.Eat(pawn, poisoned);

            Assert.True(pawn.HasHediff(CraftingHediffDefOf.FoodPoisoning));
        }

        [Fact]
        public void FoodPoisonChanceFromCook_matches_the_tuned_curve_points()
        {
            Assert.Equal(0.05f, FoodUtility.FoodPoisonChanceFromCook(0));
            Assert.Equal(0.02f, FoodUtility.FoodPoisonChanceFromCook(5));
            Assert.Equal(0.005f, FoodUtility.FoodPoisonChanceFromCook(10));
            Assert.Equal(0.001f, FoodUtility.FoodPoisonChanceFromCook(20));
        }
    }
}
