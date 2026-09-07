using System.Collections.Generic;
using System.Linq;
using SimWorld.Crafting;

namespace SimWorld.Defs
{
    /// <summary>
    /// Crafting & Production's additions to ThingDef: item categorisation, stuff/material properties,
    /// crafting cost, stacking, and the ingestible (food) block. Kept in its own partial file so parallel
    /// modules can each add their slice of ThingDef without touching one another's files — see
    /// <c>Defs/ThingDef.cs</c> for the single shared edit (<c>class</c> -&gt; <c>partial class</c>) every
    /// such file depends on.
    /// </summary>
    public partial class ThingDef
    {
        public List<ThingCategoryDef>? thingCategories;

        /// <summary>Present when this def can itself be used as a building/crafting material for a "made from stuff" ThingDef.</summary>
        public StuffProperties? stuffProps;

        /// <summary>Stuff categories a "made from stuff" recipe may pick this def's material from.</summary>
        public List<StuffCategoryDef>? stuffCategories;

        public List<ThingDefCountClass>? costList;
        public int costStuffCount;

        /// <summary>Most units one ItemStack of this def holds (RimWorld: <c>ThingDef.stackLimit</c>).</summary>
        public int stackLimit = 1;

        /// <summary>Present when pawns can eat this (RimWorld: <c>ThingDef.ingestible</c>).</summary>
        public IngestibleProperties? ingestible;

        /// <summary>
        /// Whether Things of this def carry a QualityCategory (RimWorld: a CompQuality on the def's comps;
        /// simplified here to a flag since Comps do not model quality yet).
        /// </summary>
        public bool hasQuality;

        private List<RecipeDef>? allRecipesCache;

        public bool IsStuff => stuffProps != null;

        public bool MadeFromStuff => stuffCategories != null && stuffCategories.Count > 0;

        public float BaseMarketValue => GetStatValueAbstract(StatDefOf.MarketValue);

        public bool IsNutritionGivingIngestible =>
            ingestible != null && ingestible.nutrition > 0f && ingestible.preferability > FoodPreferability.NeverForNutrition;

        public bool IsMeat => ingestible != null && (ingestible.foodType & FoodTypeFlags.Meat) != FoodTypeFlags.None;

        public bool IsMeal => ingestible != null && (ingestible.foodType & FoodTypeFlags.Meal) != FoodTypeFlags.None;

        /// <summary>Whether a human pawn can eat this for nutrition at all.</summary>
        public bool HumanEdible => IsNutritionGivingIngestible;

        /// <summary>
        /// Every RecipeDef whose recipeUsers lists this def, lazily cached (RimWorld: <c>ThingDef.AllRecipes</c>).
        /// <b>Deviation:</b> not invalidated — ThingDef is now assembled from partial files contributed by
        /// several modules, so this file deliberately avoids overriding the one shared
        /// <see cref="Def.ClearCachedData"/> hook, which only one contributor could safely do.
        /// </summary>
        public IReadOnlyList<RecipeDef> AllRecipes
        {
            get
            {
                if (allRecipesCache == null)
                {
                    allRecipesCache = DefDatabase<RecipeDef>.AllDefsListForReading
                        .Where(r => r.recipeUsers != null && r.recipeUsers.Contains(this))
                        .ToList();
                }
                return allRecipesCache;
            }
        }
    }
}
