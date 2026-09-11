using System;
using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Tuning for <see cref="CookingInitiative"/>. RimWorld has nothing to port: its player adds the bill and
    /// types the target count. Both figures below are this port's own, pinned by behaviour rather than by the
    /// literal, per CLAUDE.md.
    /// </summary>
    public static class CookingTuning
    {
        /// <summary>Self-gate cadence — the same rare bucket every other initiative here uses.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>How many stoves a settlement wants. One: a bill is worked by one cook at a time, and a
        /// second bench would only help if the first were the bottleneck, which it is not — raw food is. The
        /// day a settlement's kitchen is the constraint this becomes a real per-capita figure.</summary>
        public const int StovesWanted = 1;

        /// <summary>
        /// How far from the raw food a new stove may be placed, in cells. The binding constraint is
        /// <see cref="WorkGiver_DoBill.IngredientSearchRadius"/>: that giver only offers a bill whose
        /// ingredients already lie within its radius of the bench, so a stove placed anywhere else is a stove
        /// nobody can ever work. Kept at that radius rather than a looser one of its own — the same reason
        /// <c>StonecutterInitiative</c> plants its table among the chunks.
        /// </summary>
        public const int StovePlacementRadius = WorkGiver_DoBill.IngredientSearchRadius;
    }

    /// <summary>
    /// Translation: <b>a settlement cooks for itself</b>, because there is no player to add the bill.
    ///
    /// <para/><b>The gap, and why the register called cooking "the bench that is genuinely ready".</b>
    /// <c>Crafting.StonecutterInitiative</c> closed the "nothing in <c>src/</c> ever created a bill" hole for
    /// stone and left the same hole open for every other bench, with an explicit rule for when to fill it: a
    /// bill whose target is zero must not be queued, because nothing consumes the product. Smithing and
    /// tailoring still fail that test — nothing in this port equips a crafted weapon or wears crafted
    /// apparel. <b>Meals do not.</b> Every citizen eats, <c>AI.JobGiver_GetFood</c> already sends a hungry one
    /// to the nearest edible Thing, and a meal is an edible Thing. So this is the one remaining bench whose
    /// demand figure is real, and this class is the somebody that wants it.
    ///
    /// <para/><b>What cooking is actually worth, and it is not flavour.</b> <c>CookMealSimple</c> spends 0.5
    /// nutrition of raw ingredients and produces a <c>MealSimple</c> worth 0.9 — RimWorld's own arithmetic,
    /// already in this port's content. Cooking is therefore the only step in the whole food chain that makes
    /// more nutrition than it consumes, and it very nearly doubles what a settlement gets out of whatever its
    /// foragers and farmers bring in. A settlement that cannot cook needs almost twice the raw supply to feed
    /// the same people, which is most of the difference between a food economy that closes and one that does
    /// not.
    ///
    /// <para/><b>Both halves had to land together.</b> Placing the bill was not enough: <c>FueledStove</c> had
    /// no Blueprint/Frame pair authored at all, so <see cref="GenConstruct.BlueprintDefFor"/> returned null
    /// for it and no stove could be raised on any map by anything. That content is in
    /// <c>Buildings_Kitchen.xml</c>, beside this class's own reason for needing it.
    ///
    /// <para/><b>Read off content, never off a new Def field.</b> A recipe is cooking when every product it
    /// makes is a meal (<see cref="Defs.ThingDef.IsMeal"/>); a bench is a stove when a cooking recipe names it
    /// in <see cref="RecipeDef.recipeUsers"/> and it carries a <see cref="CompProperties_BillGiver"/> — the
    /// same inversion <c>StonecutterInitiative</c> and <c>AI.WorkGiver_ButcherCorpse</c> both already use, so
    /// a fifth meal recipe shipped in content is found with no code change and no shared file is contested.
    ///
    /// <para/><b>No state of its own.</b> Everything is re-derived every gated pass from what is standing on
    /// the map, so there is nothing here to Scribe.
    /// </summary>
    public static class CookingInitiative
    {
        // -------------------------------------------------------------------------------------------
        // Entry points.
        // -------------------------------------------------------------------------------------------

        /// <summary>Civilization-wide entry point. A silent no-op with no world running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            if (world == null) return;
            foreach (Settlement settlement in world.Settlements) TickSettlement(settlement);
        }

        /// <summary>One settlement, reading its own <see cref="Settlement.InteriorMap"/> — null is a real,
        /// expected state and a no-op.</summary>
        public static void TickSettlement(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return;
            TickMap(map);
        }

        /// <summary>One map, self-gating on <see cref="CookingTuning.IntervalTicks"/>. Called once per map
        /// per tick from <see cref="Map.Map.MapTick"/>.</summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % CookingTuning.IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated logic. Public so a test can drive one pass without arranging for the tick
        /// number to land on the interval.</summary>
        public static void Run(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            List<RecipeDef> recipes = CookableRecipesOn(map);
            if (recipes.Count == 0) return; // Nothing researched, or nothing raw on the map to cook.

            EnsureStove(map, recipes);
            EnsureBills(map, recipes);
        }

        // -------------------------------------------------------------------------------------------
        // What the content says is cooking, and what this map can actually cook right now.
        // -------------------------------------------------------------------------------------------

        /// <summary>True when every product <paramref name="recipe"/> makes is a meal — read off the product,
        /// not off a new Def field.</summary>
        public static bool IsCooking(RecipeDef recipe)
        {
            if (recipe?.products == null || recipe.products.Count == 0) return false;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDef? product = recipe.products[i].thingDef;
                if (product == null || !product.IsMeal) return false;
            }
            return true;
        }

        /// <summary>A building this port would work a cooking bill at.</summary>
        public static bool IsStoveDef(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Building) return false;
            if (!HasBillGiverComp(def)) return false;
            IReadOnlyList<RecipeDef> recipes = def.AllRecipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (IsCooking(recipes[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Every cooking recipe the civilization has researched and has the ingredients for right now.
        /// Ordered by defName so two runs of the same state queue the same bills in the same order.
        /// </summary>
        public static List<RecipeDef> CookableRecipesOn(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var found = new List<RecipeDef>();
            IReadOnlyList<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef recipe = all[i];
                if (!IsCooking(recipe) || !recipe.AvailableNow) continue;
                if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) continue;
                if (!IngredientsOnMap(map, recipe)) continue;
                found.Add(recipe);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return found;
        }

        /// <summary>
        /// Whether every one of <paramref name="recipe"/>'s ingredient slots can be filled from something
        /// lying on <paramref name="map"/>. Unlike stonecutting's own version this cannot look for a fixed
        /// Def — every shipped meal recipe filters by <see cref="ThingCategoryDef"/> ("meat OR plant food"),
        /// which is the whole point of a meal — so it asks the slot's own
        /// <see cref="ThingFilter.Allows(ThingDef)"/> instead. Presence only, not amount: a bill that is
        /// briefly short of ingredients pauses by itself in <c>WorkGiver_DoBill</c>, and re-deriving exact
        /// quantities here would be a second, drifting copy of that giver's own accounting.
        /// </summary>
        public static bool IngredientsOnMap(Map.Map map, RecipeDef recipe)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (recipe?.ingredients == null || recipe.ingredients.Count == 0) return false;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                if (FirstIngredientStack(map, recipe.ingredients[i]) == null) return false;
            }
            return true;
        }

        /// <summary>The first spawned stack on the map this ingredient slot accepts, or null.</summary>
        private static Thing? FirstIngredientStack(Map.Map map, IngredientCount ingredient)
        {
            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.Item);
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.Spawned || t.stackCount <= 0) continue;
                if (ingredient.filter.Allows(t.def)) return t;
            }
            return null;
        }

        /// <summary>Where the kitchen goes: the first stack any cookable recipe would use. A stove has to
        /// stand among the food it cooks — see <see cref="CookingTuning.StovePlacementRadius"/>.</summary>
        private static Thing? RawFoodOn(Map.Map map, List<RecipeDef> recipes)
        {
            for (int r = 0; r < recipes.Count; r++)
            {
                List<IngredientCount>? ingredients = recipes[r].ingredients;
                if (ingredients == null) continue;
                for (int i = 0; i < ingredients.Count; i++)
                {
                    Thing? stack = FirstIngredientStack(map, ingredients[i]);
                    if (stack != null) return stack;
                }
            }
            return null;
        }

        // -------------------------------------------------------------------------------------------
        // The bench.
        // -------------------------------------------------------------------------------------------

        private static void EnsureStove(Map.Map map, List<RecipeDef> recipes)
        {
            ThingDef? stoveDef = StoveDefFor(recipes);
            if (stoveDef == null || !stoveDef.IsResearchFinished) return;
            if (CountBuiltOrPlanned(map, stoveDef) >= CookingTuning.StovesWanted) return;

            Thing? food = RawFoodOn(map, recipes);
            if (food == null) return;
            if (!TryFindPlacementCell(map, stoveDef, food.Position, out IntVec3 cell)) return;

            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(stoveDef);
            if (blueprintDef == null) return; // No Blueprint content authored for this bench — nothing to place.
            GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), cell, map);
        }

        /// <summary>The bench these recipes are cooked at — the first one any of them names, by defName
        /// order, so the choice does not move with Def load order.</summary>
        public static ThingDef? StoveDefFor(List<RecipeDef> recipes)
        {
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));

            ThingDef? best = null;
            for (int i = 0; i < recipes.Count; i++)
            {
                List<ThingDef>? users = recipes[i].recipeUsers;
                if (users == null) continue;
                for (int u = 0; u < users.Count; u++)
                {
                    ThingDef user = users[u];
                    if (!IsStoveDef(user)) continue;
                    if (best == null || string.CompareOrdinal(user.defName, best.defName) < 0) best = user;
                }
            }
            return best;
        }

        private static int CountBuiltOrPlanned(Map.Map map, ThingDef entityDef)
        {
            int count = map.listerThings.ThingsOfDef(entityDef).Count;

            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp && ReferenceEquals(bp.EntityToBuild, entityDef)) count++;
            }

            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f && ReferenceEquals(f.EntityToBuild, entityDef)) count++;
            }

            return count;
        }

        private static bool TryFindPlacementCell(Map.Map map, ThingDef entityDef, IntVec3 near, out IntVec3 cell)
        {
            CellRect scan = CellRect.CenteredOn(near, CookingTuning.StovePlacementRadius).ClipInsideMap(map);
            foreach (IntVec3 candidate in scan.Cells)
            {
                if (GenConstruct.CanPlaceBlueprintAt(entityDef, candidate, map, out _))
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = default;
            return false;
        }

        // -------------------------------------------------------------------------------------------
        // The bills.
        // -------------------------------------------------------------------------------------------

        private static void EnsureBills(Map.Map map, List<RecipeDef> recipes)
        {
            Settlement? settlement = HuntingInitiative.SettlementFor(map);

            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int b = 0; b < buildings.Count; b++)
            {
                Thing bench = buildings[b];
                if (!bench.Spawned) continue;
                CompBillGiver? comp = (bench as ThingWithComps)?.GetComp<CompBillGiver>();
                if (comp == null || !IsStoveDef(bench.def)) continue;

                for (int r = 0; r < recipes.Count; r++)
                {
                    RecipeDef recipe = recipes[r];
                    if (!Lists(bench.def.AllRecipes, recipe) || HasBillFor(comp, recipe)) continue;

                    Bill_Production bill = StandingBillFor(recipe, settlement, map);
                    if (bill.targetCount <= 0) continue;
                    comp.BillStack.AddBill(bill);
                }
            }
        }

        /// <summary>
        /// The bill this initiative queues: keep enough meals standing to cover what the settlement wants in
        /// hand. The demand figure is <see cref="HuntingInitiative.NutritionWanted"/> — the food economy's one
        /// demand figure, shared with the hunting gate and with <c>Building.FarmingInitiative</c>'s field size,
        /// rather than a third number of its own that could drift from the other two — divided by what one
        /// meal of this recipe is worth. A <see cref="BillRepeatMode.TargetCount"/> bill, so the kitchen stops
        /// when the larder is full and starts again when it runs down: RimWorld's own hysteresis, unchanged.
        /// </summary>
        public static Bill_Production StandingBillFor(RecipeDef recipe, Settlement? settlement, Map.Map map)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (map == null) throw new ArgumentNullException(nameof(map));

            float wantedNutrition = HuntingInitiative.NutritionWanted(settlement, map);
            float perMeal = NutritionPerBatch(recipe);
            int wanted = perMeal > 0f ? (int)Math.Ceiling(wantedNutrition / perMeal) : 0;

            return new Bill_Production(recipe)
            {
                repeatMode = BillRepeatMode.TargetCount,
                targetCount = wanted,

                // Half the target, the same batch-not-a-trickle band StonecutterInitiative uses and for the
                // same reason: a kitchen that restarts every time one person eats one meal is a kitchen
                // nobody else can use the bench of.
                unpauseWhenYouHave = wanted / 2,
            };
        }

        /// <summary>Nutrition one run of this recipe puts on the map.</summary>
        private static float NutritionPerBatch(RecipeDef recipe)
        {
            if (recipe.products == null) return 0f;
            float total = 0f;
            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDefCountClass product = recipe.products[i];
                ThingDef? def = product.thingDef;
                if (def?.ingestible == null) continue;
                total += def.ingestible.nutrition * product.count;
            }
            return total;
        }

        private static bool Lists(IReadOnlyList<RecipeDef> recipes, RecipeDef recipe)
        {
            for (int i = 0; i < recipes.Count; i++)
            {
                if (ReferenceEquals(recipes[i], recipe)) return true;
            }
            return false;
        }

        private static bool HasBillFor(CompBillGiver comp, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = comp.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (ReferenceEquals(bills[i].recipe, recipe)) return true;
            }
            return false;
        }

        private static bool HasBillGiverComp(ThingDef def)
        {
            if (def.comps == null) return false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is CompProperties_BillGiver) return true;
            }
            return false;
        }
    }
}
