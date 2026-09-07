using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Chooses which candidate ItemStacks satisfy a bill's recipe (RimWorld:
    /// <c>WorkGiver_DoBill.TryFindBestBillIngredients</c>, ported over <see cref="ItemStack"/> candidates
    /// instead of map Things — stockpile scanning and distance ordering land with the Map/AI module).
    /// <paramref name="candidates"/> is assumed already ordered by whatever priority the caller wants
    /// (nearest first, typically); this never reorders it.
    /// </summary>
    public static class BillIngredientsFinder
    {
        private const float Epsilon = 0.0001f;

        /// <summary>
        /// Fills <paramref name="chosen"/> with one entry per stack (or partial stack) consumed and returns
        /// true, or clears it and returns false when the recipe's ingredient slots cannot all be filled from
        /// <paramref name="candidates"/>.
        /// </summary>
        public static bool TryFindBestIngredients(Bill bill, IReadOnlyList<ItemStack> candidates, List<ItemStack> chosen)
        {
            if (bill == null) throw new ArgumentNullException(nameof(bill));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (chosen == null) throw new ArgumentNullException(nameof(chosen));

            chosen.Clear();
            List<IngredientCount>? ingredients = bill.recipe.ingredients;
            if (ingredients == null || ingredients.Count == 0) return false;

            // How much of each candidate stack is still unclaimed; carried across ingredient slots so one
            // plentiful pile can satisfy more than one slot, and a slot can span several piles (splitting).
            var remaining = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) remaining[i] = candidates[i].Count;

            IngredientValueGetter valueGetter = bill.recipe.IngredientValueGetter;
            bool allowMixing = bill.recipe.allowMixingIngredients;

            foreach (IngredientCount ingredient in ingredients)
            {
                float need = ingredient.GetBaseCount();
                ThingDef? lockedDef = null;

                for (int i = 0; i < candidates.Count && need > Epsilon; i++)
                {
                    if (remaining[i] <= 0) continue;
                    ItemStack stack = candidates[i];

                    if (!allowMixing && lockedDef != null && stack.Def != lockedDef) continue;
                    if (!bill.ingredientFilter.Allows(stack)) continue;
                    if (!ingredient.filter.Allows(stack)) continue;

                    float valuePerUnit = valueGetter.ValuePerUnitOf(stack.Def);
                    if (valuePerUnit <= 0f) continue;

                    int neededUnits = (int)Math.Ceiling(need / valuePerUnit - Epsilon);
                    if (neededUnits < 1) neededUnits = 1;
                    int takeUnits = Math.Min(remaining[i], neededUnits);
                    if (takeUnits <= 0) continue;

                    chosen.Add(stack with { Count = takeUnits });
                    remaining[i] -= takeUnits;
                    need -= takeUnits * valuePerUnit;
                    lockedDef = stack.Def;
                }

                if (need > Epsilon)
                {
                    chosen.Clear();
                    return false;
                }
            }
            return true;
        }
    }
}
