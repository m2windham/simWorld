using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Turns consumed ingredients into a recipe's products (RimWorld: <c>RimWorld.GenRecipe</c>). This port
    /// has no job driver yet to tick a bill's work over time, so callers invoke this directly once a bill's
    /// ingredients (see <see cref="BillIngredientsFinder"/>) are in hand.
    /// </summary>
    public static class GenRecipe
    {
        public static List<ItemStack> MakeRecipeProducts(RecipeDef recipe, Pawn worker, List<ItemStack> ingredients, ThingDef? dominantIngredient = null)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (worker == null) throw new ArgumentNullException(nameof(worker));
            if (ingredients == null) throw new ArgumentNullException(nameof(ingredients));

            dominantIngredient ??= DominantIngredient(ingredients);

            var products = new List<ItemStack>();
            if (recipe.products != null)
            {
                foreach (ThingDefCountClass entry in recipe.products)
                {
                    ThingDef? stuff = recipe.productHasIngredientStuff ? dominantIngredient : null;
                    QualityCategory? quality = null;
                    if (entry.thingDef.hasQuality)
                    {
                        int skillLevel = recipe.workSkill != null ? worker.skills.GetSkill(recipe.workSkill)?.Level ?? 0 : 0;
                        quality = QualityUtility.GenerateQualityCreatedByPawn(skillLevel, inspired: false);
                    }
                    ItemStack product = new ItemStack(entry.thingDef, entry.count, stuff, quality);
                    products.Add(PostProcessProduct(product, recipe, worker));
                }
            }

            // Deviation: RimWorld awards skill XP per work tick as a bill is worked (roughly workAmount ticks
            // at a small XP rate each, scaled by workSkillLearnFactor). Without a job driver to tick, this
            // port awards the equivalent lump sum once per finished recipe instead: workAmount (RimWorld's
            // "ticks of work") x workSkillLearnFactor x 0.1 (RimWorld's per-tick base learn rate).
            if (recipe.workSkill != null)
            {
                worker.skills.Learn(recipe.workSkill, recipe.WorkAmountTotal() * recipe.workSkillLearnFactor * 0.1f);
            }

            return products;
        }

        /// <summary>Post-creation effects — currently just a cooked meal's food-poisoning roll.</summary>
        public static ItemStack PostProcessProduct(ItemStack product, RecipeDef recipe, Pawn worker)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            bool isCookedMeal = recipe.workSkill == SkillDefOf.Cooking && product.Def.IsMeal;
            if (isCookedMeal)
            {
                int cookLevel = worker.skills.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
                if (Rand.Chance(FoodUtility.FoodPoisonChanceFromCook(cookLevel)))
                {
                    product = product with { Poisoned = true };
                }
            }
            return product;
        }

        /// <summary>The ingredient present in the largest quantity; ties keep the first one seen.</summary>
        public static ThingDef? DominantIngredient(IReadOnlyList<ItemStack> ingredients)
        {
            if (ingredients == null) throw new ArgumentNullException(nameof(ingredients));
            ThingDef? best = null;
            int bestCount = -1;
            foreach (ItemStack stack in ingredients)
            {
                if (stack.Count > bestCount)
                {
                    bestCount = stack.Count;
                    best = stack.Def;
                }
            }
            return best;
        }
    }
}
