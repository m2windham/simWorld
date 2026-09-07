using System;
using SimWorld.Pawns;

namespace SimWorld.Crafting
{
    /// <summary>Eating and food-poisoning helpers (RimWorld: <c>RimWorld.FoodUtility</c>, trimmed to what this port needs).</summary>
    public static class FoodUtility
    {
        /// <summary>
        /// Chance a cooked meal comes out poisoned, by the cook's skill (RimWorld's FoodPoisonChance stat).
        /// <b>Deviation:</b> RimWorld also factors in the kitchen room's cleanliness (dirt/filth raise this
        /// further); that half lands once Building/Room exist. This curve is the skill-only baseline —
        /// points taken from RimWorld's own tuning: 5% at skill 0, 2% at 5, 0.5% at 10, 0.1% at 20.
        /// </summary>
        public static readonly SimpleCurve FoodPoisonChanceFromCookCurve = new SimpleCurve(new[]
        {
            new CurvePoint(0, 0.05f),
            new CurvePoint(5, 0.02f),
            new CurvePoint(10, 0.005f),
            new CurvePoint(20, 0.001f),
        });

        public static float FoodPoisonChanceFromCook(int cookSkillLevel) => FoodPoisonChanceFromCookCurve.Evaluate(cookSkillLevel);

        public static float NutritionForItem(ItemStack food)
        {
            if (food == null) throw new ArgumentNullException(nameof(food));
            return (food.Def.ingestible?.nutrition ?? 0f) * food.Count;
        }

        /// <summary>
        /// Feeds <paramref name="food"/> to <paramref name="pawn"/>: raises the food need by its nutrition
        /// (clamped to the need's max) and, if the stack is <see cref="ItemStack.Poisoned"/>, adds
        /// <see cref="CraftingHediffDefOf.FoodPoisoning"/>. Returns the count eaten.
        /// </summary>
        public static int Eat(Pawn pawn, ItemStack food)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (food == null) throw new ArgumentNullException(nameof(food));

            pawn.needs.food?.Eat(NutritionForItem(food));
            if (food.Poisoned)
            {
                pawn.health.AddHediff(CraftingHediffDefOf.FoodPoisoning);
            }
            return food.Count;
        }
    }
}
