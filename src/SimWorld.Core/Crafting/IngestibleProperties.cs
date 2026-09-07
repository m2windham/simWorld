using System;

namespace SimWorld.Crafting
{
    /// <summary>How appetizing a food item is, coarsely (RimWorld: <c>RimWorld.FoodPreferability</c>); ordered so a higher value always beats a lower one.</summary>
    public enum FoodPreferability
    {
        Undefined,
        NeverForNutrition,
        DesperateOnly,
        RawBad,
        RawTasty,
        MealAwful,
        MealSimple,
        MealFine,
        MealLavish,
    }

    /// <summary>What kind of food an ingestible is (RimWorld: <c>RimWorld.FoodTypeFlags</c>); combinable.</summary>
    [Flags]
    public enum FoodTypeFlags
    {
        None = 0,
        VegetableOrFruit = 1 << 0,
        Meat = 1 << 1,
        Fluid = 1 << 2,
        Corpse = 1 << 3,
        Seed = 1 << 4,
        AnimalProduct = 1 << 5,
        Plant = 1 << 6,
        Tree = 1 << 7,
        Meal = 1 << 8,
        Processed = 1 << 9,
    }

    /// <summary>What makes a ThingDef edible (RimWorld: <c>RimWorld.IngestibleProperties</c>).</summary>
    public class IngestibleProperties
    {
        public float nutrition;
        public FoodPreferability preferability = FoodPreferability.Undefined;
        public FoodTypeFlags foodType = FoodTypeFlags.None;
        public float joy;

        // joyKind and tasteThought intentionally omitted: no Joy-kind or Thought content for ingestibles yet.
    }
}
