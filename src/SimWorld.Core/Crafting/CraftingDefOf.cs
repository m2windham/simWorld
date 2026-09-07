using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Crafting
{
    [DefOf]
    public static class ThingCategoryDefOf
    {
        public static ThingCategoryDef Root = null!;
        public static ThingCategoryDef Foods = null!;
        public static ThingCategoryDef Meat = null!;
        public static ThingCategoryDef PlantFoodRaw = null!;
        public static ThingCategoryDef FoodMeals = null!;
    }

    [DefOf]
    public static class RecipeDefOf
    {
        public static RecipeDef CookMealSimple = null!;
        public static RecipeDef Make_Blocks_Sandstone = null!;
    }

    /// <summary>Hediffs GenRecipe/FoodUtility add directly, kept out of Health's own HediffDefOf since Health/ is read-only for this module.</summary>
    [DefOf]
    public static class CraftingHediffDefOf
    {
        public static HediffDef FoodPoisoning = null!;
    }
}
