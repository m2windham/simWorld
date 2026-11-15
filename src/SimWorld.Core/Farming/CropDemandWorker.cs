using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Farming
{
    /// <summary>Sizes one <see cref="CropDemandDef"/>'s field. The base wants nothing, so a def with no worker
    /// of its own grows no field.</summary>
    public class CropDemandWorker
    {
        public CropDemandDef def = null!;

        /// <summary>How many cells of <see cref="CropDemandDef.crop"/> the settlement on
        /// <paramref name="map"/> wants sown right now. Zero or less: no more sowing in this field.</summary>
        public virtual int CellsWanted(Map.Map map, Settlement? settlement) => 0;

        /// <summary>Cells needed to harvest <paramref name="amount"/> of the crop's product in one growth:
        /// one harvest yield per cell, before difficulty and growth losses.</summary>
        protected int CellsFor(int amount)
        {
            int yield = def.crop.plant?.harvestYield ?? 0;
            if (amount <= 0 || yield <= 0) return 0;
            return (amount + yield - 1) / yield;
        }
    }

    /// <summary>
    /// Grows enough of a fibre crop to cover what the outfitting benches standing on the map will need for the
    /// citizens who lack what they make (<see cref="OutfittingInitiative"/>): for every researched apparel or
    /// weapon recipe one of whose ingredient slots accepts the crop's product and whose bench stands on the
    /// map, the citizens lacking it times that slot's count, less what of the product already lies on the map.
    /// No bench, no field: the cloth would have nowhere to go.
    /// </summary>
    public class CropDemandWorker_Outfitting : CropDemandWorker
    {
        public override int CellsWanted(Map.Map map, Settlement? settlement)
        {
            ThingDef? product = def.crop.plant?.harvestedThingDef;
            if (product == null) return 0;

            List<Pawn> citizens = OutfittingInitiative.CitizensOn(map);
            int needed = 0;
            IReadOnlyList<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int r = 0; r < recipes.Count; r++)
            {
                RecipeDef recipe = recipes[r];
                if (!OutfittingInitiative.IsOutfitting(recipe) || !recipe.AvailableNow) continue;
                if (recipe.ingredients == null || !BenchStands(map, recipe)) continue;
                int perUnit = 0;
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    if (recipe.ingredients[i].filter.Allows(product)) perUnit += (int)Math.Ceiling(recipe.ingredients[i].count);
                }
                if (perUnit == 0) continue;
                needed += perUnit * OutfittingInitiative.Lacking(recipe, citizens);
            }

            return CellsFor(needed - OnMap(map, product));
        }

        private static bool BenchStands(Map.Map map, RecipeDef recipe)
        {
            if (recipe.recipeUsers == null) return false;
            for (int i = 0; i < recipe.recipeUsers.Count; i++)
            {
                if (map.listerThings.ThingsOfDef(recipe.recipeUsers[i]).Count > 0) return true;
            }
            return false;
        }

        private static int OnMap(Map.Map map, ThingDef product)
        {
            int total = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(product);
            for (int i = 0; i < stacks.Count; i++) total += stacks[i].stackCount;
            return total;
        }
    }
}
