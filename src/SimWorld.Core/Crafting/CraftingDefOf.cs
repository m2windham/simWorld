using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Crafting
{
    /// <summary>The JobDef <see cref="WorkGiver_DoBill"/> issues, bound by defName the same way
    /// <c>Building.BuildingJobDefOf</c> keeps its own module's JobDefs out of the shared <c>AI.JobDefOf</c> —
    /// a file the hauling/research/doctoring lanes are concurrently adding their own entries to in this same
    /// pass (<c>docs/WORK-REGISTER.md</c>).</summary>
    [DefOf]
    public static class CraftingJobDefOf
    {
        public static JobDef DoBill = null!;
    }

    [DefOf]
    public static class ThingCategoryDefOf
    {
        public static ThingCategoryDef Root = null!;
        public static ThingCategoryDef Foods = null!;
        public static ThingCategoryDef Meat = null!;
        public static ThingCategoryDef PlantFoodRaw = null!;
        public static ThingCategoryDef FoodMeals = null!;
        public static ThingCategoryDef AnimalProductRaw = null!;
        public static ThingCategoryDef Leathers = null!;
        public static ThingCategoryDef Wool = null!;
    }

    /// <summary>Item ThingDefs read by name rather than through a RecipeDef/ThingCategoryDef reference
    /// (system: crafting.animals) — <c>Recipe_ButcherAnimal</c>'s fallback when a race sets no
    /// <see cref="Pawns.RaceProperties.meatDef"/> of its own (RimWorld: same default).</summary>
    [DefOf]
    public static class ThingDefOf
    {
        public static ThingDef Meat_Generic = null!;
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
