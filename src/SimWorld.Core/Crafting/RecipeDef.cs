using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>A minimum skill level a pawn must have to start a bill (RimWorld: <c>RimWorld.SkillRequirement</c>).</summary>
    public class SkillRequirement
    {
        public SkillDef skill = null!;
        public int minLevel;

        public override string ToString() => (skill?.defName ?? "?") + " " + minLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One ingredient slot on a RecipeDef (RimWorld: <c>RimWorld.IngredientCount</c>): a filter naming which
    /// ThingDefs qualify, and how much of them — in the recipe's <see cref="IngredientValueGetter"/> units —
    /// is required.
    /// </summary>
    public class IngredientCount
    {
        public ThingFilter filter = new ThingFilter();
        public float count = 1f;

        /// <summary>True when the filter allows exactly one ThingDef — a specific, non-substitutable ingredient.</summary>
        public bool IsFixedIngredient => filter.AllowedDefCount == 1;

        public float GetBaseCount() => count;
    }

    /// <summary>
    /// Converts a candidate ThingDef into the units a recipe's <see cref="IngredientCount"/>s are expressed
    /// in (RimWorld: <c>RimWorld.IngredientValueGetter</c>). The default (<see cref="IngredientValueGetter_Volume"/>)
    /// counts raw pieces; <see cref="IngredientValueGetter_Nutrition"/> counts nutrition instead, so "0.5
    /// nutrition of meat or veg" needs fewer units of a richer ingredient.
    /// </summary>
    public abstract class IngredientValueGetter
    {
        public abstract float ValuePerUnitOf(ThingDef t);

        public virtual string BillRequirementsDescription(RecipeDef recipe, IngredientCount ingredient) =>
            ingredient.GetBaseCount().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x " + ingredient.filter.Summary;
    }

    public class IngredientValueGetter_Volume : IngredientValueGetter
    {
        public override float ValuePerUnitOf(ThingDef t) => 1f;
    }

    public class IngredientValueGetter_Nutrition : IngredientValueGetter
    {
        public override float ValuePerUnitOf(ThingDef t) => t?.ingestible?.nutrition ?? 0f;

        public override string BillRequirementsDescription(RecipeDef recipe, IngredientCount ingredient) =>
            ingredient.GetBaseCount().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " nutrition from " + ingredient.filter.Summary;
    }

    /// <summary>
    /// A craftable recipe (RimWorld: <c>RimWorld.RecipeDef</c>): what it consumes, what it produces, and who
    /// can work it. <see cref="Bill"/>s queue instances of a recipe on a workbench; ingredient selection and
    /// product creation are <see cref="BillIngredientsFinder"/> and <see cref="GenRecipe"/>.
    /// </summary>
    public class RecipeDef : Def, Research.IResearchUnlockable
    {
        public string? jobString;
        public float workAmount;
        public StatDef? workSpeedStat;
        public SkillDef? workSkill;
        public float workSkillLearnFactor = 1f;
        public List<IngredientCount>? ingredients;
        public ThingFilter? fixedIngredientFilter;
        public ThingFilter? defaultIngredientFilter;
        public bool allowMixingIngredients;
        public Type ingredientValueGetterClass = typeof(IngredientValueGetter_Volume);
        public List<ThingDefCountClass>? products;
        public List<SkillRequirement>? skillRequirements;
        public List<ThingDef>? recipeUsers;

        // ---- surgery (RimWorld: the same fields on its own RecipeDef) ----

        /// <summary>Behaviour half of the recipe, selected by <c>Class=</c>-style full type name in content.
        /// The default does nothing, which is right for every ordinary crafting recipe.</summary>
        public Type workerClass = typeof(RecipeWorker);

        /// <summary>True for a recipe performed on a pawn rather than at a workbench.</summary>
        public bool isSurgery;

        /// <summary>The recipe names a body part to operate on, and a <c>Bill_Medical</c> for it carries one.</summary>
        public bool targetsBodyPart = true;

        /// <summary>When set, the only parts this recipe may be applied to; otherwise any part of the body.</summary>
        public List<BodyPartDef>? appliedOnFixedBodyParts;

        /// <summary>Hediff the recipe adds to the operated part on success (a prosthetic, an implant).</summary>
        public HediffDef? addsHediff;

        /// <summary>Hediff the recipe removes from the operated part on success.</summary>
        public HediffDef? removesHediff;

        /// <summary>
        /// Multiplies the surgeon's own chance of getting this operation right — a simple amputation is more
        /// forgiving than delicate work. RimWorld carries this exact field; the numbers in this port's content
        /// are its own.
        /// </summary>
        public float surgerySuccessChanceFactor = 1f;

        /// <summary>Chance a failed operation kills the patient outright, rather than merely injuring them.</summary>
        public float deathOnFailedSurgeryChance;

        private RecipeWorker? workerCache;

        /// <summary>The lazily-created worker (RimWorld: <c>RecipeDef.Worker</c>).</summary>
        public RecipeWorker Worker
        {
            get
            {
                if (workerCache == null)
                {
                    workerCache = (RecipeWorker)Activator.CreateInstance(workerClass)!;
                    workerCache.recipe = this;
                }
                return workerCache;
            }
        }

        /// <summary>When true, a product's stuff (see <see cref="ItemStack.Stuff"/>) is set to the dominant ingredient.</summary>
        public bool productHasIngredientStuff;

        private IngredientValueGetter? valueGetterCache;

        public IngredientValueGetter IngredientValueGetter =>
            valueGetterCache ??= (IngredientValueGetter)Activator.CreateInstance(ingredientValueGetterClass)!;

        /// <summary>
        /// Total work a job driver must apply to finish one iteration. <paramref name="stuff"/> is reserved
        /// for a future stuff-based work-speed stat factor and currently ignored (kept for the RimWorld-shaped
        /// call site: real recipes with stuff-scaled work — e.g. a steel wall costing more work than a wood
        /// one — land once a Stats module resolves StatDefs generically).
        /// </summary>
        public float WorkAmountTotal(ThingDef? stuff = null) => workAmount;

        public bool PawnSatisfiesSkillRequirements(Pawn pawn) => FirstSkillRequirementPawnDoesntSatisfy(pawn) == null;

        public SkillRequirement? FirstSkillRequirementPawnDoesntSatisfy(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (skillRequirements == null) return null;
            foreach (SkillRequirement requirement in skillRequirements)
            {
                int level = pawn.skills?.GetSkill(requirement.skill)?.Level ?? 0;
                if (level < requirement.minLevel) return requirement;
            }
            return null;
        }

        public bool IsIngredient(ThingDef def) => ingredients != null && ingredients.Any(i => i.filter.Allows(def));

        public IEnumerable<ThingDef> AllRecipeUsers => recipeUsers ?? Enumerable.Empty<ThingDef>();

        /// <summary>Projects that must be finished before this recipe can be run.</summary>
        public List<Research.ResearchProjectDef>? researchPrerequisites;

        IReadOnlyList<Research.ResearchProjectDef>? Research.IResearchUnlockable.ResearchPrerequisites => researchPrerequisites;

        /// <summary>
        /// True when the civilization knows every project this recipe requires. This is the gate the stub that
        /// stood here promised: a bill for a recipe whose research is unfinished must not be startable.
        /// </summary>
        public bool AvailableNow
        {
            get
            {
                if (researchPrerequisites == null) return true;
                for (int i = 0; i < researchPrerequisites.Count; i++)
                {
                    if (!Sim.Find.ResearchManager.IsFinished(researchPrerequisites[i])) return false;
                }
                return true;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerCache = null;
            valueGetterCache = null;
        }

        public ThingDef? ProducedThingDef => products != null && products.Count > 0 ? products[0].thingDef : null;

        private ThingFilter? autoFixedIngredientFilterCache;
        private ThingFilter? autoDefaultIngredientFilterCache;

        /// <summary>
        /// <see cref="fixedIngredientFilter"/> when the XML sets one, else the union of every ingredient
        /// slot's filter (RimWorld auto-generates fixedIngredientFilter the same way when content doesn't).
        /// <b>Deviation (deliberate):</b> computed lazily on first use rather than in
        /// <see cref="Def.ResolveReferences"/> — ThingCategoryDef's own category-tree lookups read
        /// <c>DefDatabase&lt;ThingDef&gt;</c> through the static <see cref="DefDatabase{T}"/> facade, which
        /// only tracks <see cref="DefDatabase.Global"/>; a content load into an explicit (non-Global)
        /// DefDatabase — such as this codebase's own test fixture — would see an empty or unrelated
        /// database if this ran during ResolveReferences instead of after load, when Global is current.
        /// </summary>
        public ThingFilter EffectiveFixedIngredientFilter
        {
            get
            {
                if (fixedIngredientFilter != null) return fixedIngredientFilter;
                if (autoFixedIngredientFilterCache == null)
                {
                    autoFixedIngredientFilterCache = new ThingFilter();
                    if (ingredients != null)
                    {
                        foreach (IngredientCount ingredient in ingredients)
                        {
                            foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                            {
                                autoFixedIngredientFilterCache.SetAllow(def, true);
                            }
                        }
                    }
                }
                return autoFixedIngredientFilterCache;
            }
        }

        /// <summary>
        /// <see cref="defaultIngredientFilter"/> when the XML sets one, else a copy of
        /// <see cref="EffectiveFixedIngredientFilter"/> — what a freshly created <see cref="Bill"/> starts
        /// its own <see cref="Bill.ingredientFilter"/> from. See <see cref="EffectiveFixedIngredientFilter"/>
        /// for why this is lazy rather than resolved at load time.
        /// </summary>
        public ThingFilter EffectiveDefaultIngredientFilter
        {
            get
            {
                if (defaultIngredientFilter != null) return defaultIngredientFilter;
                if (autoDefaultIngredientFilterCache == null)
                {
                    autoDefaultIngredientFilterCache = new ThingFilter();
                    autoDefaultIngredientFilterCache.CopyAllowancesFrom(EffectiveFixedIngredientFilter);
                }
                return autoDefaultIngredientFilterCache;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            // A surgery consumes nothing and produces nothing: its whole effect is what its worker does to the
            // patient. Requiring ingredients and products of one would be requiring it to be a crafting recipe.
            if (!isSurgery)
            {
                if (ingredients == null || ingredients.Count == 0)
                {
                    yield return "has no ingredients.";
                }
                if (products == null || products.Count == 0)
                {
                    yield return "has no products.";
                }
            }
            else
            {
                if (!typeof(Health.Recipe_Surgery).IsAssignableFrom(workerClass))
                {
                    yield return "isSurgery is set but workerClass is not a Recipe_Surgery.";
                }
            }
            if (workAmount < 0f)
            {
                yield return "workAmount is negative.";
            }
            if (!typeof(IngredientValueGetter).IsAssignableFrom(ingredientValueGetterClass))
            {
                yield return "ingredientValueGetterClass must derive from IngredientValueGetter.";
            }
        }
    }
}
